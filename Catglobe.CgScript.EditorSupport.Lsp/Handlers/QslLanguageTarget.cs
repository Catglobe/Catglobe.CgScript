using Catglobe.CgScript.EditorSupport.Parsing;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;
using System.Collections.Concurrent;
using System.Threading;
using LspDiagnostic = Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic;
using LspRange      = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace Catglobe.CgScript.EditorSupport.Lsp.Handlers;

/// <summary>
/// Handles LSP requests/notifications for QSL (.qsl) documents.
/// Supports document synchronisation, diagnostics, semantic tokens,
/// hover, definition, references, rename, highlights, symbols, and completion.
/// </summary>
public class QslLanguageTarget
{
   private readonly ConcurrentDictionary<string, (string Text, ParseResult Result, QslAnalysis Analysis)> _docs = new();
   private readonly ConcurrentDictionary<string, (int[] Data, string ResultId)> _semanticCache = new();
   private int _semanticResultIdCounter;

   /// <summary>Gets or sets the JSON-RPC connection used to push notifications to the client.</summary>
   public JsonRpc? Rpc { get; set; }

   // ── lifecycle ─────────────────────────────────────────────────────────────────

   /// <summary>Returns server capabilities for QSL documents.</summary>
   public InitializeResult Initialize(InitializeParams? _ = null) =>
      new InitializeResult
      {
         Capabilities = new ServerCapabilities
         {
            TextDocumentSync = new TextDocumentSyncOptions
            {
               OpenClose = true,
               Change    = TextDocumentSyncKind.Incremental,
            },
            SemanticTokensOptions = new SemanticTokensOptions
            {
               Full  = new SemanticTokensFullOptions { Delta = true },
               Range = true,
               Legend = new SemanticTokensLegend
               {
                  TokenTypes     = SemanticTokensBuilder.TokenTypes,
                  TokenModifiers = SemanticTokensBuilder.TokenModifiers,
               },
            },
            HoverProvider             = true,
            DefinitionProvider        = new SumType<bool, DefinitionOptions>(true),
            ReferencesProvider        = new SumType<bool, ReferenceOptions>(true),
            RenameProvider            = new SumType<bool, RenameOptions>(new RenameOptions { PrepareProvider = true }),
            DocumentHighlightProvider = new SumType<bool, DocumentHighlightOptions>(true),
            DocumentSymbolProvider    = new SumType<bool, DocumentSymbolOptions>(true),
            CompletionProvider        = new CompletionOptions
            {
               ResolveProvider   = false,
               TriggerCharacters = ["["],
            },
            FoldingRangeProvider      = new SumType<bool, FoldingRangeOptions>(true),
         },
      };

   /// <summary>Called after the client sends the <c>initialized</c> notification.</summary>
   public void Initialized(InitializedParams? _ = null) { }
   /// <summary>Handles the <c>shutdown</c> request.</summary>
   public object Shutdown(object? _ = null) => null!;
   /// <summary>Handles the <c>exit</c> notification.</summary>
   public void Exit(object? _ = null) => Rpc?.Dispose();

   // ── document sync ─────────────────────────────────────────────────────────────

   /// <summary>Handles <c>textDocument/didOpen</c>.</summary>
   public void OnDidOpen(DidOpenTextDocumentParams p)
   {
      var uri = p.TextDocument.Uri.ToString();
      try { UpdateDoc(uri, p.TextDocument.Text); }
      catch (Exception ex) { _ = PublishErrorDiagnosticAsync(p.TextDocument.Uri, ex); return; }
      _semanticCache.TryRemove(uri, out _);
      _ = PublishDiagnosticsAsync(p.TextDocument.Uri);
   }

   /// <summary>Handles <c>textDocument/didChange</c>.</summary>
   public void OnDidChange(DidChangeTextDocumentParams p)
   {
      var uri = p.TextDocument.Uri.ToString();
      string newText;
      try
      {
         newText = (p.ContentChanges ?? []).Aggregate(
            _docs.TryGetValue(uri, out var e) ? e.Text : string.Empty,
            LspHelpers.ApplyChange);
      }
      catch (Exception ex) { _ = PublishErrorDiagnosticAsync(p.TextDocument.Uri, ex); return; }
      UpdateDoc(uri, newText);
      _ = PublishDiagnosticsAsync(p.TextDocument.Uri);
   }

