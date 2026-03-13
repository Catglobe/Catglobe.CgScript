using Antlr4.Runtime;
using Catglobe.CgScript.EditorSupport.Parsing;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using System.Text;

namespace Catglobe.CgScript.EditorSupport.Lsp.Handlers;

/// <summary>
/// Token-stream formatter for CgScript source code.
/// Produces output similar to clang default style:
/// <list type="bullet">
///   <item>Space between control-flow keywords and their <c>(</c></item>
///   <item>2-space (or tab-size from options) indentation for statement bodies</item>
///   <item>Spaces around binary operators</item>
///   <item>Space after commas</item>
/// </list>
/// </summary>
internal static class CgScriptFormatter
{
   /// <summary>Formats <paramref name="source"/> and returns the formatted text.</summary>
   public static string Format(string source, FormattingOptions? options = null)
   {
      int    tabSize      = options?.TabSize ?? 2;
      bool   insertSpaces = options?.InsertSpaces ?? true;
      string indentUnit   = insertSpaces ? new string(' ', tabSize) : "\t";

      var lexer     = new CgScriptLexer(CharStreams.fromString(source));
      var allStream = new CommonTokenStream(lexer);
      allStream.Fill();
      var tokens = allStream.GetTokens();

      var output = new StringBuilder(source.Length + 128);

      // Track indent level driven by curly braces.
      int  indentDepth = 0;

      // Whether the current output line is empty (nothing has been written on it yet).
      bool lineEmpty   = true;

      // Previous default-channel token type (for spacing decisions).
      int  prevType    = -1;

      // State-machine for detecting non-block control-flow bodies:
      //   NONE        – not inside a control-flow header
      //   IN_HEADER   – saw if/while/for/switch, counting parens to find closing ')'
      //   AFTER_COND  – condition closed, next token decides: '{' or single-statement
      // We use a stack to handle nested cases (e.g. if inside for header).
      // Each entry: (parenDepth, keyword).
      var cfStack = new Stack<(int ParenDepth, int Keyword)>();

      // How many "single-statement body" extra indent levels are currently active.
      // We pop one off after each SEMI that terminates such a body.
      var bodyIndentStack = new Stack<int>(); // stores indentDepth at time of push

      // When true, a '\n' is emitted immediately after the current token.
      bool emitNewlineAfterCurrent = false;

      // Track paren/bracket nesting depth to distinguish dictionary-level commas
      // (which should produce a newline) from commas inside function calls or arrays.
      int parenDepth   = 0;
      int bracketDepth = 0;

      // Stack per open '{': records (parenDepth, bracketDepth) at the time '{' was
      // opened, plus whether this '{...}' should be expanded across multiple lines.
      var curlyParenStack = new Stack<(int Paren, int Bracket, bool Expanded)>();

      // Current column on the output line, used to decide inline vs. expanded for '{...}'.
      int currentCol = 0;

      for (int idx = 0; idx < tokens.Count; idx++)
      {
         var tok = tokens[idx];
         if (tok.Type == TokenConstants.EOF) break;

         // ── hidden-channel tokens: comments pass through; WS is discarded ─────
         if (tok.Channel != Lexer.DefaultTokenChannel)
         {
            if (tok.Type == CgScriptLexer.SL_COMMENT)
            {
               if (!lineEmpty) { output.Append(' '); currentCol++; }
               else { EmitIndent(output, indentUnit, indentDepth); currentCol = indentDepth * tabSize; }
               output.Append(tok.Text.TrimEnd());
               output.Append('\n');
               lineEmpty = true;
               currentCol = 0;
            }
            else if (tok.Type == CgScriptLexer.ML_COMMENT)
            {
               if (!lineEmpty) { output.Append(' '); currentCol++; }
               else { EmitIndent(output, indentUnit, indentDepth); currentCol = indentDepth * tabSize; }
               output.Append(tok.Text);
               if (tok.Text.Contains('\n'))
               {
                  output.Append('\n');
                  lineEmpty = true;
                  currentCol = 0;
               }
               else
               {
                  lineEmpty = false;
                  currentCol += tok.Text.Length;
               }
            }
            // Plain WS discarded.
            continue;
         }

         // ── default-channel token ─────────────────────────────────────────────
         int type = tok.Type;
         emitNewlineAfterCurrent = false;

         // ── control-flow header tracking ──────────────────────────────────────
         // Update paren depth while inside a control-flow header.
         if (cfStack.Count > 0)
         {
            var top = cfStack.Peek();
            if (type == CgScriptLexer.LPAREN)
            {
               cfStack.Pop();
               cfStack.Push((top.ParenDepth + 1, top.Keyword));
            }
            else if (type == CgScriptLexer.RPAREN)
            {
               cfStack.Pop();
               if (top.ParenDepth == 1)
               {
                  // Condition closed.  Peek: is the next meaningful token '{'?
                  int next = PeekNextMeaningful(tokens, idx);
                  if (next != CgScriptLexer.LCURLY && next != TokenConstants.EOF)
                  {
                     // Single-statement body: push an extra indent level and
                     // force a newline after the closing ')'.
                     bodyIndentStack.Push(indentDepth);
                     indentDepth++;
                     emitNewlineAfterCurrent = true;
                  }
                  // else: block body – indent managed by the '{' itself.
               }
               else
               {
                  cfStack.Push((top.ParenDepth - 1, top.Keyword));
               }
            }
         }

         // ── LCURLY: look ahead to decide inline vs. expanded ─────────────────
         bool lcurlyExpanded = true; // only meaningful when type == LCURLY
         if (type == CgScriptLexer.LCURLY)
         {
            var (hasComma, inlineLen) = InspectCurlyBlock(tokens, idx);
            if (hasComma)
            {
               int colBeforeOpen = lineEmpty
                  ? indentDepth * tabSize
                  : currentCol + (NeedsSpaceBefore(CgScriptLexer.LCURLY, prevType) ? 1 : 0);
               lcurlyExpanded = colBeforeOpen + inlineLen > PrintWidth;
            }
         }

         // ── RCURLY: check whether the matching LCURLY was expanded ────────────
         bool rcurlyExpanded = type == CgScriptLexer.RCURLY
            && curlyParenStack.Count > 0 && curlyParenStack.Peek().Expanded;

         // ── pre-emit: update indent before RCURLY ─────────────────────────────
         if (type == CgScriptLexer.RCURLY && rcurlyExpanded)
         {
            // Pop any pending body indents for single-statement bodies whose
            // terminating SEMI was never seen (e.g. last statement in a block).
            // We compare against indentDepth - 1 because indentDepth hasn't been
            // decremented yet; a saved depth equal to the current depth - 1 means
            // the indent was pushed at the enclosing block's level.
            while (bodyIndentStack.Count > 0 && bodyIndentStack.Peek() >= indentDepth - 1)
               indentDepth = bodyIndentStack.Pop();
            indentDepth = Math.Max(0, indentDepth - 1);
         }

         // ── emit leading whitespace / indent ──────────────────────────────────
         if (lineEmpty)
         {
            EmitIndent(output, indentUnit, indentDepth);
            currentCol = indentDepth * tabSize;
            lineEmpty = false;
         }
         else
         {
            if (NeedsSpaceBefore(type, prevType)) { output.Append(' '); currentCol++; }
         }

         // ── emit token text ───────────────────────────────────────────────────
         output.Append(tok.Text);
         currentCol += tok.Text.Length;

         // ── post-emit: forced newline from condition-close detection ──────────
         if (emitNewlineAfterCurrent)
         {
            output.Append('\n');
            lineEmpty = true;
            currentCol = 0;
         }

         // ── post-emit bookkeeping ─────────────────────────────────────────────
         switch (type)
         {
            case CgScriptLexer.IF:
            case CgScriptLexer.WHILE:
            case CgScriptLexer.FOR:
            case CgScriptLexer.SWITCH:
               // Begin tracking the control-flow condition header.
               cfStack.Push((0, type));
               break;

            case CgScriptLexer.ELSE:
            {
               // After 'else': if the next token is not '{' and not 'if', the body is
               // a single statement that needs an extra indent level.
               int nextElse = PeekNextMeaningful(tokens, idx);
               if (nextElse != CgScriptLexer.LCURLY && nextElse != CgScriptLexer.IF
                   && nextElse != TokenConstants.EOF)
               {
                  bodyIndentStack.Push(indentDepth);
                  indentDepth++;
                  output.Append('\n');
                  lineEmpty = true;
                  currentCol = 0;
               }
               break;
            }

            case CgScriptLexer.LCURLY:
               curlyParenStack.Push((parenDepth, bracketDepth, lcurlyExpanded));
               if (lcurlyExpanded)
               {
                  indentDepth++;
                  output.Append('\n');
                  lineEmpty = true;
                  currentCol = 0;
               }
               break;

            case CgScriptLexer.RCURLY:
               if (curlyParenStack.Count > 0) curlyParenStack.Pop();
               // For expanded blocks: emit a newline unless followed by else/catch/;/,
               int nextAfterBrace = PeekNextMeaningful(tokens, idx);
               if (!rcurlyExpanded
                   || nextAfterBrace == CgScriptLexer.ELSE || nextAfterBrace == CgScriptLexer.CATCH
                   || nextAfterBrace == CgScriptLexer.SEMI || nextAfterBrace == CgScriptLexer.COMMA)
               {
                  // Keep on same line.
                  lineEmpty = false;
               }
               else
               {
                  output.Append('\n');
                  lineEmpty = true;
                  currentCol = 0;
               }
               break;

            case CgScriptLexer.LPAREN:
               parenDepth++;
               break;

            case CgScriptLexer.RPAREN:
               if (parenDepth > 0) parenDepth--;
               break;

            case CgScriptLexer.LBRACKET:
               bracketDepth++;
               break;

            case CgScriptLexer.RBRACKET:
               if (bracketDepth > 0) bracketDepth--;
               break;

            case CgScriptLexer.COMMA:
               // If the comma is at the top level of an expanded dictionary/array literal,
               // put each entry on its own line.
               if (curlyParenStack.Count > 0)
               {
                  var top = curlyParenStack.Peek();
                  if (parenDepth == top.Paren && bracketDepth == top.Bracket && top.Expanded)
                  {
                     output.Append('\n');
                     lineEmpty = true;
                     currentCol = 0;
                  }
               }
               break;

            case CgScriptLexer.SEMI:
               // Pop one non-block body indent if we were in one.
               if (bodyIndentStack.Count > 0)
                  indentDepth = bodyIndentStack.Pop();
               output.Append('\n');
               lineEmpty = true;
               currentCol = 0;
               break;
         }

         prevType = type;
      }

      var result = output.ToString().TrimEnd();
      return result.Length == 0 ? string.Empty : result + '\n';
   }

