using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
using Catglobe.CgScript.EditorSupport.Lsp.Handlers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

/// <summary>
/// Verifies that <see cref="CgScriptLanguageTarget.OnHover"/> returns the correct
/// type information for user-defined symbols, including function parameters.
/// </summary>
public class HoverTests
{
   // ── Test infrastructure ───────────────────────────────────────────────────

   private static (CgScriptLanguageTarget Target, string Uri) CreateTarget(string source)
   {
      var uri         = "file:///test.cgs";
      var definitions = new DefinitionLoader();
      var store       = new DocumentStore(definitions);
      store.Update(uri, source);
      var target = new CgScriptLanguageTarget(store, definitions);
      return (target, uri);
   }

   private static string? GetHoverText(CgScriptLanguageTarget target, string uri, int line, int character)
   {
      var p = new TextDocumentPositionParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = new Uri(uri) },
         Position     = new Position(line, character),
      };
      var hover = target.OnHover(p);
      if (hover is null) return null;

      // Contents may be MarkupContent (plain or markdown)
      if (hover.Contents.TryGetThird(out var markup)) return markup?.Value;

      return null;
   }

   // ── Global variable hover ────────────────────────────────────────────────

   [Fact]
   public void GlobalVariable_Hover_ShowsType()
   {
      const string source = "Dictionary d;\nd;";
      var (target, uri) = CreateTarget(source);

      // Hovering over "d" on the second line (line 1, col 0)
      var text = GetHoverText(target, uri, line: 1, character: 0);

      Assert.NotNull(text);
      Assert.Contains("Dictionary", text);
      Assert.Contains("d", text);
   }

   // ── Function parameter hover ─────────────────────────────────────────────

   [Fact]
   public void FunctionParameter_Hover_ShowsCorrectType()
   {
      // Regression: hovering over a typed function parameter previously showed
      // "? ZipFromPanelData" because DocumentSymbolCollector.CollectAll did not
      // include function parameters.  After the fix the type is resolved via the
      // same ResolveVariableType path used by completion.
      const string source =
         "batch.Execute(function(Question Q1, Question Q2, Question ZipCode, Question ZipFromPanelData) {\n" +
         "   ZipFromPanelData;\n" +
         "});";
      var (target, uri) = CreateTarget(source);

      // Hovering over "ZipFromPanelData" on line 1, column 3
      var text = GetHoverText(target, uri, line: 1, character: 3);

      Assert.NotNull(text);
      Assert.DoesNotContain("? ZipFromPanelData", text);
      Assert.Contains("Question", text);
      Assert.Contains("ZipFromPanelData", text);
   }

   [Fact]
   public void FunctionParameter_Hover_NotUnknown()
   {
      // Hovering over a typed function parameter must not show "?" as the type.
      const string source =
         "someFunc(function(Question myParam) {\n" +
         "   myParam;\n" +
         "});";
      var (target, uri) = CreateTarget(source);

      var text = GetHoverText(target, uri, line: 1, character: 3);

      Assert.NotNull(text);
      // Must not fall back to unknown type
      Assert.DoesNotContain("? myParam", text);
      Assert.Contains("Question", text);
   }

   // ── Enum constant hover ──────────────────────────────────────────────────

   [Fact]
   public void EnumConstant_Hover_ShowsEnumDocAndValue()
   {
      // COLOR_RED is an enum-derived constant; hover should include the enum
      // generic doc ("Index used in Color object"), the constant name, and its value.
      const string source = "COLOR_RED;";
      var (target, uri) = CreateTarget(source);

      var text = GetHoverText(target, uri, line: 0, character: 0);

      Assert.NotNull(text);
      Assert.Contains("COLOR_RED", text);
      // Enum generic documentation
      Assert.Contains("Index used in Color object", text);
      // Numeric value shown
      Assert.Contains("1", text);
   }

   [Fact]
   public void EnumConstant_Hover_DoesNotShowPlainConstantFallback()
   {
      // Hover should NOT fall back to the old "constant: COLOR_RED" text for enum values.
      const string source = "COLOR_RED;";
      var (target, uri) = CreateTarget(source);

      var text = GetHoverText(target, uri, line: 0, character: 0);

      Assert.NotNull(text);
      Assert.DoesNotContain("constant: COLOR_RED", text);
   }
}
