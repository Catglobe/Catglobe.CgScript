using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
using Catglobe.CgScript.EditorSupport.Lsp.Handlers;
using Catglobe.CgScript.EditorSupport.Parsing;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

public class DefinitionLoaderEnumTests
{
   [Fact]
   public void DefinitionLoader_ConstantsContainHandWrittenConstant()
   {
      var definitions = new DefinitionLoader();

      Assert.Contains("DATETIME_DAY", definitions.Constants);
   }

   [Fact]
   public void KnownNamesLoader_ConstantNamesContainHandWrittenConstant()
   {
      Assert.Contains("DATETIME_DAY", KnownNamesLoader.ConstantNames);
   }

   /// <summary>
   /// COLOR_RED is derived from ColorCGO.Constants (a [Cg("COLOR",...)] enum),
   /// proving that enum → prefixed-constant registration survives the unified JSON path.
   /// </summary>
   [Fact]
   public void DefinitionLoader_ConstantsContainEnumDerivedColorConstant()
   {
      var definitions = new DefinitionLoader();

      Assert.Contains("COLOR_RED", definitions.Constants);
   }

   [Fact]
   public void KnownNamesLoader_ConstantNamesContainEnumDerivedColorConstant()
   {
      Assert.Contains("COLOR_RED", KnownNamesLoader.ConstantNames);
   }

   // ── Completion documentation for enum constants ───────────────────────────

   private static (CgScriptLanguageTarget Target, string Uri) CreateTarget(string source)
   {
      var uri         = "file:///test.cgs";
      var definitions = new DefinitionLoader();
      var store       = new DocumentStore(definitions);
      store.Update(uri, source);
      return (new CgScriptLanguageTarget(store, definitions), uri);
   }

   private static CompletionItem[] GetCompletions(CgScriptLanguageTarget target, string uri, string prefix)
   {
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

   [Fact]
   public void EnumConstant_Completion_HasDocumentation()
   {
      var (target, uri) = CreateTarget("COLOR_RED");

      var items = GetCompletions(target, uri, prefix: "COLOR_RED");

      var item = items.FirstOrDefault(i => i.Label == "COLOR_RED");
      Assert.NotNull(item);
      // Documentation must be set for enum-derived constants
      Assert.NotNull(item.Documentation);
   }

   [Fact]
   public void EnumConstant_Completion_DocumentationContainsEnumDocAndValue()
   {
      var (target, uri) = CreateTarget("COLOR_RED");

      var items = GetCompletions(target, uri, prefix: "COLOR_RED");

      var item = items.FirstOrDefault(i => i.Label == "COLOR_RED");
      Assert.NotNull(item);

      // Extract the documentation text
      string? docText = null;
      if (item.Documentation?.TryGetSecond(out var mc) == true) docText = mc?.Value;
      else if (item.Documentation?.TryGetFirst(out var s) == true) docText = s;

      Assert.NotNull(docText);
      Assert.Contains("Index used in Color object", docText);
      Assert.Contains("COLOR_RED", docText);
      Assert.Contains("1", docText);
   }

   // ── Completion for plain (non-enum) constants ─────────────────────────────

   [Fact]
   public void PlainConstant_Completion_AppearsInResults()
   {
      var (target, uri) = CreateTarget("DATETIME_DAY");

      var items = GetCompletions(target, uri, prefix: "DATETIME_DAY");

      Assert.Contains(items, i => i.Label == "DATETIME_DAY" && i.Kind == CompletionItemKind.Constant);
   }

   [Fact]
   public void PlainConstant_Completion_HasNoDocumentation()
   {
      // SYSTEM_RESOURCE_ID is a plain constant (not enum-derived); it should not have
      // documentation set on the completion item.
      var (target, uri) = CreateTarget("SYSTEM_RESOURCE_ID");

      var items = GetCompletions(target, uri, prefix: "SYSTEM_RESOURCE_ID");

      var item = items.FirstOrDefault(i => i.Label == "SYSTEM_RESOURCE_ID");
      Assert.NotNull(item);
      Assert.Null(item.Documentation);
   }
}
