using Catglobe.CgScript.EditorSupport.Parsing;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

/// <summary>
/// Verifies that every CGSxxx diagnostic reports a meaningful source location
/// (non-zero <see cref="Diagnostic.Line"/>, non-zero <see cref="Diagnostic.Length"/>,
/// and the correct <see cref="Diagnostic.Column"/>) so that editor squiggles are
/// rendered at the right place.
/// </summary>
public class DiagnosticPositionTests
{
   // ── Inner test class ──────────────────────────────────────────────────────

   private sealed class TestCgScriptDefinitions : CgScriptDefinitions
   {
      public TestCgScriptDefinitions(
         Dictionary<string, FunctionDefinition> functions,
         Dictionary<string, ObjectDefinition>   objects,
         IReadOnlyCollection<string>             constants)
         : base(functions, objects, constants,
                globalVariables: new Dictionary<string, GlobalVariableDefinition>(),
                enums: new Dictionary<string, EnumDefinition>())
      { }
   }

   // ── Helpers ───────────────────────────────────────────────────────────────

   /// <summary>Parse and analyse with no external names (minimal setup).</summary>
   private static IReadOnlyList<Diagnostic> Analyze(
      string               source,
      IEnumerable<string>? functions           = null,
      IEnumerable<string>? objects             = null,
      IEnumerable<string>? constants           = null,
      IReadOnlyDictionary<string, FunctionInfo>? functionDefinitions = null)
   {
      var result   = CgScriptParseService.Parse(source);
      var funcDefs = new Dictionary<string, FunctionDefinition>(StringComparer.Ordinal);
      foreach (var fn in functions ?? [])
      {
         if (functionDefinitions?.TryGetValue(fn, out var info) == true)
            funcDefs[fn] = new FunctionDefinition(
               info.Variants.Select(overload =>
                  new FunctionVariant("",
                     overload.Select((t, i) => new FunctionVariantParam($"p{i}", "", t)).ToArray(),
                     "")).ToArray());
         else
            funcDefs[fn] = new FunctionDefinition(null!);
      }
      var objDefs = new Dictionary<string, ObjectDefinition>(StringComparer.Ordinal);
      foreach (var obj in objects ?? [])
         objDefs[obj] = new ObjectDefinition("", [], [], [], []);
      var defs = new TestCgScriptDefinitions(funcDefs, objDefs, (constants ?? []).ToList());
      return SemanticAnalyzer.Analyze(result.Tree, defs);
   }

