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
}