   /// <summary>Handles <c>textDocument/didSave</c>.</summary>
   public void OnDidSave(DidSaveTextDocumentParams? _ = null) { }

   /// <summary>Handles <c>textDocument/didClose</c>.</summary>
   public void OnDidClose(DidCloseTextDocumentParams p)
   {
      var uri = p.TextDocument.Uri.ToString();
      _docs.TryRemove(uri, out _);
      _semanticCache.TryRemove(uri, out _);
      _ = Rpc?.NotifyWithParameterObjectAsync(Methods.TextDocumentPublishDiagnosticsName,
         new PublishDiagnosticParams { Uri = p.TextDocument.Uri, Diagnostics = [] });
   }

   private void UpdateDoc(string uri, string text)
   {
      // Optimistically keep the last analysis while re-parsing.
      if (_docs.TryGetValue(uri, out var existing))
         _docs[uri] = (text, existing.Result, existing.Analysis);

      ParseResult result;
      QslAnalysis analysis;
      try
      {
         (result, analysis) = QslParseService.ParseAndAnalyze(text);
      }
      catch (Exception ex)
      {
         var errDiag = new Parsing.Diagnostic(Parsing.DiagnosticSeverity.Error,
            $"Internal LSP error (QSL parser): {ex.Message}", 1, 0, 0, "QSL000");
         if (_docs.TryGetValue(uri, out var prev))
            _docs[uri] = (text, ParseResult.WithExtra(prev.Result, [errDiag]), prev.Analysis);
         return;
      }
      _docs[uri] = (text, result, analysis);
   }

   // ── diagnostics ───────────────────────────────────────────────────────────────

   private async Task PublishDiagnosticsAsync(Uri uri)
   {
      if (Rpc is null) return;
      var uriStr = uri.ToString();
      if (!_docs.TryGetValue(uriStr, out var entry)) return;

      LspDiagnostic[] diags;
      try
      {
         // Combine parse errors and semantic warnings.
         var all = entry.Result.Diagnostics
            .Concat(entry.Analysis.Diagnostics)
            .Select(d => LspHelpers.DiagnosticToLsp(d, "qsl"))
            .ToArray();
         diags = all;
      }
      catch (Exception ex) { diags = [LspHelpers.MakeInternalErrorDiag("QSL000", $"Internal LSP error: {ex.Message}", "qsl")]; }

      try
      {
         await Rpc.NotifyWithParameterObjectAsync(Methods.TextDocumentPublishDiagnosticsName,
            new PublishDiagnosticParams { Uri = uri, Diagnostics = diags });
      }
      catch { }
   }

   private Task PublishErrorDiagnosticAsync(Uri uri, Exception ex)
   {
      if (Rpc is null) return Task.CompletedTask;
      return Rpc.NotifyWithParameterObjectAsync(Methods.TextDocumentPublishDiagnosticsName,
         new PublishDiagnosticParams
         {
            Uri         = uri,
            Diagnostics = [LspHelpers.MakeInternalErrorDiag("QSL000", $"Internal LSP error: {ex.Message}", "qsl")],
         });
   }

   // ── semantic tokens ───────────────────────────────────────────────────────────

   /// <summary>Returns full semantic tokens for the document.</summary>
   public SemanticTokens OnSemanticTokensFull(SemanticTokensParams p)
   {
      var uri      = p.TextDocument.Uri.ToString();
      var text     = _docs.TryGetValue(uri, out var e) ? e.Text : string.Empty;
      var data     = QslSemanticTokensBuilder.Build(text).Data;
      var resultId = Interlocked.Increment(ref _semanticResultIdCounter).ToString();
      _semanticCache[uri] = (data, resultId);
      return new SemanticTokens { ResultId = resultId, Data = data };
   }

