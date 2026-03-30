using Catglobe.CgScript.EditorSupport.Parsing;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

/// <summary>
/// Verifies that locally declared variables appear in completion results.
/// The completion provider uses <see cref="DocumentSymbolCollector.CollectAll"/>
/// to enumerate local variables from the parse tree, so these tests validate
/// that the underlying collection is correct.
/// </summary>
public class CompletionVariableTests
{
   // ── Variable symbol collection ────────────────────────────────────────────

   [Fact]
   public void DeclaredVariable_IsCollected()
   {
      var result = CgScriptParseService.Parse("Dictionary d;");
      var symbols = DocumentSymbolCollector.CollectAll(result.Tree);

      var variable = symbols.FirstOrDefault(s => s.Name == "d");
      Assert.NotNull(variable);
      Assert.Equal("variable", variable.Kind);
      Assert.Equal("Dictionary", variable.TypeName);
   }

   [Fact]
   public void MultipleDeclaredVariables_AreAllCollected()
   {
      const string src = "Dictionary d;\nnumber n;\nstring s;";
      var result  = CgScriptParseService.Parse(src);
      var symbols = DocumentSymbolCollector.CollectAll(result.Tree);

      Assert.Contains(symbols, s => s.Name == "d"  && s.TypeName == "Dictionary");
      Assert.Contains(symbols, s => s.Name == "n"  && s.TypeName == "number");
      Assert.Contains(symbols, s => s.Name == "s"  && s.TypeName == "string");
   }

   [Fact]
   public void VariableWithPrefix_MatchesCaseInsensitively()
   {
      var result = CgScriptParseService.Parse("Dictionary d;\nDashboard dash;");
      var symbols = DocumentSymbolCollector.CollectAll(result.Tree);

      // Both "d" and "dash" start with "d" — both should be found
      var matching = symbols
         .Where(s => s.Kind == "variable" && s.Name.StartsWith("d", StringComparison.OrdinalIgnoreCase))
         .ToList();

      Assert.Equal(2, matching.Count);
   }

   // ── Function parameter collection ─────────────────────────────────────────

   [Fact]
   public void FunctionParameter_IsCollectedByCollectAll()
   {
      // Regression: function parameters declared as "Question ZipFromPanelData"
      // should appear in CollectAll with the correct type.
      const string src = "someFunc(function(Question Q1, Question ZipFromPanelData) { });";
      var result  = CgScriptParseService.Parse(src);
      var symbols = DocumentSymbolCollector.CollectAll(result.Tree);

      var param = symbols.FirstOrDefault(s => s.Name == "ZipFromPanelData");
      Assert.NotNull(param);
      Assert.Equal("parameter", param.Kind);
      Assert.Equal("Question", param.TypeName);
   }

   [Fact]
   public void FunctionParameter_NotCollectedByCollect()
   {
      // Function parameters should NOT appear in the document outline (Collect).
      const string src = "someFunc(function(Question ZipFromPanelData) { });";
      var result  = CgScriptParseService.Parse(src);
      var symbols = DocumentSymbolCollector.Collect(result.Tree);

      Assert.DoesNotContain(symbols, s => s.Name == "ZipFromPanelData");
   }

   [Fact]
   public void MultipleFunctionParameters_AllCollectedByCollectAll()
   {
      const string src = "batch.Execute(function(Question Q1, Question Q2, Question ZipCode) { });";
      var result  = CgScriptParseService.Parse(src);
      var symbols = DocumentSymbolCollector.CollectAll(result.Tree);

      Assert.Contains(symbols, s => s.Name == "Q1"       && s.TypeName == "Question" && s.Kind == "parameter");
      Assert.Contains(symbols, s => s.Name == "Q2"       && s.TypeName == "Question" && s.Kind == "parameter");
      Assert.Contains(symbols, s => s.Name == "ZipCode"  && s.TypeName == "Question" && s.Kind == "parameter");
   }

   // ── CollectAtPosition: scope isolation ────────────────────────────────────

   [Fact]
   public void CollectAtPosition_OuterScope_ShowsGlobalVariable_NotFunctionParam()
   {
      // Cursor is on line 5 (1-based), outside the function literal.
      // 'a' should be resolved as 'User', not 'Dictionary'.
      const string src =
         "function(Dictionary a) {\n" +  // line 1
         "    a.Count;\n" +               // line 2
         "};\n" +                         // line 3
         "User a;\n" +                    // line 4
         "a";                             // line 5 — cursor here

      var result = CgScriptParseService.Parse(src);
      // Cursor at line 5, column 0
      var symbols = DocumentSymbolCollector.CollectAtPosition(result.Tree, 5, 0);

      var sym = symbols.FirstOrDefault(s => s.Name == "a");
      Assert.NotNull(sym);
      Assert.Equal("User",     sym.TypeName);
      Assert.Equal("variable", sym.Kind);
   }

   [Fact]
   public void CollectAtPosition_InsideFunction_ShowsParameter_NotGlobal()
   {
      // Cursor is on line 2 (inside the function body).
      // 'a' should be resolved as 'Dictionary' (the parameter).
      const string src =
         "function(Dictionary a) {\n" +  // line 1
         "    a.Count;\n" +               // line 2 — cursor here
         "};\n" +                         // line 3
         "User a;";                       // line 4

      var result = CgScriptParseService.Parse(src);
      // Cursor at line 2, column 4
      var symbols = DocumentSymbolCollector.CollectAtPosition(result.Tree, 2, 4);

      var sym = symbols.FirstOrDefault(s => s.Name == "a");
      Assert.NotNull(sym);
      Assert.Equal("Dictionary", sym.TypeName);
      Assert.Equal("parameter",  sym.Kind);
   }

   [Fact]
   public void CollectAtPosition_OuterScope_NoDuplicates()
   {
      // There must be exactly one entry for 'a' at the outer scope.
      const string src =
         "function(Dictionary a) {\n" +
         "    a.Count;\n" +
         "};\n" +
         "User a;\n" +
         "a";

      var result  = CgScriptParseService.Parse(src);
      var symbols = DocumentSymbolCollector.CollectAtPosition(result.Tree, 5, 0);

      Assert.Equal(1, symbols.Count(s => s.Name == "a"));
   }

   [Fact]
   public void CollectAtPosition_InsideFunction_NoDuplicates()
   {
      // There must be exactly one entry for 'a' inside the function body.
      const string src =
         "function(Dictionary a) {\n" +
         "    a.Count;\n" +
         "};\n" +
         "User a;";

      var result  = CgScriptParseService.Parse(src);
      var symbols = DocumentSymbolCollector.CollectAtPosition(result.Tree, 2, 4);

      Assert.Equal(1, symbols.Count(s => s.Name == "a"));
   }
}