   // ── helpers ───────────────────────────────────────────────────────────────

   /// <summary>Maximum line length before a <c>{...}</c> literal is formatted across multiple lines.</summary>
   private const int PrintWidth = 80;

   private static void EmitIndent(StringBuilder sb, string unit, int depth)
   {
      for (int i = 0; i < depth; i++) sb.Append(unit);
   }

   /// <summary>Returns <c>true</c> when a space should be inserted before <paramref name="type"/>.</summary>
   private static bool NeedsSpaceBefore(int type, int prevType)
   {
      if (prevType < 0) return false;

      // No space after open-paren / open-bracket / open-brace / dot / unary-not.
      switch (prevType)
      {
         case CgScriptLexer.LPAREN:
         case CgScriptLexer.LBRACKET:
         case CgScriptLexer.LCURLY:
         case CgScriptLexer.DOT:
         case CgScriptLexer.NOT:
            return false;
      }

      // No space before: , ; . ) ] }
      switch (type)
      {
         case CgScriptLexer.COMMA:
         case CgScriptLexer.SEMI:
         case CgScriptLexer.DOT:
         case CgScriptLexer.RPAREN:
         case CgScriptLexer.RBRACKET:
         case CgScriptLexer.RCURLY:
            return false;
      }

      // No space before [ (array-access / array-literal).
      if (type == CgScriptLexer.LBRACKET) return false;

      // Space before ( only when previous token is a control-flow keyword.
      if (type == CgScriptLexer.LPAREN)
         return IsControlKeyword(prevType);

      // Space before {.
      if (type == CgScriptLexer.LCURLY) return true;

      // ++ / --: no spaces around them.
      if (type == CgScriptLexer.INC || type == CgScriptLexer.DEC) return false;
      if (prevType == CgScriptLexer.INC || prevType == CgScriptLexer.DEC) return false;

      // Space after comma.
      if (prevType == CgScriptLexer.COMMA) return true;

      // Binary operators: space on both sides (with unary exception).
      if (IsBinaryOperator(type))
      {
         if ((type == CgScriptLexer.PLUS || type == CgScriptLexer.MINUS)
             && IsUnaryPosition(prevType))
            return false;
         return true;
      }
      if (IsBinaryOperator(prevType)) return true;

      // else / catch: always a space before.
      if (type == CgScriptLexer.ELSE || type == CgScriptLexer.CATCH) return true;

      // Word-token adjacency.
      return IsWordToken(prevType) && IsWordToken(type);
   }