   /// <summary>Returns incremental semantic token deltas.</summary>
   public SumType<SemanticTokens, SemanticTokensDelta>? OnSemanticTokensFullDelta(SemanticTokensDeltaParams p)
   {
      var uri      = p.TextDocument.Uri.ToString();
      var text     = _docs.TryGetValue(uri, out var e) ? e.Text : string.Empty;
      var newData  = QslSemanticTokensBuilder.Build(text).Data;
      var resultId = Interlocked.Increment(ref _semanticResultIdCounter).ToString();

      if (_semanticCache.TryGetValue(uri, out var cached) && cached.ResultId == p.PreviousResultId)
      {
         _semanticCache[uri] = (newData, resultId);
         var (edit, hasChanges) = LspHelpers.ComputeSemanticEdit(cached.Data, newData);
         return new SumType<SemanticTokens, SemanticTokensDelta>(
            new SemanticTokensDelta { ResultId = resultId, Edits = hasChanges ? [edit] : [] });
      }

      _semanticCache[uri] = (newData, resultId);
      return new SumType<SemanticTokens, SemanticTokensDelta>(
         new SemanticTokens { ResultId = resultId, Data = newData });
   }

   /// <summary>Returns semantic tokens for a line range.</summary>
   public SemanticTokens OnSemanticTokensRange(SemanticTokensRangeParams p)
   {
      var uri  = p.TextDocument.Uri.ToString();
      var text = _docs.TryGetValue(uri, out var e) ? e.Text : string.Empty;
      return QslSemanticTokensBuilder.BuildRange(text,
         startLine0: p.Range.Start.Line,
         endLine0:   p.Range.End.Line);
   }

   // ── hover ─────────────────────────────────────────────────────────────────────

   /// <summary>Returns Markdown hover information for the label or property name under the cursor.</summary>
   public Hover OnHover(TextDocumentPositionParams p)
   {
      var uri = p.TextDocument.Uri.ToString();
      if (!_docs.TryGetValue(uri, out var entry)) return null!;

      // ── Check if cursor is on a label reference ──────────────────────────────
      var r = FindRefAtPosition(entry.Analysis, p.Position.Line, p.Position.Character);
      if (r is not null)
      {
         if (!entry.Analysis.Symbols.TryGetValue(r.Label, out var sym))
            return new Hover
            {
               Contents = new MarkupContent
               {
                  Kind  = MarkupKind.Markdown,
                  Value = $"**→** `{r.Label}` (undefined — may be in the full questionnaire)",
               },
            };

         var md = sym.Kind == "question"
            ? $"**QUESTION** `{sym.Name}` {sym.QuestionType}" +
              (string.IsNullOrEmpty(sym.DisplayText) ? "" : $"\n\n---\n\n\"{sym.DisplayText}\"")
            : $"**GROUP** `{sym.Name}`";

         // Augment with ON_PAGE cross-reference info.
         if (entry.Analysis.OnPageMap.TryGetValue(sym.Name, out var pageLabelName))
            md += $"\n\n**Displayed on page:** `{pageLabelName}`";

         return new Hover
         {
            Contents = new MarkupContent { Kind = MarkupKind.Markdown, Value = md },
         };
      }

      // ── Check if cursor is on a property name (Label followed by '=') ───────
      var propName = FindPropertyNameAt(entry.Text, p.Position.Line, p.Position.Character);
      if (propName is not null && QslPropertyMeta.All.TryGetValue(propName, out var propInfo))
      {
         return new Hover
         {
            Contents = new MarkupContent
            {
               Kind  = MarkupKind.Markdown,
               Value = $"**Property** `{propInfo.Name}` _{propInfo.ValueType}_\n\n{propInfo.Doc}",
            },
         };
      }

      return null!;
   }

   // ── go-to-definition ──────────────────────────────────────────────────────────

