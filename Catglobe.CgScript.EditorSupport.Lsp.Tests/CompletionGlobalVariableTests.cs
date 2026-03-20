using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
using Catglobe.CgScript.EditorSupport.Lsp.Handlers;
using Catglobe.CgScript.EditorSupport.Parsing;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

/// <summary>
/// Verifies that runtime-provided global variables (from <see cref="DefinitionLoader.GlobalVariables"/>)
/// appear in top-level completion results.
/// </summary>
public class CompletionGlobalVariableTests
{
   private sealed class TestDefinitionLoader : DefinitionLoader
   {
      public TestDefinitionLoader(Dictionary<string, string> globalVariables)
         : base(
            functions:       [],
            objects:         [],
            constants:       [],
            globalVariables: globalVariables,
            enums:           [])
      { }
   }

   private static (CgScriptLanguageTarget Target, string Uri) CreateTarget(
      Dictionary<string, string> globalVariables,
      string                     source = "")
   {
      var uri         = "file:///test.cgs";
      var definitions = new TestDefinitionLoader(globalVariables);
      var store       = new DocumentStore(definitions);
      store.Update(uri, source);
      var target = new CgScriptLanguageTarget(store, definitions);
      return (target, uri);
   }

   private static CompletionItem[] GetCompletions(
      CgScriptLanguageTarget target,
      string                 uri,
      string                 prefix = "")
   {
      // Position at column = prefix.Length so GetWordPrefix returns the prefix
      var p = new CompletionParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = new Uri(uri) },
         Position     = new Position(0, prefix.Length),
      };
      var result = target.OnCompletion(p);
      if (result is null) return [];

      if (result.Value.TryGetFirst(out var arr)) return arr ?? [];
      if (result.Value.TryGetSecond(out var list)) return list?.Items ?? [];
      return [];
   }

   // ── Global variable appears in completions ────────────────────────────────

   [Fact]
   public void GlobalVariable_AppearsInCompletions()
   {
      var (target, uri) = CreateTarget(
         new Dictionary<string, string> { { "Catglobe", "GlobalNamespace" } },
         source: "Catglobe");

      var items = GetCompletions(target, uri, prefix: "Catglobe");

      Assert.Contains(items, i => i.Label == "Catglobe" && i.Kind == CompletionItemKind.Variable);
   }

   [Fact]
   public void GlobalVariable_HasTypeAsDetail()
   {
      var (target, uri) = CreateTarget(
         new Dictionary<string, string> { { "MyGlobal", "MyType" } },
         source: "MyGlobal");

      var items = GetCompletions(target, uri, prefix: "MyGlobal");

      var item = items.FirstOrDefault(i => i.Label == "MyGlobal");
      Assert.NotNull(item);
      Assert.Equal("MyType", item.Detail);
   }

   [Fact]
   public void GlobalVariable_AppearsInEmptyPrefixCompletions()
   {
      var (target, uri) = CreateTarget(
         new Dictionary<string, string> { { "Catglobe", "GlobalNamespace" } },
         source: "");

      var items = GetCompletions(target, uri, prefix: "");

      Assert.Contains(items, i => i.Label == "Catglobe");
   }

   [Fact]
   public void GlobalVariable_IsFilteredByPrefix()
   {
      var (target, uri) = CreateTarget(
         new Dictionary<string, string> { { "Catglobe", "GlobalNamespace" }, { "Other", "OtherType" } },
         source: "Cat");

      var items = GetCompletions(target, uri, prefix: "Cat");

      Assert.Contains(items, i => i.Label == "Catglobe");
      Assert.DoesNotContain(items, i => i.Label == "Other");
   }

   // ── GlobalVariableNames from KnownNamesLoader matches DefinitionLoader ────

   [Fact]
   public void KnownNamesLoader_GlobalVariableNames_MatchDefinitionLoader()
   {
      var definitions = new DefinitionLoader();
      var loaderKeys  = new HashSet<string>(definitions.GlobalVariables.Keys, StringComparer.Ordinal);
      var knownNames  = new HashSet<string>(KnownNamesLoader.GlobalVariableNames, StringComparer.Ordinal);

      Assert.Equal(loaderKeys, knownNames);
   }
}