   private static bool IsUnaryPosition(int prevType)
   {
      if (prevType < 0) return true;
      switch (prevType)
      {
         case CgScriptLexer.LPAREN:
         case CgScriptLexer.LBRACKET:
         case CgScriptLexer.COMMA:
         case CgScriptLexer.SEMI:
         case CgScriptLexer.ASSIGN:
         case CgScriptLexer.LCURLY:
         case CgScriptLexer.COLON:
            return true;
      }
      return IsBinaryOperator(prevType);
   }

   private static bool IsControlKeyword(int t) =>
      t == CgScriptLexer.IF     || t == CgScriptLexer.WHILE   ||
      t == CgScriptLexer.FOR    || t == CgScriptLexer.SWITCH  ||
      t == CgScriptLexer.CATCH  || t == CgScriptLexer.ELSE    ||
      t == CgScriptLexer.NEW    || t == CgScriptLexer.RETURN  ||
      t == CgScriptLexer.THROW;

   private static bool IsBinaryOperator(int t)
   {
      switch (t)
      {
         case CgScriptLexer.ASSIGN:
         case CgScriptLexer.EQUALS:
         case CgScriptLexer.NOT_EQUALS:
         case CgScriptLexer.LTEQ:
         case CgScriptLexer.GTEQ:
         case CgScriptLexer.AND:
         case CgScriptLexer.OR:
         case CgScriptLexer.LTHAN:
         case CgScriptLexer.GTHAN:
         case CgScriptLexer.PLUS:
         case CgScriptLexer.MINUS:
         case CgScriptLexer.STAR:
         case CgScriptLexer.DIV:
         case CgScriptLexer.MOD:
         case CgScriptLexer.POW:
         case CgScriptLexer.QMARK:    // '?' ternary operator
         case CgScriptLexer.COLON:
         case CgScriptLexer.WHERE:
            return true;
      }
      return false;
   }

