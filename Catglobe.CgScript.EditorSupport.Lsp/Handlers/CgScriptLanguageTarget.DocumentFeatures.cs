using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Catglobe.CgScript.EditorSupport.Parsing;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;
using System.Collections.Concurrent;
using System.Threading;
using LspDiagnostic = Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic;
using LspRange      = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace Catglobe.CgScript.EditorSupport.Lsp.Handlers;

public partial class CgScriptLanguageTarget
{
   // ── document highlight ────────────────────────────────────────────────────────

   /// <summary>
   /// Highlights all occurrences of the symbol under the cursor.
   /// Declaration sites are marked <see cref="DocumentHighlightKind.Write"/>;
   /// read sites are marked <see cref="DocumentHighlightKind.Read"/>.
   /// </summary>
   public DocumentHighlight[] OnDocumentHighlight(DocumentHighlightParams p)
   {
      var uri    = p.TextDocument.Uri.ToString();
      var result = _store.GetParseResult(uri);
      var text   = _store.GetText(uri) ?? string.Empty;
      var lines  = text.Split('\n');
      var highlights = new List<DocumentHighlight>();

      // Case 1: cursor on name="xxx" in xmldoc
      var docParam = TryGetDocParamNameAtCursor(lines, p.Position.Line, p.Position.Character);
      if (docParam is not null)
      {
         // Resolve to code references
         var codePos = FindParamInFunctionForDocBlock(lines, p.Position.Line, docParam.Value.Name);
         var refs = (result is not null && codePos is not null)
            ? ReferenceAnalyzer.FindReferences(result.Tree, codePos.Value.AntlrLine, codePos.Value.Col)
            : Array.Empty<SymbolRef>();
         foreach (var r in refs)
            highlights.Add(new DocumentHighlight { Range = ToRange(r), Kind = r.IsDeclaration ? DocumentHighlightKind.Write : DocumentHighlightKind.Read });
         // Xmldoc occurrences
         AddXmlDocHighlights(highlights, lines, p.Position.Line, docParam.Value.Name);
         return highlights.ToArray();
      }

      if (result is null) return [];

      // Case 2: cursor in code
      var codeRefs = ReferenceAnalyzer.FindReferences(result.Tree, p.Position.Line + 1, p.Position.Character);
      if (codeRefs.Count == 0) return [];
      foreach (var r in codeRefs)
         highlights.Add(new DocumentHighlight { Range = ToRange(r), Kind = r.IsDeclaration ? DocumentHighlightKind.Write : DocumentHighlightKind.Read });

      var wordAtCursor = GetWordAt(text, GetOffset(text, p.Position.Line, p.Position.Character));
      if (!string.IsNullOrEmpty(wordAtCursor))
      {
         foreach (var occ in FindXmlDocOccurrencesForCodeParam(text, wordAtCursor, codeRefs))
            highlights.Add(new DocumentHighlight { Range = new LspRange { Start = new Position(occ.LspLine, occ.NameStart), End = new Position(occ.LspLine, occ.NameStart + occ.NameLen) }, Kind = DocumentHighlightKind.Text });
      }

      return highlights.ToArray();
   }

   /// <summary>Finds xmldoc occurrences and adds them as <see cref="DocumentHighlightKind.Text"/> highlights.</summary>
   private static void AddXmlDocHighlights(List<DocumentHighlight> highlights, string[] lines, int cursorLine, string paramName)
   {
      // Find the function line below the doc block
      int funcLine = -1;
      for (int i = cursorLine + 1; i < lines.Length; i++)
      {
         var t = lines[i].TrimStart();
         if (t.StartsWith("///") || t.Length == 0) continue;
         if (t.IndexOf("function", StringComparison.Ordinal) >= 0) funcLine = i;
         break;
      }
      if (funcLine < 0) return;
      foreach (var (lspLine, nameStart, nameLen) in FindXmlDocNameOccurrences(lines, funcLine, paramName))
         highlights.Add(new DocumentHighlight { Range = new LspRange { Start = new Position(lspLine, nameStart), End = new Position(lspLine, nameStart + nameLen) }, Kind = DocumentHighlightKind.Text });
   }

   // ── document symbols ──────────────────────────────────────────────────────────

