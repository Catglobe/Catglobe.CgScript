using Catglobe.CgScript.EditorSupport.Parsing;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

/// <summary>
/// Verifies that <see cref="SemanticAnalyzer"/> does not report known constants or
/// known global variables as "Undefined variable".
/// </summary>
public class SemanticAnalyzerDiagnosticsTests
{
   private static IReadOnlyList<Diagnostic> Analyze(
      string               source,
      IEnumerable<string>? functions       = null,
      IEnumerable<string>? objects         = null,
      IEnumerable<string>? constants       = null,
      IEnumerable<string>? globalVariables = null)
   {
      var result = CgScriptParseService.Parse(source);
      return SemanticAnalyzer.Analyze(
         result.Tree,
         functions       ?? [],
         objects         ?? [],
         constants       ?? [],
         globalVariables ?? []);
   }

   // ── Known constants are not reported as undefined ─────────────────────────

   [Fact]
   public void KnownConstant_IsNotReportedAsUndefined()
   {
      var diags = Analyze(
         "number b = DATETIME_DAY; print(b);",
         functions: ["print"],
         constants: ["DATETIME_DAY"]);

      Assert.DoesNotContain(diags, d => d.Message.Contains("DATETIME_DAY"));
   }

   [Fact]
   public void KnownConstantsFromLoader_AreNotReportedAsUndefined()
   {
      var diags = Analyze(
         "number b = DATETIME_DAY; print(b);",
         functions:       KnownNamesLoader.FunctionNames,
         objects:         KnownNamesLoader.ObjectNames,
         constants:       KnownNamesLoader.ConstantNames,
         globalVariables: KnownNamesLoader.GlobalVariableNames);

      Assert.DoesNotContain(diags, d => d.Message.Contains("DATETIME_DAY"));
   }

   // ── Known global variables are not reported as undefined ──────────────────

   [Fact]
   public void KnownGlobalVariable_IsNotReportedAsUndefined()
   {
      var diags = Analyze(
         "number x = myGlobal; print(x);",
         functions:       ["print"],
         globalVariables: ["myGlobal"]);

      Assert.DoesNotContain(diags, d => d.Message.Contains("myGlobal"));
   }

   [Fact]
   public void GlobalVariablesFromLoader_AreNotReportedAsUndefined()
   {
      // Catglobe is the runtime global variable exposed in CgScriptGlobalVariables.json
      var diags = Analyze(
         "string s = Catglobe; print(s);",
         functions:       KnownNamesLoader.FunctionNames,
         objects:         KnownNamesLoader.ObjectNames,
         constants:       KnownNamesLoader.ConstantNames,
         globalVariables: KnownNamesLoader.GlobalVariableNames);

      Assert.DoesNotContain(diags, d => d.Message.Contains("Catglobe") && d.Message.Contains("Undefined"));
   }

   // ── Unknown identifiers are still reported ────────────────────────────────

   [Fact]
   public void UnknownIdentifier_IsReportedAsUndefined()
   {
      var diags = Analyze("number x = unknownVar;");

      Assert.Contains(diags, d => d.Message.Contains("unknownVar") && d.Message.Contains("Undefined"));
   }
}