   /// <summary>Returns the location of the definition of the label under the cursor.</summary>
   public SumType<Location, Location[]>? OnDefinition(TextDocumentPositionParams p)
   {
      var uri = p.TextDocument.Uri.ToString();
      if (!_docs.TryGetValue(uri, out var entry)) return null;

      var r = FindRefAtPosition(entry.Analysis, p.Position.Line, p.Position.Character);
      if (r is null) return null;
      if (!entry.Analysis.Symbols.TryGetValue(r.Label, out var sym)) return null;

      return new SumType<Location, Location[]>(new Location
      {
         Uri   = p.TextDocument.Uri,
         Range = SymToRange(sym),
      });
   }

   // ── references ────────────────────────────────────────────────────────────────

   /// <summary>Returns all occurrences of the label under the cursor.</summary>
   public Location[] OnReferences(ReferenceParams p)
   {
      var uri = p.TextDocument.Uri.ToString();
      if (!_docs.TryGetValue(uri, out var entry)) return [];

      var r = FindRefAtPosition(entry.Analysis, p.Position.Line, p.Position.Character);
      if (r is null) return [];

      bool inclDecl = p.Context?.IncludeDeclaration ?? true;
      return entry.Analysis.LabelRefs
         .Where(x => string.Equals(x.Label, r.Label, StringComparison.OrdinalIgnoreCase)
                     && (inclDecl || !x.IsDefinition))
         .Select(x => new Location { Uri = p.TextDocument.Uri, Range = RefToRange(x) })
         .ToArray();
   }

   // ── prepare rename ────────────────────────────────────────────────────────────

   /// <summary>Returns the range of the label under the cursor, or null if not renameable.</summary>
   public LspRange? OnPrepareRename(TextDocumentPositionParams p)
   {
      var uri = p.TextDocument.Uri.ToString();
      if (!_docs.TryGetValue(uri, out var entry)) return null;

      var r = FindRefAtPosition(entry.Analysis, p.Position.Line, p.Position.Character);
      return r is null ? null : RefToRange(r);
   }

   // ── rename ────────────────────────────────────────────────────────────────────

   /// <summary>Produces a <see cref="WorkspaceEdit"/> renaming all occurrences of the label under the cursor.</summary>
   public WorkspaceEdit OnRename(RenameParams p)
   {
      var uri = p.TextDocument.Uri.ToString();
      if (!_docs.TryGetValue(uri, out var entry)) return new WorkspaceEdit();

      var r = FindRefAtPosition(entry.Analysis, p.Position.Line, p.Position.Character);
      if (r is null) return new WorkspaceEdit();

      var edits = entry.Analysis.LabelRefs
         .Where(x => string.Equals(x.Label, r.Label, StringComparison.OrdinalIgnoreCase))
         .Select(x => new TextEdit { Range = RefToRange(x), NewText = p.NewName })
         .ToArray();

      if (edits.Length == 0) return new WorkspaceEdit();
      return new WorkspaceEdit { Changes = new Dictionary<string, TextEdit[]> { [uri] = edits } };
   }

   // ── document highlight ────────────────────────────────────────────────────────

   /// <summary>Highlights all occurrences of the label under the cursor.</summary>
   public DocumentHighlight[] OnDocumentHighlight(DocumentHighlightParams p)
   {
      var uri = p.TextDocument.Uri.ToString();
      if (!_docs.TryGetValue(uri, out var entry)) return [];

      var r = FindRefAtPosition(entry.Analysis, p.Position.Line, p.Position.Character);
      if (r is null) return [];

      return entry.Analysis.LabelRefs
         .Where(x => string.Equals(x.Label, r.Label, StringComparison.OrdinalIgnoreCase))
         .Select(x => new DocumentHighlight
         {
            Range = RefToRange(x),
            Kind  = x.IsDefinition ? DocumentHighlightKind.Write : DocumentHighlightKind.Read,
         })
         .ToArray();
   }

   // ── document symbols ──────────────────────────────────────────────────────────