   /// <summary>
   /// Returns all top-level symbol declarations in the document as
   /// <see cref="SymbolInformation"/> entries (compatible with LSP 3.15 clients).
   /// </summary>
   public SymbolInformation[] OnDocumentSymbol(DocumentSymbolParams p)
   {
      var uri    = p.TextDocument.Uri.ToString();
      var result = _store.GetParseResult(uri);
      if (result is null) return [];

      var symbols = DocumentSymbolCollector.Collect(result.Tree);
      if (symbols.Count == 0) return [];

      return symbols
         .Select(s => new SymbolInformation
         {
            Name     = s.Name,
            Kind     = s.Kind == "function" ? SymbolKind.Function : SymbolKind.Variable,
            Location = new Location
            {
               Uri   = p.TextDocument.Uri,
               Range = new LspRange
               {
                  Start = new Position(s.StartLine - 1, s.StartColumn),
                  End   = new Position(s.EndLine   - 1, s.EndColumn),
               },
            },
         })
         .ToArray();
   }

   // ── folding ranges ────────────────────────────────────────────────────────────

   /// <summary>
   /// Returns folding ranges derived from matching curly-brace pairs and
   /// multi-line block comments.
   /// </summary>
   public FoldingRange[] OnFoldingRange(FoldingRangeParams p)
   {
      var text   = _store.GetText(p.TextDocument.Uri.ToString()) ?? string.Empty;
      var lexer  = new CgScriptLexer(CharStreams.fromString(text));
      var stream = new CommonTokenStream(lexer);
      stream.Fill();

      var result = new List<FoldingRange>();
      var stack  = new Stack<int>(); // LCURLY start lines (0-based)

      foreach (var token in stream.GetTokens())
      {
         if (token.Type == TokenConstants.EOF) break;

         switch (token.Type)
         {
            case CgScriptLexer.LCURLY:
               stack.Push(token.Line - 1);
               break;

            case CgScriptLexer.RCURLY:
               if (stack.Count > 0)
               {
                  int startLine = stack.Pop();
                  int endLine   = token.Line - 1;
                  if (startLine < endLine)
                  {
                     result.Add(new FoldingRange
                     {
                        StartLine = startLine,
                        EndLine   = endLine - 1, // standard: close-brace line not folded
                        Kind      = FoldingRangeKind.Region,
                     });
                  }
               }
               break;

            case CgScriptLexer.ML_COMMENT:
            {
               int startLine = token.Line - 1;
               int newlines  = token.Text.Count(c => c == '\n');
               if (newlines > 0)
               {
                  result.Add(new FoldingRange
                  {
                     StartLine = startLine,
                     EndLine   = startLine + newlines,
                     Kind      = FoldingRangeKind.Comment,
                  });
               }
               break;
            }
         }
      }

      return result.ToArray();
   }

   // ── selection range ───────────────────────────────────────────────────────────

   /// <summary>
   /// Returns a chain of progressively larger selection ranges around each requested
   /// cursor position by walking the ANTLR4 parse tree from the innermost matching
   /// node to the root.
   /// </summary>
   /// <remarks>
   /// <c>textDocument/selectionRange</c> is registered via a raw method-name string
   /// because VS LSP 17.2.x does not expose typed constants or parameter types for it.
   /// The param/result types are defined locally in <see cref="SelectionRangeTypes"/>.
   /// </remarks>
   public CgSelectionRange[]? OnSelectionRange(SelectionRangeParams p)
   {
      var uri    = p.TextDocument.Uri.ToString();
      var result = _store.GetParseResult(uri);
      if (result is null) return null;

      var output = new CgSelectionRange[p.Positions.Length];

      for (int i = 0; i < p.Positions.Length; i++)
      {
         var pos   = p.Positions[i];
         var chain = SelectionRangeHelper.GetNodeChain(
            result.Tree,
            cursorLine1:    pos.Line + 1,   // LSP 0-based → ANTLR 1-based
            cursorColumn0:  pos.Character);

         // Build the linked list from outermost → innermost so that the
         // innermost SelectionRange's .Parent points to the next wider range.
         CgSelectionRange? current = null;
         for (int j = chain.Count - 1; j >= 0; j--)
         {
            var range = NodeToLspRange(chain[j]);
            if (range is null) continue;
            current = new CgSelectionRange { Range = range, Parent = current };
         }

         output[i] = current ?? new CgSelectionRange
         {
            Range = new LspRange { Start = pos, End = pos },
         };
      }

      return output;
   }