   private static bool IsWordToken(int t)
   {
      switch (t)
      {
         case CgScriptLexer.IDENTIFIER:
         case CgScriptLexer.NUM_INT:
         case CgScriptLexer.NUM_DOUBLE:
         case CgScriptLexer.STRING_LITERAL:
         case CgScriptLexer.CHAR_LITERAL:
         case CgScriptLexer.DATE_LITERAL:
         case CgScriptLexer.TRUE:
         case CgScriptLexer.FALSE:
         case CgScriptLexer.EMPTY:
            return true;
      }
      return IsKeyword(t);
   }

   private static bool IsKeyword(int t)
   {
      switch (t)
      {
         case CgScriptLexer.IF:
         case CgScriptLexer.ELSE:
         case CgScriptLexer.WHILE:
         case CgScriptLexer.FOR:
         case CgScriptLexer.SWITCH:
         case CgScriptLexer.CASE:
         case CgScriptLexer.DEFAULT:
         case CgScriptLexer.TRY:
         case CgScriptLexer.CATCH:
         case CgScriptLexer.RETURN:
         case CgScriptLexer.THROW:
         case CgScriptLexer.BREAK:
         case CgScriptLexer.CONTINUE:
         case CgScriptLexer.NEW:
         case CgScriptLexer.BOOL:
         case CgScriptLexer.NUMBER:
         case CgScriptLexer.STRING:
         case CgScriptLexer.ARRAY:
         case CgScriptLexer.OBJECT:
         case CgScriptLexer.QUESTION: // 'question' type keyword (distinct from QMARK '?' operator)
         case CgScriptLexer.FUNCTION:
         case CgScriptLexer.WHERE:
         case CgScriptLexer.NOT:
            return true;
      }
      return false;
   }