   /// <summary>Returns all question and group symbols defined in the document.</summary>
   public SymbolInformation[] OnDocumentSymbol(DocumentSymbolParams p)
   {
      var uri = p.TextDocument.Uri.ToString();
      if (!_docs.TryGetValue(uri, out var entry)) return [];

      return entry.Analysis.Symbols.Values
         .Select(s => new SymbolInformation
         {
            Name = s.Kind == "question"
               ? $"{s.Name} ({s.QuestionType})"
               : s.Name,
            Kind     = s.Kind == "question" ? SymbolKind.Field : SymbolKind.Module,
            Location = new Location { Uri = p.TextDocument.Uri, Range = SymToRange(s) },
         })
         .ToArray();
   }

   // ── completion ────────────────────────────────────────────────────────────────

   /// <summary>
   /// Provides completions for label names (after GOTO/AFTER/BEFORE/INC_AO_FROM/EXC_AO_FROM)
   /// and property names (inside <c>[…]</c> blocks).
   /// </summary>
   public SumType<CompletionItem[], CompletionList>? OnCompletion(CompletionParams p)
   {
      var uri = p.TextDocument.Uri.ToString();
      if (!_docs.TryGetValue(uri, out var entry)) return null;

      var text   = entry.Text;
      var offset = GetOffset(text, p.Position.Line, p.Position.Character);
      if (offset == 0) return null;

      // Scan backwards for context keyword.
      var context = GetCompletionContext(text, offset);

      CompletionItem[] items = context switch
      {
         CompletionContext.LabelContext =>
            entry.Analysis.Symbols.Values
               .Where(s => s.Kind == "question")
               .Select(s => new CompletionItem
               {
                  Label         = s.Name,
                  Kind          = CompletionItemKind.Field,
                  Detail        = s.QuestionType,
                  Documentation = string.IsNullOrEmpty(s.DisplayText) ? default(SumType<string, MarkupContent>?)
                     : new SumType<string, MarkupContent>(s.DisplayText),
               })
               .ToArray(),
         CompletionContext.QnaireProps =>
            BuildPropCompletions(QslPropertySets.QnaireProps),
         CompletionContext.QuestionProps =>
            BuildPropCompletions(QslPropertySets.QuestionProps),
         CompletionContext.SqProps =>
            BuildPropCompletions(QslPropertySets.SqProps),
         CompletionContext.AoProps =>
            BuildPropCompletions(QslPropertySets.AoProps),
         _ => [],
      };

      return new SumType<CompletionItem[], CompletionList>(
         new CompletionList { IsIncomplete = false, Items = items });
   }

   // ── position helpers ──────────────────────────────────────────────────────────

   private static QslTokenRef? FindRefAtPosition(QslAnalysis analysis, int lspLine, int lspChar)
   {
      int antlrLine = lspLine + 1;
      return analysis.LabelRefs.FirstOrDefault(r =>
         r.Line == antlrLine && r.Column <= lspChar && lspChar < r.Column + r.Length);
   }

   private static LspRange RefToRange(QslTokenRef r) => new()
   {
      Start = new Position(r.Line - 1, r.Column),
      End   = new Position(r.Line - 1, r.Column + r.Length),
   };

   private static LspRange SymToRange(QslSymbol s) => new()
   {
      Start = new Position(s.Line - 1, s.Column),
      End   = new Position(s.Line - 1, s.Column + s.Length),
   };

   private static int GetOffset(string text, int line, int character)
   {
      int currentLine = 0, i = 0;
      while (i < text.Length && currentLine < line)
         if (text[i++] == '\n') currentLine++;
      return Math.Min(i + character, text.Length);
   }

   // ── completion context detection ──────────────────────────────────────────────

   private enum CompletionContext
   {
      None,
      LabelContext,
      QnaireProps,
      QuestionProps,
      SqProps,
      AoProps,
   }

