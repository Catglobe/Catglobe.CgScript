using Antlr4.Runtime;
using Catglobe.CgScript.EditorSupport.Parsing;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Catglobe.CgScript.EditorSupport.Lsp.Handlers;

/// <summary>
/// Encodes QSL lexer tokens into the LSP semantic tokens integer array format.
/// Reuses the same token type legend as <see cref="SemanticTokensBuilder"/> so
/// that a single set of LSP capabilities covers both CgScript and QSL.
/// </summary>
public static class QslSemanticTokensBuilder
{
   // ── Token type indices — must match SemanticTokensBuilder.TokenTypes legend ──
   private const int TypeKeyword  = 0;
   private const int TypeString   = 1;
   private const int TypeNumber   = 2;
   private const int TypeOperator = 4;
   private const int TypeVariable = 5;
   private const int TypeProperty = 11;
   private const int TypeMacro    = 12;

   // No modifier bits needed for QSL tokens.
   private readonly record struct RawToken(int Line, int Col, int Length, int TypeIdx);

   // ── Public API ────────────────────────────────────────────────────────────

   /// <summary>Builds a full set of semantic tokens for <paramref name="text"/>.</summary>
   public static SemanticTokens Build(string text)
   {
      var raw = BuildRawTokens(text);
      return new SemanticTokens { Data = Encode(raw) };
   }

   /// <summary>Builds a range-filtered set of semantic tokens.</summary>
   public static SemanticTokens BuildRange(string text, int startLine0, int endLine0)
   {
      var filtered = BuildRawTokens(text).Where(t => t.Line >= startLine0 && t.Line <= endLine0);
      return new SemanticTokens { Data = Encode(filtered) };
   }

   // ── Core implementation ───────────────────────────────────────────────────

