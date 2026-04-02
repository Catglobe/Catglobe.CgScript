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
      var definitions = new CgScriptDefinitions();
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

   [Fact]
   public void FunctionParameter_ShadowsGlobal_HoverShowsParameterType()
   {
      // Regression: when a function parameter has the same name as a global
      // variable but a different type, hovering over the global usage (line 1)
      // must show the global's type and hovering inside the function (line 3)
      // must show the parameter's type — not the global's.
      //   Line 0: User u;
      //   Line 1: u;                       ← global — should hover as "User"
      //   Line 2: someFunc(function(Dictionary u) {
      //   Line 3:    u["Name"] = "Remove"; ← param  — should hover as "Dictionary"
      //   Line 4: });
      const string source =
         "User u;\n" +
         "u;\n" +
         "someFunc(function(Dictionary u) {\n" +
         "   u;\n" +
         "});";
      var (target, uri) = CreateTarget(source);

      // Line 1 (0-based) — the bare global usage "u;"
      var globalText = GetHoverText(target, uri, line: 1, character: 0);
      Assert.NotNull(globalText);
      Assert.Contains("User", globalText);

      // Line 3 (0-based) — "u" inside the function body (the Dictionary parameter)
      var paramText = GetHoverText(target, uri, line: 3, character: 3);
      Assert.NotNull(paramText);
      Assert.Contains("Dictionary", paramText);
      Assert.DoesNotContain("User", paramText);
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

   // ── Plain (non-enum) constant hover ──────────────────────────────────────

   [Fact]
   public void PlainConstant_Hover_ShowsFallbackText()
   {
      // TASK_RESOURCE_ID is a plain constant (not enum-derived); hover should show
      // the "constant: TASK_RESOURCE_ID" fallback.
      const string source = "TASK_RESOURCE_ID;";
      var (target, uri) = CreateTarget(source);

      var text = GetHoverText(target, uri, line: 0, character: 0);

      Assert.NotNull(text);
      Assert.Contains("constant: TASK_RESOURCE_ID", text);
   }
}
