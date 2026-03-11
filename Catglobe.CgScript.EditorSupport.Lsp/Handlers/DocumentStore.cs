using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
using Catglobe.CgScript.EditorSupport.Parsing;
using System.Collections.Concurrent;

namespace Catglobe.CgScript.EditorSupport.Lsp.Handlers;

/// <summary>
/// Keeps the most-recently-parsed version of each open document.
/// Parsing is followed by semantic analysis so that the stored
/// <see cref="ParseResult"/> contains both syntax and semantic diagnostics.
/// </summary>
public sealed class DocumentStore
{
   private readonly ConcurrentDictionary<string, (string Text, ParseResult Result)> _docs = new();
   private readonly DefinitionLoader _definitions;

   public DocumentStore(DefinitionLoader definitions)
   {
      _definitions = definitions;
   }

   public void Update(string uri, string text)
   {
      var (cleanedText, _) = PreprocessorScanner.Strip(text);
      var result     = CgScriptParseService.Parse(cleanedText);
      var extraDiags = SemanticAnalyzer.Analyze(
         result.Tree,
         _definitions.Functions.Keys,
         _definitions.Objects.Keys,
         _definitions.Constants);
      var merged = ParseResult.WithExtra(result, extraDiags);
      _docs[uri] = (text, merged);
   }

   public void Remove(string uri) => _docs.TryRemove(uri, out _);

   public ParseResult? GetParseResult(string uri)
      => _docs.TryGetValue(uri, out var entry) ? entry.Result : null;

   public string? GetText(string uri)
      => _docs.TryGetValue(uri, out var entry) ? entry.Text : null;
}