   private static CompletionContext GetCompletionContext(string text, int offset)
   {
      // ── Item 3: Suppress completions inside string literals ──────────────────
      // Count unescaped '"' characters on the current line before the cursor.
      // An odd count means the cursor is inside an open string literal.
      if (IsInsideStringLiteral(text, offset))
         return CompletionContext.None;

      // Skip backward over current word and whitespace to find preceding keyword.
      int pos = offset - 1;
      while (pos >= 0 && IsLabelChar(text[pos])) pos--;
      while (pos >= 0 && (text[pos] == ' ' || text[pos] == '\t')) pos--;

      if (pos < 0) return CompletionContext.None;

      // Check for label-triggering keywords.
      if (MatchKeywordAt(text, pos, "GOTO"))       return CompletionContext.LabelContext;
      if (MatchKeywordAt(text, pos, "AFTER"))      return CompletionContext.LabelContext;
      if (MatchKeywordAt(text, pos, "BEFORE"))     return CompletionContext.LabelContext;
      if (MatchKeywordAt(text, pos, "INC_AO_FROM")) return CompletionContext.LabelContext;
      if (MatchKeywordAt(text, pos, "EXC_AO_FROM")) return CompletionContext.LabelContext;

      // Check if we are inside a property block: scan back for '['.
      if (TryGetPropertyBracketContext(text, offset, out var ctx))
         return ctx;

      return CompletionContext.None;
   }

   /// <summary>
   /// Returns <c>true</c> when <paramref name="offset"/> is inside an open string literal
   /// on its current line, using a simple unescaped-quote-count heuristic.
   /// </summary>
   private static bool IsInsideStringLiteral(string text, int offset)
   {
      // Find the start of the current line.
      int lineStart = offset > 0 ? offset - 1 : 0;
      while (lineStart > 0 && text[lineStart - 1] != '\n') lineStart--;

      // Count unescaped '"' characters from lineStart up to (but not including) offset.
      int quoteCount = 0;
      for (int i = lineStart; i < offset && i < text.Length; i++)
      {
         if (text[i] == '\\') { i++; continue; } // skip escaped character
         if (text[i] == '"')  quoteCount++;
      }

      // Odd count ⇒ we are inside an open string literal.
      return (quoteCount & 1) == 1;
   }

   private static bool MatchKeywordAt(string text, int endInclusive, string keyword)
   {
      int start = endInclusive - keyword.Length + 1;
      if (start < 0) return false;
      if (endInclusive + 1 < text.Length && IsLabelChar(text[endInclusive + 1])) return false;
      for (int i = 0; i < keyword.Length; i++)
         if (text[start + i] != keyword[i]) return false;
      if (start > 0 && IsLabelChar(text[start - 1])) return false;
      return true;
   }

   private static bool TryGetPropertyBracketContext(
      string text, int offset, out CompletionContext ctx)
   {
      ctx = CompletionContext.None;
      // Scan backward for '[' that is not yet closed.
      int depth = 0;
      for (int i = offset - 1; i >= 0; i--)
      {
         if (text[i] == ']') { depth++; continue; }
         if (text[i] == '[')
         {
            if (depth > 0) { depth--; continue; }
            // Found an unclosed '['. Now scan further back for context.
            ctx = ClassifyPropertyBlock(text, i);
            return ctx != CompletionContext.None;
         }
      }
      return false;
   }