   // ── code actions ──────────────────────────────────────────────────────────────

   /// <summary>
   /// Returns code actions for the given range:
   /// <list type="bullet">
   ///   <item>Quick-fix: remove CGS009 "unused variable" declarations.</item>
   ///   <item>Refactor: extract selected expression to a new variable.</item>
   /// </list>
   /// </summary>
   public SumType<Command, CodeAction>[] OnCodeAction(CodeActionParams p)
   {
      var uri         = p.TextDocument.Uri.ToString();
      var lspDiagList = p.Context?.Diagnostics ?? [];
      var actions     = new List<SumType<Command, CodeAction>>();

      // ── quick fix: remove unused variable ────────────────────────────────────
      foreach (var lspDiag in lspDiagList)
      {
         var msg = lspDiag.Message ?? string.Empty;
         if (!msg.Contains("declared but never used")) continue;

         int quoteOpen  = msg.IndexOf('\'');
         if (quoteOpen < 0) continue;
         int quoteClose = msg.IndexOf('\'', quoteOpen + 1);
         if (quoteClose <= quoteOpen) continue;
         var name  = msg.Substring(quoteOpen + 1, quoteClose - quoteOpen - 1);
         int line0 = lspDiag.Range.Start.Line;

         actions.Add(new SumType<Command, CodeAction>(new CodeAction
         {
            Title       = $"Remove unused variable '{name}'",
            Kind        = CodeActionKind.QuickFix,
            Diagnostics = [lspDiag],
            Edit        = new WorkspaceEdit
            {
               Changes = new Dictionary<string, TextEdit[]>
               {
                  [uri] =
                  [
                     new TextEdit
                     {
                        Range   = new LspRange { Start = new Position(line0, 0), End = new Position(line0 + 1, 0) },
                        NewText = string.Empty,
                     },
                  ],
               },
            },
         }));
      }

      // ── refactor: convert C-style for loop to native CgScript for loop ────────
      // Matches: for(type? name = start; name < end; name++) { … }
      // Emits:   for(name for start; end) { … }
      var forText   = _store.GetText(uri);
      var cursorLine = p.Range.Start.Line;
      if (forText is not null)
      {
         var forLines = forText.Split('\n');
         if (cursorLine < forLines.Length)
         {
            var line = forLines[cursorLine];
            var nativeFor = TryConvertCStyleFor(line);
            if (nativeFor is not null)
            {
               var lineEnd = line.TrimEnd('\r').Length;
               actions.Add(new SumType<Command, CodeAction>(new CodeAction
               {
                  Title = "Use CgScript native for loop",
                  Kind  = CodeActionKind.Refactor,
                  Edit  = new WorkspaceEdit
                  {
                     Changes = new Dictionary<string, TextEdit[]>
                     {
                        [uri] =
                        [
                           new TextEdit
                           {
                              Range   = new LspRange { Start = new Position(cursorLine, 0), End = new Position(cursorLine, lineEnd) },
                              NewText = nativeFor,
                           },
                        ],
                     },
                  },
               }));
            }
         }
      }

      // ── refactor: extract expression to variable (parse-tree based) ─────────
      // Cursor = normalised range start (works regardless of selection direction).
      var rawA   = p.Range.Start;
      var rawB   = p.Range.End;
      var cursor = (rawA.Line < rawB.Line || (rawA.Line == rawB.Line && rawA.Character <= rawB.Character))
                   ? rawA : rawB;

      var extractText   = _store.GetText(uri);
      var extractResult = _store.GetParseResult(uri);
      if (extractText != null && extractResult != null)
      {
         // ANTLR lines are 1-based; columns are 0-based (same as LSP).
         var chain = SelectionRangeHelper.GetNodeChain(
            extractResult.Tree, cursor.Line + 1, cursor.Character);

         var node = FindExtractableNode(chain);
         if (node is ParserRuleContext { Start: { } tokStart, Stop: { } tokStop })
         {
            // Convert ANTLR 1-based lines back to 0-based LSP positions.
            var exprStart = new Position(tokStart.Line - 1, tokStart.Column);
            var exprEnd   = new Position(tokStop.Line  - 1,
                                         tokStop.Column + (tokStop.Text?.Length ?? 1));

            var exprText = ExtractRangeText(extractText, exprStart, exprEnd).Trim();
            if (!string.IsNullOrEmpty(exprText))
            {
               var (typeName, varName) = InferTypeAndName(exprText);
               var indent       = GetLineIndent(extractText, exprStart.Line);
               var prefixOnLine = ExtractRangeText(extractText,
                                     new Position(exprStart.Line, 0), exprStart);

               actions.Add(new SumType<Command, CodeAction>(new CodeAction
               {
                  Title = $"Extract '{(exprText.Length > 40 ? exprText[..40] + "…" : exprText).Replace('\n', ' ')}' to variable",
                  Kind  = CodeActionKind.RefactorExtract,
                  Edit  = new WorkspaceEdit
                  {
                     Changes = new Dictionary<string, TextEdit[]>
                     {
                        [uri] =
                        [
                           // Single edit: replace from column-0 of exprStart's line to exprEnd.
                           // This avoids dual-edit position-shifting bugs.
                           new TextEdit
                           {
                              Range   = new LspRange { Start = new Position(exprStart.Line, 0), End = exprEnd },
                              NewText = $"{indent}{typeName} {varName} = {exprText};\n{prefixOnLine}{varName}",
                           },
                        ],
                     },
                  },
               }));
            }
         }
      }

      return actions.ToArray();
   }

