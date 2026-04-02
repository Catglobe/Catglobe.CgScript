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
      return SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());
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

   [Fact]
   public void ReadonlyProperty_UsedAsIndexKey_NoCGS018()
   {
      // Regression: u.ResourceId is used as the index key, not as the assignment
      // target, so it must not be flagged as a read-only assignment.
      var diags = Analyze("User u;\nDictionary d;\nd[u.ResourceId] = u;");
      Assert.DoesNotContain(diags, d => d.Code == "CGS018");
   }

   [Fact]
   public void ReadonlyProperty_UsedAsArrayIndex_NoCGS018()
   {
      // Same regression for an array index.
      var diags = Analyze("User u;\narray arr;\narr[u.ResourceId] = u;");
      Assert.DoesNotContain(diags, d => d.Code == "CGS018");
   }

   [Fact]
   public void ReadonlyIntermediateProperty_InChainAssignment_NoCGS018()
   {
      // Regression: Tenant.ResourceModel is read-only, but the actual assignment
      // target is ResourceModel.Name (which has a setter).  ResourceModel must not
      // be flagged.
      var diags = Analyze("Tenant t;\nt.ResourceModel.Name = \"x\";");
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

   // ── Multi-level member access (e.g. Catglobe.Json.Parse) ─────────────────

   [Fact]
   public void MultiLevel_KnownMethod_NoCGS017()
   {
      // Catglobe is a global variable of type GlobalNamespace;
      // GlobalNamespace.Json has return type JsonNamespace;
      // JsonNamespace has a method Parse — so no error expected.
      var diags = Analyze("Catglobe.Json.Parse(\"ok\");");
      Assert.DoesNotContain(diags, d => d.Code == "CGS017");
   }

   [Fact]
   public void MultiLevel_UnknownMethod_ReportsCGS017()
   {
      // JsonNamespace does not have a method Read — expect CGS017.
      var diags = Analyze("Catglobe.Json.Read(\"ok\");");
      var d = FindByCode(diags, "CGS017");
      Assert.NotNull(d);
      Assert.Equal(DiagnosticSeverity.Error, d.Severity);
      Assert.Contains("JsonNamespace", d.Message);
      Assert.Contains("Read", d.Message);
   }

   // ── Regression: typed variable with initializer — CGS016 false negative ──

   [Fact]
   public void TypedVarWithInitializer_KnownProperty_NoCGS016()
   {
      // Regression: when declared with a constructor-call initializer the variable
      // type must still be tracked so that member access validation does not fire.
      var diags = Analyze(
         "QuestionnaireBatchJob batch = new QuestionnaireBatchJob(0 /*0 == current*/);\n" +
         "if (!batch.CurrentCompleted)\n" +
         "   batch.CurrentCompleted = true;");
      Assert.DoesNotContain(diags, d => d.Code == "CGS016");
   }

   // ── Typed variable inside a function body ─────────────────────────────────

   [Fact]
   public void TypedVarInFunctionBody_KnownProperty_NoCGS016()
   {
      // A typed variable declared inside a function literal body should not produce
      // a CGS016 false positive when a known property is accessed on it.
      var diags = Analyze(
         "someFunc(function() {\n" +
         "   QuestionnaireBatchJob batch = new QuestionnaireBatchJob(0 /*0 == current*/);\n" +
         "   if (!batch.CurrentCompleted)\n" +
         "      batch.CurrentCompleted = true;\n" +
         "});");
      Assert.DoesNotContain(diags, d => d.Code == "CGS016");
   }

   [Fact]
   public void TypedVarInFunctionBody_UnknownProperty_ReportsCGS016()
   {
      // A typed variable declared inside a function literal body should still
      // report CGS016 when an unknown property is accessed.
      var diags = Analyze(
         "someFunc(function() {\n" +
         "   QuestionnaireBatchJob batch = new QuestionnaireBatchJob(0);\n" +
         "   batch.NonExistentProp;\n" +
         "});");
      Assert.Contains(diags, d => d.Code == "CGS016");
   }

   // ── CGS020: property return type normalisation (regression) ──────────────

   [Fact]
   public void PropertyReturnType_LowercaseString_NoCGS020()
   {
      // Regression: EmailTemplate.MessageDefaultLanguage has ReturnType "string"
      // (lowercase) in the JSON definitions.  Assigning it to a string variable
      // must not produce CGS020.
      var diags = Analyze(
         "EmailTemplate et = new EmailTemplate(\"name\", 0);\n" +
         "string s = et.MessageDefaultLanguage;");
      Assert.DoesNotContain(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void PropertyReturnType_CSharpIntAlias_NoCGS020()
   {
      // Regression: CopyResource.ResourceIdOfCopy has ReturnType "int" (C# type).
      // CgScript has no int type — the canonical type is "number" — so assigning
      // to a number variable must not produce CGS020.
      var diags = Analyze(
         "CopyResource cr = new CopyResource(0, 0, \"name\");\n" +
         "number n = cr.ResourceIdOfCopy;");
      Assert.DoesNotContain(diags, d => d.Code == "CGS020");
   }
}