   /// <summary>
   /// Returns the type of the next default-channel token after index <paramref name="idx"/>,
   /// or -1 if none exists before EOF.
   /// </summary>
   private static int PeekNextMeaningful(IList<IToken> tokens, int idx)
   {
      for (int i = idx + 1; i < tokens.Count; i++)
      {
         var t = tokens[i];
         if (t.Type == TokenConstants.EOF) break;
         if (t.Channel == Lexer.DefaultTokenChannel) return t.Type;
      }
      return -1;
   }

   /// <summary>
   /// Scans from the LCURLY at <paramref name="lcurlyIdx"/> to its matching RCURLY,
   /// returning whether any comma appears at the top level of the block (not nested inside
   /// parens, brackets, or inner braces) and the total character count as if the entire
   /// block were formatted on a single line (including the <c>{</c> and <c>}</c>).
   /// </summary>
   private static (bool HasTopLevelComma, int InlineLength) InspectCurlyBlock(IList<IToken> tokens, int lcurlyIdx)
   {
      int curlyDepth   = 1;
      int parenDepth   = 0;
      int bracketDepth = 0;
      bool hasComma    = false;
      int  length      = 1; // for '{'
      int  prevT       = CgScriptLexer.LCURLY;

      for (int i = lcurlyIdx + 1; i < tokens.Count; i++)
      {
         var tok = tokens[i];
         if (tok.Type == TokenConstants.EOF) break;
         if (tok.Channel != Lexer.DefaultTokenChannel) continue;

         int t = tok.Type;
         if (NeedsSpaceBefore(t, prevT)) length++;
         length += tok.Text.Length;

         switch (t)
         {
            case CgScriptLexer.LCURLY:   curlyDepth++;                                                   break;
            case CgScriptLexer.RCURLY:   if (curlyDepth > 0) { curlyDepth--; if (curlyDepth == 0) goto done; } break;
            case CgScriptLexer.LPAREN:   parenDepth++;                                break;
            case CgScriptLexer.RPAREN:   if (parenDepth > 0) parenDepth--;            break;
            case CgScriptLexer.LBRACKET: bracketDepth++;                              break;
            case CgScriptLexer.RBRACKET: if (bracketDepth > 0) bracketDepth--;        break;
            case CgScriptLexer.COMMA:
               if (curlyDepth == 1 && parenDepth == 0 && bracketDepth == 0)
                  hasComma = true;
               break;
         }
         prevT = t;
      }
      done:
      return (hasComma, length);
   }
}