   /// <summary>
   /// Attempts to convert a C-style for loop header on the given line to a CgScript native for loop.
   /// Handles: <c>for(type? name = start; name &lt; end; name++) {</c>
   /// Returns the converted line text, or null if the pattern does not match.
   /// </summary>
   private static string? TryConvertCStyleFor(string line)
   {
      // Match: optional-indent for ( optional-type name = start ; name < end ; name++ ) optional-brace
      // Groups: (1)=indent (2)=varName (3)=start (4)=end (5)=trailingBrace
      var m = System.Text.RegularExpressions.Regex.Match(line,
         @"^(\s*)for\s*\(\s*(?:\w+\s+)?(\w+)\s*=\s*([^;]+?)\s*;\s*\2\s*<\s*([^;]+?)\s*;\s*\2\s*\+\+\s*\)(.*)",
         System.Text.RegularExpressions.RegexOptions.None);
      if (!m.Success) return null;

      var indent  = m.Groups[1].Value;
      var varName = m.Groups[2].Value;
      var start   = m.Groups[3].Value.Trim();
      var end     = m.Groups[4].Value.Trim();
      var tail    = m.Groups[5].Value; // e.g. " {" or ""

      return $"{indent}for({varName} for {start}; {end}){tail}";
   }

   /// <summary>Extracts the raw text covered by an LSP range.</summary>
   private static string ExtractRangeText(string text, Position start, Position end)
   {
      int s = LineColToOffset(text, start.Line, start.Character);
      int e = LineColToOffset(text, end.Line, end.Character);
      return e > s ? text[s..Math.Min(e, text.Length)] : string.Empty;
   }

   /// <summary>Returns the leading whitespace of the given 0-based line.</summary>
   private static string GetLineIndent(string text, int line0)
   {
      int off = LineColToOffset(text, line0, 0);
      int end = off;
      while (end < text.Length && text[end] is ' ' or '\t') end++;
      return text[off..end];
   }

   private static int LineColToOffset(string text, int line0, int col)
   {
      int cur = 0;
      for (int i = 0; i < line0; i++)
      {
         int nl = text.IndexOf('\n', cur);
         if (nl < 0) return text.Length;
         cur = nl + 1;
      }
      return Math.Min(cur + col, text.Length);
   }