   /// <summary>
   /// Given the index of a '[' that opens a property block,
   /// determine whether it belongs to questionnaire, question, subquestion, or answer option.
   /// </summary>
   private static CompletionContext ClassifyPropertyBlock(string text, int bracketPos)
   {
      // Scan backward over whitespace/identifiers to find what precedes the bracket.
      int pos = bracketPos - 1;
      while (pos >= 0 && (text[pos] == ' ' || text[pos] == '\t' || text[pos] == '\r' || text[pos] == '\n')) pos--;
      if (pos < 0) return CompletionContext.None;

      // If the token before '[' is a string literal closing quote, it's *inside* a question definition —
      // but properties are before the string literal. So properties appear after a Label or Int.

      // Walk back over a Label token.
      if (IsLabelChar(text[pos]))
      {
         int end = pos;
         while (pos > 0 && IsLabelChar(text[pos - 1])) pos--;
         string tokenBefore = text.Substring(pos, end - pos + 1);

         // Check if this is preceded by QUESTIONNAIRE keyword → qnaire props.
         int prev = pos - 1;
         while (prev >= 0 && (text[prev] == ' ' || text[prev] == '\t')) prev--;
         if (prev >= 0 && IsLabelChar(text[prev]))
         {
            int kEnd = prev;
            while (prev > 0 && IsLabelChar(text[prev - 1])) prev--;
            string prevToken = text.Substring(prev, kEnd - prev + 1);
            if (prevToken == "QUESTIONNAIRE") return CompletionContext.QnaireProps;
         }

         // It's a label — could be question label (QUESTION Label [) or SQ group.
         // To distinguish question from SQ, look for colon before bracket.
         return CompletionContext.QuestionProps;
      }

      // If the token before '[' ends with ':', it's an answer option or SQ.
      if (text[pos] == ':')
      {
         pos--;
         while (pos >= 0 && (text[pos] == ' ' || text[pos] == '\t')) pos--;
         if (pos >= 0 && char.IsDigit(text[pos]))
            return CompletionContext.AoProps;
         // SQ keyword
         if (pos >= 1 && text[pos] == 'Q' && text[pos - 1] == 'S')
            return CompletionContext.SqProps;
      }

      return CompletionContext.None;
   }

   private static bool IsLabelChar(char c) =>
      char.IsLetterOrDigit(c) || c == '_';

   private static CompletionItem[] BuildPropCompletions(IEnumerable<string> props) =>
      props.Select(n => new CompletionItem
      {
         Label = n,
         Kind  = CompletionItemKind.Property,
      }).ToArray();

   // ── folding ranges ────────────────────────────────────────────────────────────

   /// <summary>Returns all foldable regions for the document.</summary>
   public FoldingRange[] OnFoldingRange(FoldingRangeParams p)
   {
      var uri = p.TextDocument.Uri.ToString();
      if (!_docs.TryGetValue(uri, out var entry)) return [];

      return entry.Analysis.FoldingRanges
         .Select(r => new FoldingRange
         {
            StartLine = r.StartLine,
            EndLine   = r.EndLine,
            Kind      = r.Kind == "comment" ? FoldingRangeKind.Comment : FoldingRangeKind.Region,
         })
         .ToArray();
   }

   // ── property-name location helper ─────────────────────────────────────────────

   /// <summary>
   /// Returns the property name at the given cursor position if the cursor is on a
   /// <c>Label = …</c> property assignment, otherwise <c>null</c>.
   /// </summary>
   private static string? FindPropertyNameAt(string text, int lspLine, int lspChar)
   {
      // Locate the start of the requested line.
      int lineStart   = 0;
      int currentLine = 0;
      while (currentLine < lspLine)
      {
         int nl = text.IndexOf('\n', lineStart);
         if (nl < 0) return null;
         lineStart = nl + 1;
         currentLine++;
      }

      int lineEnd   = text.IndexOf('\n', lineStart);
      if (lineEnd < 0) lineEnd = text.Length;

      int charOff = lineStart + lspChar;
      if (charOff >= lineEnd || charOff < 0) return null;
      if (charOff >= text.Length) return null;

      // Widen to find the full label word under/adjacent to the cursor.
      if (!IsLabelChar(text[charOff]))
      {
         // Cursor may be one position past the end of the label.
         if (charOff > lineStart && IsLabelChar(text[charOff - 1]))
            charOff--;
         else
            return null;
      }

      int start = charOff;
      while (start > lineStart && IsLabelChar(text[start - 1])) start--;
      int end = charOff;
      while (end < lineEnd - 1 && IsLabelChar(text[end + 1])) end++;

      string word = text.Substring(start, end - start + 1);

      // Verify that '=' follows (with optional whitespace) — this is a property assignment.
      int pos = end + 1;
      while (pos < lineEnd && (text[pos] == ' ' || text[pos] == '\t')) pos++;
      if (pos >= lineEnd || text[pos] != '=') return null;

      return word;
   }
}