   /// <summary>Parse and analyse with the full embedded definitions.</summary>
   private static IReadOnlyList<Diagnostic> AnalyzeKnown(string source)
   {
      var result = CgScriptParseService.Parse(source);
      return SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());
   }

   private static Diagnostic Find(IReadOnlyList<Diagnostic> diags, string code)
   {
      var d = diags.FirstOrDefault(x => x.Code == code);
      Assert.NotNull(d);
      return d;
   }

   // ── CGS001: duplicate variable declaration ────────────────────────────────

   [Fact]
   public void CGS001_PointsToRedeclaredIdentifier()
   {
      // "number x = 1;\nnumber x = 2;\nx;" — second 'x' is at line 2, col 7
      var diags = Analyze("number x = 1;\nnumber x = 2;\nx;");
      var d = Find(diags, "CGS001");
      Assert.Equal(2,  d.Line);    // second declaration
      Assert.Equal(7,  d.Column);  // column of second 'x'
      Assert.Equal(1,  d.Length);  // length of 'x'
   }

   // ── CGS002: unknown type in declaration ───────────────────────────────────

   [Fact]
   public void CGS002_PointsToTypeIdentifier()
   {
      // "MyFakeType a;" — type name starts at col 0, length 10
      var diags = Analyze("MyFakeType a;");
      var d = Find(diags, "CGS002");
      Assert.Equal(1,  d.Line);
      Assert.Equal(0,  d.Column);  // start of line
      Assert.Equal(10, d.Length);  // "MyFakeType"
   }

   // ── CGS003: unknown type in new-expression ────────────────────────────────

   [Fact]
   public void CGS003_PointsToTypeIdentifier()
   {
      // "object a = new FakeType();\na;" — "FakeType" starts at col 15
      var diags = Analyze("object a = new FakeType();\na;");
      var d = Find(diags, "CGS003");
      Assert.Equal(1,  d.Line);
      Assert.Equal(15, d.Column);  // after "object a = new "
      Assert.Equal(8,  d.Length);  // "FakeType"
   }

   // ── CGS004: unknown function ──────────────────────────────────────────────

   [Fact]
   public void CGS004_PointsToFunctionIdentifier()
   {
      // "unknownFunc();" — function name at col 0, length 11
      var diags = Analyze("unknownFunc();");
      var d = Find(diags, "CGS004");
      Assert.Equal(1,  d.Line);
      Assert.Equal(0,  d.Column);
      Assert.Equal(11, d.Length);  // "unknownFunc"
   }

   // ── CGS005: undefined variable ────────────────────────────────────────────

   [Fact]
   public void CGS005_PointsToVariableToken()
   {
      // "number x = unknownVar;" — "unknownVar" at col 11, length 10
      var diags = Analyze("number x = unknownVar;");
      var d = Find(diags, "CGS005");
      Assert.Equal(1,  d.Line);
      Assert.Equal(11, d.Column);  // after "number x = "
      Assert.Equal(10, d.Length);  // "unknownVar"
   }

   // ── CGS006: empty statement ───────────────────────────────────────────────

   [Fact]
   public void CGS006_PointsToSemicolon()
   {
      // ";;" — first empty statement at col 0, length 1
      var diags = Analyze(";;");
      var d = Find(diags, "CGS006");
      Assert.Equal(1, d.Line);
      Assert.Equal(0, d.Column);
      Assert.Equal(1, d.Length);   // ";"
   }

   // ── CGS007: unreachable code ──────────────────────────────────────────────

   [Fact]
   public void CGS007_PointsToUnreachableStatement()
   {
      // Block: return on line 2, unreachable 'number x = 2;' on line 3 (col 2)
      var source = "function() {\n  return 1;\n  number x = 2;\n}();";
      var diags = Analyze(source);
      var d = Find(diags, "CGS007");
      Assert.Equal(3,  d.Line);    // third line
      Assert.Equal(2,  d.Column);  // indented by 2 spaces
      Assert.True(d.Length > 0, "Length must be > 0 so squiggle is rendered");
   }

   // ── CGS008: use before define ─────────────────────────────────────────────

   [Fact]
   public void CGS008_PointsToUsageSite()
   {
      // "number x = y;\nnumber y = 1;\nx;y;" — 'y' used at line 1, col 11
      var diags = Analyze("number x = y;\nnumber y = 1;\nx;y;");
      var d = Find(diags, "CGS008");
      Assert.Equal(1,  d.Line);    // line of the usage (before declaration)
      Assert.Equal(11, d.Column);  // column of 'y' usage
      Assert.Equal(1,  d.Length);  // "y"
   }

   // ── CGS009: unused variable ───────────────────────────────────────────────

   [Fact]
   public void CGS009_PointsToDeclarationIdentifier()
   {
      // "number unusedVar = 1;" — 'unusedVar' declared at col 7, length 9
      var diags = Analyze("number unusedVar = 1;");
      var d = Find(diags, "CGS009");
      Assert.Equal(1, d.Line);
      Assert.Equal(7, d.Column);   // after "number "
      Assert.Equal(9, d.Length);   // "unusedVar"
   }

   // ── CGS015: C-style for loop ──────────────────────────────────────────────

   [Fact]
   public void CGS015_PointsToForKeyword()
   {
      // "for (number i = 0; i < 10; i++) {}" — 'for' at col 0, length 3
      var diags = Analyze("for (number i = 0; i < 10; i++) {}");
      var d = Find(diags, "CGS015");
      Assert.Equal(1, d.Line);
      Assert.Equal(0, d.Column);
      Assert.Equal(3, d.Length);   // "for"
   }

   // ── CGS016: unknown property ──────────────────────────────────────────────

   [Fact]
   public void CGS016_PointsToMemberToken()
   {
      // "Dictionary a;\na.fakeProp;" — 'fakeProp' at line 2, col 2, length 8
      var diags = AnalyzeKnown("Dictionary a;\na.fakeProp;");
      var d = Find(diags, "CGS016");
      Assert.Equal(2, d.Line);
      Assert.Equal(2, d.Column);   // after 'a.'
      Assert.Equal(8, d.Length);   // "fakeProp"
   }

   // ── CGS017: unknown method ────────────────────────────────────────────────

   [Fact]
   public void CGS017_PointsToMethodToken()
   {
      // "Dictionary a;\na.FakeMethod();" — 'FakeMethod' at line 2, col 2, length 10
      var diags = AnalyzeKnown("Dictionary a;\na.FakeMethod();");
      var d = Find(diags, "CGS017");
      Assert.Equal(2,  d.Line);
      Assert.Equal(2,  d.Column);  // after 'a.'
      Assert.Equal(10, d.Length);  // "FakeMethod"
   }

   // ── CGS018: read-only property assignment ─────────────────────────────────

   [Fact]
   public void CGS018_PointsToPropertyToken()
   {
      // "Dictionary a;\na.Count = 5;" — 'Count' at line 2, col 2, length 5
      var diags = AnalyzeKnown("Dictionary a;\na.Count = 5;");
      var d = Find(diags, "CGS018");
      Assert.Equal(2, d.Line);
      Assert.Equal(2, d.Column);  // after 'a.'
      Assert.Equal(5, d.Length);  // "Count"
   }

   // ── CGS019: syntax error (parser) ────────────────────────────────────────

   [Fact]
   public void CGS019_Parser_PointsToOffendingToken()
   {
      // "number a = ;" — mismatched ';' at col 11, length 1
      var result = CgScriptParseService.Parse("number a = ;");
      var d = result.Diagnostics.FirstOrDefault(x => x.Code == "CGS019");
      Assert.NotNull(d);
      Assert.Equal(1,  d.Line);
      Assert.Equal(11, d.Column);  // column of the offending ';'
      Assert.True(d.Length > 0, "Length must be > 0 so squiggle is rendered");
   }

   [Fact]
   public void CGS019_Lexer_PointsToOffendingCharacter()
   {
      // "number a = @;" — unrecognised '@' at col 11, length 1
      var result = CgScriptParseService.Parse("number a = @;");
      var d = result.Diagnostics.FirstOrDefault(x => x.Code == "CGS019");
      Assert.NotNull(d);
      Assert.Equal(1, d.Line);
      Assert.Equal(11, d.Column);  // column of '@'
      Assert.True(d.Length > 0, "Length must be > 0 so squiggle is rendered");
   }

   // ── CGS022: function call argument mismatch ───────────────────────────────

   [Fact]
   public void CGS022_PointsToFunctionNameToken()
   {
      // "myFunc(\"hello\");" — 'myFunc' starts at col 0, length 6
      var funcDefs = new Dictionary<string, FunctionInfo>
      {
         ["myFunc"] = new FunctionInfo([["Number"]]),
      };

      var diags = Analyze(
         "myFunc(\"hello\");",
         functions:           ["myFunc"],
         functionDefinitions: funcDefs);

      var d = Find(diags, "CGS022");
      Assert.Equal(1, d.Line);
      Assert.Equal(0, d.Column);  // start of line
      Assert.Equal(6, d.Length);  // "myFunc"
   }

   // ── CGS020: declaration initializer type mismatch ────────────────────────

   [Fact]
   public void CGS020_PointsToInitializerExpression()
   {
      // "number a = \"hello\";" — string literal starts at col 11, length 7 (with quotes)
      var diags = Analyze("number a = \"hello\";");
      var d = Find(diags, "CGS020");
      Assert.Equal(1,  d.Line);
      Assert.Equal(11, d.Column);  // after "number a = "
      Assert.Equal(7,  d.Length);  // "\"hello\"" (7 chars including quotes)
   }

   // ── CGS021: ternary branch type mismatch ─────────────────────────────────

   [Fact]
   public void CGS021_PointsToThenToElseSpan()
   {
      // "true ? 1 : \"s\";" — span from '1' (col 7) through '"s"' (col 13), length 7
      var diags = Analyze("true ? 1 : \"s\";");
      var d = Find(diags, "CGS021");
      Assert.Equal(1, d.Line);
      Assert.Equal(7, d.Column);   // start of then-branch '1'
      Assert.Equal(7, d.Length);   // spans '1 : "s"' (cols 7–13 inclusive)
   }

   // ── CGS023: constructor argument mismatch ─────────────────────────────────

   [Fact]
   public void CGS023_PointsToConstructorTypeToken()
   {
      // "object a = new String(1, 2);\na;" — 'String' starts at col 15, length 6
      var diags = AnalyzeKnown("object a = new String(1, 2);\na;");
      var d = Find(diags, "CGS023");
      Assert.Equal(1,  d.Line);
      Assert.Equal(15, d.Column);  // after "object a = new "
      Assert.Equal(6,  d.Length);  // "String"
   }

   // ── CGS024: method call argument mismatch ─────────────────────────────────

   [Fact]
   public void CGS024_PointsToMethodNameToken()
   {
      // Line 2: "s.CompareTo(1);" — 'CompareTo' starts at col 2, length 9
      var diags = AnalyzeKnown("String s = new String(\"x\");\ns.CompareTo(1);");
      var d = Find(diags, "CGS024");
      Assert.Equal(2, d.Line);
      Assert.Equal(2, d.Column);   // after "s."
      Assert.Equal(9, d.Length);   // "CompareTo"
   }
}
