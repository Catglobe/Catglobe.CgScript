using Catglobe.CgScript.EditorSupport.Parsing;
using System.Linq;

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
      IEnumerable<string>? globalVariables = null,
      IReadOnlyDictionary<string, FunctionInfo>? functionDefinitions = null)
   {
      var result = CgScriptParseService.Parse(source);
      var globalVarTypes = globalVariables is null
         ? null
         : (IReadOnlyDictionary<string, string>)globalVariables
              .ToDictionary(v => v, _ => (string)"", StringComparer.Ordinal);
      return SemanticAnalyzer.Analyze(
         result.Tree,
         functions       ?? [],
         objects         ?? [],
         constants       ?? [],
         globalVariableTypes: globalVarTypes,
         functionDefinitions: functionDefinitions);
   }

   private static IReadOnlyList<Diagnostic> AnalyzeWithObjects(
      string source,
      IReadOnlyDictionary<string, ObjectMemberInfo> objectDefinitions,
      IEnumerable<string>? functions = null)
   {
      var result = CgScriptParseService.Parse(source);
      return SemanticAnalyzer.Analyze(
         result.Tree,
         functions ?? [],
         objectDefinitions.Keys,
         [],
         objectDefinitions: objectDefinitions);
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

   // ── CGS020: declaration initializer type mismatch ─────────────────────────

   [Fact]
   public void NumberVar_AssignedStringLiteral_ReportsCGS020()
   {
      var diags = Analyze("number a = \"asdf\";");

      Assert.Contains(diags, d => d.Code == "CGS020"
                                  && d.Message.Contains("String")
                                  && d.Message.Contains("number"));
   }

   [Fact]
   public void StringVar_AssignedNumberExpression_ReportsCGS020()
   {
      var diags = Analyze("string s = 1 + 1;");

      Assert.Contains(diags, d => d.Code == "CGS020"
                                  && d.Message.Contains("Number")
                                  && d.Message.Contains("string"));
   }

   [Fact]
   public void NumberVar_AssignedNumberLiteral_NoCGS020()
   {
      var diags = Analyze("number a = 42;");

      Assert.DoesNotContain(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void StringVar_AssignedStringLiteral_NoCGS020()
   {
      var diags = Analyze("string s = \"hello\";");

      Assert.DoesNotContain(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void ObjectVar_AssignedAnything_NoCGS020()
   {
      // object and ? types accept any value
      var diags = Analyze("object o = 123;");

      Assert.DoesNotContain(diags, d => d.Code == "CGS020");
   }

   // ── CGS020: location accuracy ─────────────────────────────────────────────

   [Fact]
   public void NumberVar_AssignedStringLiteral_CGS020_PointsToExpression()
   {
      // "number a = \"asdf\";"  →  expression "asdf" starts at column 11
      var diags = Analyze("number a = \"asdf\";");
      var d = diags.FirstOrDefault(x => x.Code == "CGS020");
      Assert.NotNull(d);
      Assert.Equal(1, d.Line);     // first (and only) line
      Assert.Equal(11, d.Column);  // column of the opening quote
      Assert.True(d.Length > 0, "Length must be positive so that a squiggle is rendered");
   }

   [Fact]
   public void StringVar_AssignedNumberExpression_CGS020_PointsToExpression()
   {
      // "string s = 1 + 1;"  →  expression "1 + 1" starts at column 11
      var diags = Analyze("string s = 1 + 1;");
      var d = diags.FirstOrDefault(x => x.Code == "CGS020");
      Assert.NotNull(d);
      Assert.Equal(1, d.Line);     // first line
      Assert.Equal(11, d.Column);  // column of the first '1'
      Assert.True(d.Length >= 5, "Length should cover the full '1 + 1' expression");
   }

   // ── CGS021: ternary branch type mismatch ──────────────────────────────────

   [Fact]
   public void TernaryWithMismatchedBranchTypes_ReportsCGS021()
   {
      var diags = Analyze(
         "Dictionary d = true ? new Dictionary() : 1;",
         objects: ["Dictionary"]);

      Assert.Contains(diags, d => d.Code == "CGS021");
   }

   [Fact]
   public void TernaryWithMatchingBranchTypes_NoCGS021()
   {
      var diags = Analyze("number x = true ? 1 : 2;");

      Assert.DoesNotContain(diags, d => d.Code == "CGS021");
   }

   // ── CGS021: location accuracy ─────────────────────────────────────────────

   [Fact]
   public void TernaryWithMismatchedBranchTypes_CGS021_PointsToBranches()
   {
      // "number x = true ? 1 : \"asdf\";"  →  then-branch "1" starts at col 18
      var diags = Analyze("number x = true ? 1 : \"asdf\";");
      var d = diags.FirstOrDefault(x => x.Code == "CGS021");
      Assert.NotNull(d);
      Assert.Equal(1, d.Line);     // first line
      Assert.Equal(18, d.Column);  // column of the then-branch '1'
      Assert.True(d.Length > 0, "Length must be positive so that a squiggle is rendered");
   }

   // ── CGS022: function call argument mismatch ───────────────────────────────

   [Fact]
   public void FunctionCalledWithTooFewArgs_ReportsCGS022()
   {
      var funcDefs = new Dictionary<string, FunctionInfo>
      {
         ["DateTime_addDays"] = new FunctionInfo(
            returnType:                  "Array",
            numberOfRequiredArguments:   2,
            parameters: [
               new FunctionParamInfo("Array",  "DATETIME"),
               new FunctionParamInfo("Number", "NONE"),
            ]),
      };

      var diags = Analyze(
         "DateTime_addDays(new DateTime());",
         functions:           ["DateTime_addDays"],
         objects:             ["DateTime"],
         functionDefinitions: funcDefs);

      Assert.Contains(diags, d => d.Code == "CGS022"
                                  && d.Message.Contains("DateTime_addDays")
                                  && d.Message.Contains("(DateTime)"));
   }

   [Fact]
   public void FunctionCalledWithCorrectArgs_NoCGS022()
   {
      var getDateTimeFuncInfo = new FunctionInfo(
         returnType:                "Array",
         numberOfRequiredArguments: 0,
         parameters:                []);

      var funcDefs = new Dictionary<string, FunctionInfo>
      {
         ["DateTime_addDays"] = new FunctionInfo(
            returnType:                  "Array",
            numberOfRequiredArguments:   2,
            parameters: [
               new FunctionParamInfo("Array",  "DATETIME"),
               new FunctionParamInfo("Number", "NONE"),
            ]),
         ["getDateTime"] = getDateTimeFuncInfo,
      };

      var diags = Analyze(
         "DateTime_addDays(getDateTime(), 1);",
         functions:           ["DateTime_addDays", "getDateTime"],
         functionDefinitions: funcDefs);

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void FunctionCalledWithWrongArgType_ReportsCGS022()
   {
      var funcDefs = new Dictionary<string, FunctionInfo>
      {
         ["myFunc"] = new FunctionInfo(
            returnType:                  "Number",
            numberOfRequiredArguments:   1,
            parameters: [new FunctionParamInfo("Number", "NONE")]),
      };

      var diags = Analyze(
         "myFunc(\"hello\");",
         functions:           ["myFunc"],
         functionDefinitions: funcDefs);

      Assert.Contains(diags, d => d.Code == "CGS022"
                                  && d.Message.Contains("myFunc")
                                  && d.Message.Contains("String"));
   }

   [Fact]
   public void KnownFunctionsFromLoader_ValidCall_NoCGS022()
   {
      // DateTime_addDays(new DateTime(), 1) is a valid call
      var diags = Analyze(
         "DateTime_addDays(new DateTime(), 1);",
         functions:           KnownNamesLoader.FunctionNames,
         objects:             KnownNamesLoader.ObjectNames,
         functionDefinitions: KnownNamesLoader.FunctionDefinitions);

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void KnownFunctionsFromLoader_TooFewArgs_ReportsCGS022()
   {
      // DateTime_addDays requires 2 args; calling with 1 should be flagged
      var diags = Analyze(
         "DateTime_addDays(new DateTime());",
         functions:           KnownNamesLoader.FunctionNames,
         objects:             KnownNamesLoader.ObjectNames,
         functionDefinitions: KnownNamesLoader.FunctionDefinitions);

      Assert.Contains(diags, d => d.Code == "CGS022"
                                  && d.Message.Contains("DateTime_addDays"));
   }

   // ── CGS023: constructor argument mismatch ─────────────────────────────────

   private static ObjectMemberInfo MakeStringInfo()
      => new ObjectMemberInfo(
         properties:           new Dictionary<string, bool>(),
         methodNames:          [],
         constructorOverloads: [["string"]]);

   [Fact]
   public void Constructor_TooManyArgs_ReportsCGS023()
   {
      // new String(1, 2) — only one constructor taking a single string arg
      var diags = AnalyzeWithObjects(
         "string s = new String(1, 2);",
         new Dictionary<string, ObjectMemberInfo> { ["String"] = MakeStringInfo() });

      Assert.Contains(diags, d => d.Code == "CGS023" && d.Message.Contains("String"));
   }

   [Fact]
   public void Constructor_WrongArgType_ReportsCGS023()
   {
      // new String(1) — constructor expects string, got number
      var diags = AnalyzeWithObjects(
         "string s = new String(1);",
         new Dictionary<string, ObjectMemberInfo> { ["String"] = MakeStringInfo() });

      Assert.Contains(diags, d => d.Code == "CGS023" && d.Message.Contains("String"));
   }

   [Fact]
   public void Constructor_NoArgs_NoCGS023()
   {
      // new String() — zero args is valid (all params treated as optional)
      var diags = AnalyzeWithObjects(
         "string s = new String();",
         new Dictionary<string, ObjectMemberInfo> { ["String"] = MakeStringInfo() });

      Assert.DoesNotContain(diags, d => d.Code == "CGS023");
   }

   [Fact]
   public void Constructor_CorrectArgType_NoCGS023()
   {
      // new String("test") — valid
      var diags = AnalyzeWithObjects(
         "string s = new String(\"test\");",
         new Dictionary<string, ObjectMemberInfo> { ["String"] = MakeStringInfo() });

      Assert.DoesNotContain(diags, d => d.Code == "CGS023");
   }

   [Fact]
   public void KnownObjectsFromLoader_ValidConstructor_NoCGS023()
   {
      // new String("hello") — valid according to embedded JSON definitions
      var result = CgScriptParseService.Parse("string s = new String(\"hello\");");
      var diags = SemanticAnalyzer.Analyze(
         result.Tree,
         KnownNamesLoader.FunctionNames,
         KnownNamesLoader.ObjectNames,
         KnownNamesLoader.ConstantNames,
         objectDefinitions: KnownNamesLoader.ObjectDefinitions);

      Assert.DoesNotContain(diags, d => d.Code == "CGS023");
   }

   [Fact]
   public void KnownObjectsFromLoader_InvalidConstructorArgs_ReportsCGS023()
   {
      // new String(1, 2) — String has only one constructor that takes a string arg
      var result = CgScriptParseService.Parse("string s = new String(1, 2);");
      var diags = SemanticAnalyzer.Analyze(
         result.Tree,
         KnownNamesLoader.FunctionNames,
         KnownNamesLoader.ObjectNames,
         KnownNamesLoader.ConstantNames,
         objectDefinitions: KnownNamesLoader.ObjectDefinitions);

      Assert.Contains(diags, d => d.Code == "CGS023" && d.Message.Contains("String"));
   }

   // ── CGS024: method call argument mismatch ─────────────────────────────────

   private static ObjectMemberInfo MakeStringWithCompareInfo()
      => new ObjectMemberInfo(
         properties:    new Dictionary<string, bool>(),
         methodNames:   ["CompareTo"],
         methodOverloads: new Dictionary<string, IReadOnlyList<IReadOnlyList<string>>>
         {
            ["CompareTo"] = [["string"]],
         });

   [Fact]
   public void MethodCall_WrongArgType_ReportsCGS024()
   {
      // s.CompareTo(1) — method expects string, got number
      var diags = AnalyzeWithObjects(
         "String s = new String(); s.CompareTo(1);",
         new Dictionary<string, ObjectMemberInfo> { ["String"] = MakeStringWithCompareInfo() });

      Assert.Contains(diags, d => d.Code == "CGS024"
                                  && d.Message.Contains("String.CompareTo"));
   }

   [Fact]
   public void MethodCall_CorrectArgType_NoCGS024()
   {
      // s.CompareTo("other") — valid
      var diags = AnalyzeWithObjects(
         "String s = new String(); s.CompareTo(\"other\");",
         new Dictionary<string, ObjectMemberInfo> { ["String"] = MakeStringWithCompareInfo() });

      Assert.DoesNotContain(diags, d => d.Code == "CGS024");
   }

   [Fact]
   public void MethodCall_TooManyArgs_ReportsCGS024()
   {
      // s.CompareTo("a", "b") — method only takes one arg
      var diags = AnalyzeWithObjects(
         "String s = new String(); s.CompareTo(\"a\", \"b\");",
         new Dictionary<string, ObjectMemberInfo> { ["String"] = MakeStringWithCompareInfo() });

      Assert.Contains(diags, d => d.Code == "CGS024"
                                  && d.Message.Contains("String.CompareTo"));
   }

   [Fact]
   public void KnownObjectsFromLoader_ValidMethodCall_NoCGS024()
   {
      // s.CompareTo("other") using embedded definitions
      var result = CgScriptParseService.Parse("String s = new String(\"x\"); s.CompareTo(\"other\");");
      var diags = SemanticAnalyzer.Analyze(
         result.Tree,
         KnownNamesLoader.FunctionNames,
         KnownNamesLoader.ObjectNames,
         KnownNamesLoader.ConstantNames,
         objectDefinitions: KnownNamesLoader.ObjectDefinitions);

      Assert.DoesNotContain(diags, d => d.Code == "CGS024");
   }

   [Fact]
   public void KnownObjectsFromLoader_WrongMethodArgType_ReportsCGS024()
   {
      // s.CompareTo(1) — String.CompareTo expects a string
      var result = CgScriptParseService.Parse("String s = new String(\"x\"); s.CompareTo(1);");
      var diags = SemanticAnalyzer.Analyze(
         result.Tree,
         KnownNamesLoader.FunctionNames,
         KnownNamesLoader.ObjectNames,
         KnownNamesLoader.ConstantNames,
         objectDefinitions: KnownNamesLoader.ObjectDefinitions);

      Assert.Contains(diags, d => d.Code == "CGS024" && d.Message.Contains("String.CompareTo"));
   }
}