   private static List<RawToken> BuildRawTokens(string text)
   {
      var lexer  = new QslLexer(CharStreams.fromString(text));
      var stream = new CommonTokenStream(lexer);
      stream.Fill();

      var result = new List<RawToken>();

      var allTokens   = stream.GetTokens();
      int count       = allTokens.Count;

      // Contextual state for richer token classification.
      int    bracketDepth   = 0;   // depth of [ ... ] bracket nesting
      string lastPropName   = "";  // most recent property name Label (inside a bracket block)
      int    prevTokenType  = -1;  // type of the preceding non-EOF token

      for (int i = 0; i < count; i++)
      {
         var token = allTokens[i];
         if (token.Type == TokenConstants.EOF) break;

         int line   = token.Line - 1; // 0-based
         int col    = token.Column;
         int length = token.StopIndex - token.StartIndex + 1;

         // ── Track bracket depth ─────────────────────────────────────────────
         if (token.Type == QslLexer.LBRACK)
         {
            bracketDepth++;
            prevTokenType = token.Type;
            continue; // punctuation — no semantic token
         }
         if (token.Type == QslLexer.RBRACK)
         {
            if (bracketDepth > 0) bracketDepth--;
            lastPropName  = "";  // leaving property block
            prevTokenType = token.Type;
            continue; // punctuation — no semantic token
         }

         // ── COLON: operator when following SQ or an Int at depth 0 ──────────
         if (token.Type == QslLexer.COLON)
         {
            if (bracketDepth == 0 &&
               (prevTokenType == QslLexer.SQ || prevTokenType == QslLexer.Int))
            {
               result.Add(new RawToken(line, col, length, TypeOperator));
            }
            prevTokenType = token.Type;
            continue;
         }

         // ── String literals ──────────────────────────────────────────────────
         if (token.Type == QslLexer.StringLiteral)
         {
            int typeIdx;
            if (bracketDepth == 0)
            {
               // Outside property blocks: question display text, IF conditions, etc.
               // All are potentially HTML/script content → TypeMacro.
               typeIdx = TypeMacro;
            }
            else if (string.Equals(lastPropName, "CG_SCRIPT",  StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(lastPropName, "JAVA_SCRIPT", StringComparison.OrdinalIgnoreCase))
            {
               // Embedded script content in property values.
               typeIdx = TypeMacro;
            }
            else
            {
               typeIdx = TypeString;
            }

            // LSP semantic tokens must be single-line; split multi-line strings.
            EmitStringToken(result, token, typeIdx);
            prevTokenType = token.Type;
            continue;
         }

         // ── Label tokens ─────────────────────────────────────────────────────
         if (token.Type == QslLexer.Label)
         {
            // Look ahead for the next token to determine if this is a property name.
            int nextType = i + 1 < count ? allTokens[i + 1].Type : -1;
            if (nextType == QslLexer.EQUALS && bracketDepth > 0)
            {
               // Property name inside a bracket block.
               lastPropName = token.Text;
               result.Add(new RawToken(line, col, length, TypeProperty));
            }
            else
            {
               result.Add(new RawToken(line, col, length, TypeVariable));
            }
            prevTokenType = token.Type;
            continue;
         }

         // ── All other typed tokens ───────────────────────────────────────────
         int typeIndex = GetTokenType(token.Type);
         if (typeIndex >= 0)
            result.Add(new RawToken(line, col, length, typeIndex));

         prevTokenType = token.Type;
         // ERROR and unrecognised tokens are intentionally skipped.
      }

      result.Sort((a, b) => a.Line != b.Line ? a.Line - b.Line : a.Col - b.Col);
      return result;
   }

   /// <summary>
   /// Emits one <see cref="RawToken"/> per source line for a (potentially multi-line)
   /// string literal token.  LSP semantic tokens are strictly single-line.
   /// </summary>
   private static void EmitStringToken(List<RawToken> result, IToken token, int typeIdx)
   {
      string text     = token.Text;
      int startLine0  = token.Line - 1; // 0-based
      int startCol    = token.Column;

      if (!text.Contains('\n'))
      {
         // Fast path for the common single-line case.
         result.Add(new RawToken(startLine0, startCol, text.Length, typeIdx));
         return;
      }

      // Split on actual newlines (not escape sequences — those are two chars in source).
      string[] parts = text.Split('\n');
      for (int p = 0; p < parts.Length; p++)
      {
         string part = parts[p];

         // Strip trailing \r from Windows-style line endings.
         if (part.Length > 0 && part[part.Length - 1] == '\r')
            part = part.Substring(0, part.Length - 1);

         int len = part.Length;
         if (len == 0) continue; // skip empty continuation lines

         int lineIdx = startLine0 + p;
         int colIdx  = p == 0 ? startCol : 0;

         result.Add(new RawToken(lineIdx, colIdx, len, typeIdx));
      }
   }

   /// <summary>
   /// Maps a QSL lexer token type to an LSP semantic token type index,
   /// or returns <c>-1</c> if the token should not produce a semantic token.
   /// </summary>
   private static int GetTokenType(int t) => t switch
   {
      // ── Structural and control keywords ───────────────────────────────────
      QslLexer.QUESTIONNAIRE or QslLexer.QUESTION or QslLexer.GROUP or QslLexer.END
      or QslLexer.SQ or QslLexer.GOTO or QslLexer.IF or QslLexer.REPLACE or QslLexer.WITH
      or QslLexer.ORCLEARCURRENT or QslLexer.ANDCLEAR
      or QslLexer.FIRST or QslLexer.LAST or QslLexer.AFTER or QslLexer.BEFORE
      or QslLexer.AS or QslLexer.QUESTIONS or QslLexer.HIDE or QslLexer.IN
      => TypeKeyword,

      // ── Question type keywords ─────────────────────────────────────────────
      QslLexer.PAGE or QslLexer.SINGLE or QslLexer.MULTI or QslLexer.SCALE
      or QslLexer.NUMBER or QslLexer.TEXT or QslLexer.OPEN or QslLexer.MULTIMEDIA
      or QslLexer.SINGLEGRID or QslLexer.MULTIGRID or QslLexer.SCALEGRID or QslLexer.TEXTGRID
      => TypeKeyword,

      // ── Condition / range filter type keywords ────────────────────────────
      QslLexer.INC_AO or QslLexer.EXC_AO or QslLexer.INC_SQ or QslLexer.EXC_SQ
      or QslLexer.INC_AO_FROM or QslLexer.EXC_AO_FROM
      => TypeKeyword,

      // ── Group / sequence type keywords ────────────────────────────────────
      QslLexer.RANDOM or QslLexer.ROTATE or QslLexer.SEQUENCE or QslLexer.SHOW
      => TypeKeyword,

      // ── Boolean literals ──────────────────────────────────────────────────
      QslLexer.TRUE or QslLexer.FALSE => TypeKeyword,

      // ── Numeric literals ──────────────────────────────────────────────────
      QslLexer.Int or QslLexer.Float => TypeNumber,

      // Label and StringLiteral are handled in the main loop above.
      // Punctuation, WS (skipped by lexer), and ERROR are intentionally omitted.
      _ => -1,
   };

   private static int[] Encode(IEnumerable<RawToken> tokens)
   {
      var data          = new List<int>();
      int prevLine      = 0;
      int prevStartChar = 0;

      foreach (var t in tokens)
      {
         int deltaLine      = t.Line - prevLine;
         int deltaStartChar = deltaLine == 0 ? t.Col - prevStartChar : t.Col;

         data.Add(deltaLine);
         data.Add(deltaStartChar);
         data.Add(t.Length);
         data.Add(t.TypeIdx);
         data.Add(0); // no modifiers

         prevLine      = t.Line;
         prevStartChar = t.Col;
      }

      return data.ToArray();
   }
}
