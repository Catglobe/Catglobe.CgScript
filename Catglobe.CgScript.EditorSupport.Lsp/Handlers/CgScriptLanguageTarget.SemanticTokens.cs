using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
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
   // ── LSP range helper ──────────────────────────────────────────────────────────

   /// <summary>Converts an ANTLR-based (1-based line) <see cref="SymbolRef"/> to an LSP <see cref="LspRange"/>.</summary>
   private static LspRange ToRange(SymbolRef r) =>
      new LspRange
      {
         Start = new Position(r.Line - 1, r.Column),
         End   = new Position(r.Line - 1, r.Column + r.Length),
      };

   public SemanticTokens OnSemanticTokensFull(SemanticTokensParams p)
   {
      var uri  = p.TextDocument.Uri.ToString();
      var text = _store.GetText(uri) ?? string.Empty;
      var data = SemanticTokensBuilder.Build(
         text,
         _definitions.Functions,
         _definitions.Objects,
         _definitions.Constants,
         _definitions.GlobalVariables).Data;

      var resultId = Interlocked.Increment(ref _semanticResultIdCounter).ToString();
      _semanticCache[uri] = (data, resultId);

      return new SemanticTokens { ResultId = resultId, Data = data };
   }

   // ── semantic tokens delta ─────────────────────────────────────────────────────

   /// <summary>
   /// Returns an incremental delta from the previous result when available,
   /// or a full response if no cached baseline exists.
   /// </summary>
   public SumType<SemanticTokens, SemanticTokensDelta>? OnSemanticTokensFullDelta(
      SemanticTokensDeltaParams p)
   {
      var uri  = p.TextDocument.Uri.ToString();
      var text = _store.GetText(uri) ?? string.Empty;

      var newData  = SemanticTokensBuilder.Build(
         text,
         _definitions.Functions,
         _definitions.Objects,
         _definitions.Constants,
         _definitions.GlobalVariables).Data;
      var resultId = Interlocked.Increment(ref _semanticResultIdCounter).ToString();

      if (_semanticCache.TryGetValue(uri, out var cached)
          && cached.ResultId == p.PreviousResultId)
      {
         _semanticCache[uri] = (newData, resultId);

         var (edit, hasChanges) = ComputeSemanticEdit(cached.Data, newData);
         return new SumType<SemanticTokens, SemanticTokensDelta>(
            new SemanticTokensDelta
            {
               ResultId = resultId,
               Edits    = hasChanges ? [edit] : [],
            });
      }

      // No valid baseline — return a full response.
      _semanticCache[uri] = (newData, resultId);
      return new SumType<SemanticTokens, SemanticTokensDelta>(
         new SemanticTokens { ResultId = resultId, Data = newData });
   }

   /// <summary>
   /// Computes a minimal single-edit delta between two token data arrays by
   /// trimming a common prefix and a common suffix.
   /// </summary>
   private static (SemanticTokensEdit Edit, bool HasChanges) ComputeSemanticEdit(
      int[] oldData, int[] newData)
   {
      int oldLen = oldData.Length;
      int newLen = newData.Length;

      int prefixLen = 0;
      while (prefixLen < oldLen && prefixLen < newLen
             && oldData[prefixLen] == newData[prefixLen])
         prefixLen++;

      int suffixLen = 0;
      while (suffixLen < (oldLen - prefixLen) && suffixLen < (newLen - prefixLen)
             && oldData[oldLen - 1 - suffixLen] == newData[newLen - 1 - suffixLen])
         suffixLen++;

      int deleteCount = oldLen - prefixLen - suffixLen;
      int insertEnd   = newLen - suffixLen;
      bool hasChanges = deleteCount > 0 || (insertEnd - prefixLen) > 0;

      return (
         new SemanticTokensEdit
         {
            Start       = prefixLen,
            DeleteCount = deleteCount,
            Data        = newData[prefixLen..insertEnd],
         },
         hasChanges);
   }

   // ── semantic tokens range ─────────────────────────────────────────────────────

   /// <summary>
   /// Returns semantic tokens restricted to the requested line range.
   /// The delta encoding is reset relative to the first token in the range.
   /// </summary>
   public SemanticTokens OnSemanticTokensRange(SemanticTokensRangeParams p)
   {
      var text = _store.GetText(p.TextDocument.Uri.ToString()) ?? string.Empty;
      return SemanticTokensBuilder.BuildRange(
         text,
         startLine0: p.Range.Start.Line,
         endLine0:   p.Range.End.Line,
         _definitions.Functions,
         _definitions.Objects,
         _definitions.Constants,
         _definitions.GlobalVariables);
   }

   // ── parse-tree → LSP range ────────────────────────────────────────────────────

   /// <summary>
   /// Converts an ANTLR4 parse-tree node to an LSP <see cref="LspRange"/>.
   /// Returns <c>null</c> when the node carries no usable span information
   /// (e.g. an EOF terminal or a rule context with missing start/stop tokens).
   /// </summary>
   private static LspRange? NodeToLspRange(IParseTree node)
   {
      if (node is ParserRuleContext rule)
      {
         var s = rule.Start;
         var e = rule.Stop;
         if (s is null || e is null) return null;

         return new LspRange
         {
            Start = new Position(s.Line - 1, s.Column),
            End   = new Position(e.Line - 1, e.Column + (e.Text?.Length ?? 1)),
         };
      }

      if (node is ITerminalNode terminal)
      {
         var sym = terminal.Symbol;
         if (sym.Type == Antlr4.Runtime.TokenConstants.EOF) return null;

         return new LspRange
         {
            Start = new Position(sym.Line - 1, sym.Column),
            End   = new Position(sym.Line - 1, sym.Column + (sym.Text?.Length ?? 1)),
         };
      }

      return null;
   }
}
