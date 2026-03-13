using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
using Catglobe.CgScript.EditorSupport.Lsp.Handlers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

/// <summary>
/// Verifies that object type completion items expose their documentation as
/// <see cref="MarkupContent"/> with <see cref="MarkupKind.Markdown"/> so that
/// LSP clients render the documentation with markdown formatting.
/// </summary>
public class CompletionObjectTests
{
   private sealed class TestDefinitionLoader : DefinitionLoader
   {
      public TestDefinitionLoader(Dictionary<string, ObjectDefinition> objects)
         : base(
            functions:       [],
            objects:         objects,
            constants:       [],
            globalVariables: [])
      { }
   }

   private static CompletionItem[] GetCompletions(
      Dictionary<string, ObjectDefinition> objects,
      string prefix = "")
   {
      var uri         = "file:///test.cgs";
      var definitions = new TestDefinitionLoader(objects);
      var store       = new DocumentStore(definitions);
      store.Update(uri, prefix);
      var target = new CgScriptLanguageTarget(store, definitions);

      var p = new CompletionParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = new Uri(uri) },
         Position     = new Position(0, prefix.Length),
      };
      var result = target.OnCompletion(p);
      if (result is null) return [];
      if (result.Value.TryGetFirst(out var arr))  return arr  ?? [];
      if (result.Value.TryGetSecond(out var list)) return list?.Items ?? [];
      return [];
   }

   [Fact]
   public void ObjectCompletion_Documentation_IsMarkupContent()
   {
      var objects = new Dictionary<string, ObjectDefinition>
      {
         ["MyObject"] = new ObjectDefinition("MyObject", "My **object** doc", null, null, null, null),
      };

      var items = GetCompletions(objects, prefix: "MyObject");

      var item = items.FirstOrDefault(i => i.Label == "MyObject");
      Assert.NotNull(item);
      Assert.True(item.Documentation.HasValue);
      Assert.True(item.Documentation.Value.TryGetSecond(out var markup));
      Assert.NotNull(markup);
      Assert.Equal(MarkupKind.Markdown, markup.Kind);
   }

   [Fact]
   public void ObjectCompletion_Documentation_ContainsDocText()
   {
      var objects = new Dictionary<string, ObjectDefinition>
      {
         ["MyObject"] = new ObjectDefinition("MyObject", "My **object** doc", null, null, null, null),
      };

      var items = GetCompletions(objects, prefix: "MyObject");

      var item = items.FirstOrDefault(i => i.Label == "MyObject");
      Assert.NotNull(item);
      Assert.True(item.Documentation.HasValue);
      Assert.True(item.Documentation.Value.TryGetSecond(out var markup));
      Assert.Equal("My **object** doc", markup?.Value);
   }

   [Fact]
   public void ObjectCompletion_NullDoc_Documentation_IsMarkupContent_WithEmptyValue()
   {
      var objects = new Dictionary<string, ObjectDefinition>
      {
         ["MyObject"] = new ObjectDefinition("MyObject", null, null, null, null, null),
      };

      var items = GetCompletions(objects, prefix: "MyObject");

      var item = items.FirstOrDefault(i => i.Label == "MyObject");
      Assert.NotNull(item);
      Assert.True(item.Documentation.HasValue);
      Assert.True(item.Documentation.Value.TryGetSecond(out var markup));
      Assert.Equal(MarkupKind.Markdown, markup?.Kind);
      Assert.Equal(string.Empty, markup?.Value);
   }
}
