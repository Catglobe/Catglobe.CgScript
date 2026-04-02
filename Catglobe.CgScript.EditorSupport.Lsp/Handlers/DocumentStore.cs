using Catglobe.CgScript.EditorSupport.Parsing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Catglobe.CgScript.EditorSupport.Lsp.Handlers;

/// <summary>
/// Keeps the most-recently-parsed version of each open document.
/// Parsing is followed by semantic analysis so that the stored
/// <see cref="ParseResult"/> contains both syntax and semantic diagnostics.
/// </summary>
public sealed class DocumentStore
{
   private readonly ConcurrentDictionary<string, (string Text, ParseResult Result)> _docs = new();
   private readonly CgScriptDefinitions _definitions;

   public DocumentStore(CgScriptDefinitions definitions) => _definitions = definitions;

   public void Update(string uri, string text)
   {
      // Expose the new text immediately so concurrent readers (hover, semantic tokens, etc.)
      // never see a stale version while the parse is running.
      if (_docs.TryGetValue(uri, out var existing))
         _docs[uri] = (text, existing.Result);

      // Each stage is caught independently so a failure in one step does not discard
      // valid results from earlier steps.  The user always sees a diagnostic instead of
      // a silent LSP disconnection.

      // Stage 1: preprocessor stripping
      string cleanedText;
      var stageDiags = new List<Diagnostic>();
      try
      {
         (cleanedText, _) = PreprocessorScanner.Strip(text);
      }
      catch (Exception ex)
      {
         stageDiags.Add(new Diagnostic(DiagnosticSeverity.Error,
            $"Internal LSP error (preprocessor): {ex.Message}", 1, 0, 0, "CGS000"));
         cleanedText = text;
      }

      // Stage 2: parsing
      ParseResult? result = null;
      try
      {
         result = CgScriptParseService.Parse(cleanedText);
      }
      catch (Exception ex)
      {
         stageDiags.Add(new Diagnostic(DiagnosticSeverity.Error,
            $"Internal LSP error (parser): {ex.Message}", 1, 0, 0, "CGS000"));
         try { result = CgScriptParseService.Parse(string.Empty); } catch { /* last resort */ }
      }

      if (result is null)
      {
         if (stageDiags.Count > 0 && _docs.TryGetValue(uri, out var prev))
            _docs[uri] = (text, ParseResult.WithExtra(prev.Result, stageDiags));
         return;
      }

      // Stage 3: semantic analysis
      IEnumerable<Diagnostic> extraDiags = [];
      try
      {
         extraDiags = SemanticAnalyzer.Analyze(result.Tree, _definitions);
      }
      catch (Exception ex)
      {
         System.Diagnostics.Debug.WriteLine(
            $"[CgScript LSP] Semantic analysis error for '{uri}': {ex}");
      }

      _docs[uri] = (text, ParseResult.WithExtra(result, extraDiags.Concat(stageDiags)));
   }

   public void Remove(string uri) => _docs.TryRemove(uri, out _);

   public ParseResult? GetParseResult(string uri)
      => _docs.TryGetValue(uri, out var entry) ? entry.Result : null;

   public string? GetText(string uri)
      => _docs.TryGetValue(uri, out var entry) ? entry.Text : null;
}
