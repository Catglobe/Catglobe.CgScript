using Catglobe.CgScript.EditorSupport.Parsing;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

/// <summary>
/// Verifies that <see cref="SemanticAnalyzer"/> correctly reports errors for
/// invalid property access, invalid method calls, and assignments to read-only
/// properties on typed variables.
/// </summary>
public class SemanticAnalyzerTests
{
   // ── Helpers ───────────────────────────────────────────────────────────────

   private static IReadOnlyList<Diagnostic> Analyze(string source)
   {
      var result = CgScriptParseService.Parse(source);
      return SemanticAnalyzer.Analyze(
         result.Tree,
         KnownNamesLoader.FunctionNames,
         KnownNamesLoader.ObjectNames,
         KnownNamesLoader.ConstantNames,
         KnownNamesLoader.ObjectDefinitions,
         KnownNamesLoader.GlobalVariableTypes);
   }

   private static Diagnostic? FindByCode(IReadOnlyList<Diagnostic> diags, string code)
      => diags.FirstOrDefault(d => d.Code == code);

   // ── CGS016: unknown property name ─────────────────────────────────────────

   [Fact]
   public void UnknownProperty_ReportsCGS016()
   {
      var diags = Analyze("Dictionary a;\na.test;");
      var d = FindByCode(diags, "CGS016");
      Assert.NotNull(d);
      Assert.Equal(DiagnosticSeverity.Error, d.Severity);
      Assert.Contains("Dictionary", d.Message);
      Assert.Contains("test", d.Message);
   }

   [Fact]
   public void KnownProperty_NoCGS016()
   {
      var diags = Analyze("Dictionary a;\na.Count;");
      Assert.DoesNotContain(diags, d => d.Code == "CGS016");
   }

   // ── CGS017: unknown method name ───────────────────────────────────────────

   [Fact]
   public void UnknownMethod_ReportsCGS017()
   {
      var diags = Analyze("Dictionary a;\na.Test();");
      var d = FindByCode(diags, "CGS017");
      Assert.NotNull(d);
      Assert.Equal(DiagnosticSeverity.Error, d.Severity);
      Assert.Contains("Dictionary", d.Message);
      Assert.Contains("Test", d.Message);
   }

   [Fact]
   public void KnownMethod_NoCGS017()
   {
      var diags = Analyze("Dictionary a;\na.Add(\"key\", 1);");
      Assert.DoesNotContain(diags, d => d.Code == "CGS017");
   }

   // ── CGS018: read-only property assignment ─────────────────────────────────

   [Fact]
   public void AssignToReadonlyProperty_ReportsCGS018()
   {
      var diags = Analyze("Dictionary a;\na.Count = 1;");
      var d = FindByCode(diags, "CGS018");
      Assert.NotNull(d);
      Assert.Equal(DiagnosticSeverity.Error, d.Severity);
      Assert.Contains("Count", d.Message);
   }

   [Fact]
   public void ReadReadonlyProperty_NoCGS018()
   {
      var diags = Analyze("Dictionary a;\nnumber x = a.Count;");
      Assert.DoesNotContain(diags, d => d.Code == "CGS018");
   }

   // ── No false positives when type is unknown ───────────────────────────────

   [Fact]
   public void UntypedVariable_NoMemberErrors()
   {
      // 'object' type — type is unknown so no member validation should occur
      var diags = Analyze("object a;\na.anything;");
      Assert.DoesNotContain(diags, d => d.Code is "CGS016" or "CGS017" or "CGS018");
   }

   // ── Line/column accuracy ──────────────────────────────────────────────────

   [Fact]
   public void UnknownProperty_PointsToMemberToken()
   {
      var diags = Analyze("Dictionary a;\na.test;");
      var d = FindByCode(diags, "CGS016");
      Assert.NotNull(d);
      Assert.Equal(2, d.Line);    // second line
      Assert.Equal(2, d.Column);  // after 'a.'
      Assert.Equal(4, d.Length);  // "test"
   }
}