   /// <summary>
   /// Infers a CgScript type name and a camelCase variable name from an expression.
   /// Recognises a leading built-in function call and uses its declared return type.
   /// Falls back to <c>object</c> / <c>extracted</c>.
   /// </summary>
   private (string TypeName, string VarName) InferTypeAndName(string expression)
   {
      // Pull the leading identifier (e.g. "Tenant_getById" from "Tenant_getById(...)")
      int idEnd = 0;
      while (idEnd < expression.Length && (char.IsLetterOrDigit(expression[idEnd]) || expression[idEnd] == '_'))
         idEnd++;
      var leadId = idEnd > 0 ? expression[..idEnd] : string.Empty;

      if (!string.IsNullOrEmpty(leadId) && _definitions.Functions.TryGetValue(leadId, out var fn))
      {
         var returnType = fn.Variants?.Length > 0 ? fn.Variants[0].ReturnType : null;

         if (!string.IsNullOrWhiteSpace(returnType))
         {
            var varName = char.ToLowerInvariant(returnType[0]) + returnType[1..];
            return (returnType, IsCgsKeyword(varName) ? "extracted" : varName);
         }
      }

      return ("object", "extracted");
   }

   private static bool IsCgsKeyword(string name) =>
      name is "object" or "string" or "number" or "bool" or "array" or "function" or "question";

   /// <summary>
   /// Walks the node chain (innermost-first from <see cref="SelectionRangeHelper.GetNodeChain"/>)
   /// and returns the first parse-tree node that represents a self-contained extractable expression.
   /// <para>
   /// Rules:
   /// <list type="bullet">
   ///   <item>Terminal tokens are skipped.</item>
   ///   <item>Non-expression contexts (statements, declarations, parameter lists, dict body…) are skipped.</item>
   ///   <item>Single-child passthrough wrappers are skipped.</item>
   ///   <item>A <see cref="CgScriptParser.PrimaryExprContext"/> wrapping only a bare IDENTIFIER is skipped
   ///         (it is already a variable — nothing to extract).</item>
   /// </list>
   /// </para>
   /// </summary>
   private static ParserRuleContext? FindExtractableNode(IReadOnlyList<IParseTree> chain)
   {
      foreach (var node in chain)
      {
         // Skip terminals (raw tokens).
         if (node is ITerminalNode) continue;
         if (node is not ParserRuleContext prc) continue;

         switch (prc)
         {
            // ── always-skip: non-expression contexts ──────────────────────────
            case CgScriptParser.ProgramContext:
            case CgScriptParser.BlockContext:
            case CgScriptParser.StatementContext:        // covers all statement subtypes
            case CgScriptParser.DeclarationContext:
            case CgScriptParser.TypeSpecContext:         // covers FunctionTypeContext etc.
            case CgScriptParser.DeclarationInitializerContext:
            case CgScriptParser.AssignmentContext:
            case CgScriptParser.ExprOrAssignContext:     // whole statement, too broad
            case CgScriptParser.ForControlContext:       // covers ForEach/ForClassic subtypes
            case CgScriptParser.CaseExpressionContext:
            case CgScriptParser.DefaultExpressionContext:
            case CgScriptParser.ParametersContext:       // argument list — skip; we want the call node
            case CgScriptParser.FunctionParametersContext:
            case CgScriptParser.DictOrArrayBodyContext:  // body without braces — not a valid expression
            case CgScriptParser.IntervalsContext:
            case CgScriptParser.IntervalContext:
               continue;

            // ── passthrough wrappers: skip if single-child ────────────────────
            case CgScriptParser.ExpressionContext    when prc.ChildCount == 1: continue;
            case CgScriptParser.SubExpressionContext when prc.ChildCount == 1: continue;
            case CgScriptParser.OrExpressionContext  when prc.ChildCount == 1: continue;
            case CgScriptParser.AndExpressionContext when prc.ChildCount == 1: continue;
            case CgScriptParser.RelExpressionContext when prc.ChildCount == 1: continue;
            case CgScriptParser.AddExpressionContext when prc.ChildCount == 1: continue;
            case CgScriptParser.MultExpressionContext when prc.ChildCount == 1: continue;
            case CgScriptParser.PowExpressionContext when prc.ChildCount == 1: continue;
            case CgScriptParser.NegExpressionContext when prc.ChildCount == 1: continue;
            case CgScriptParser.UnaryExprContext     when prc.ChildCount == 1: continue;
            case CgScriptParser.PostfixExprContext   when prc.ChildCount == 1: continue;

            // ── PrimaryExpr: skip bare IDENTIFIER (already a variable) ────────
            case CgScriptParser.PrimaryExprContext px
               when px.ChildCount == 1 && px.GetChild(0) is ITerminalNode:
               continue;

            // ── everything else is extractable ────────────────────────────────
            default:
               return prc;
         }
      }
      return null;
   }
}