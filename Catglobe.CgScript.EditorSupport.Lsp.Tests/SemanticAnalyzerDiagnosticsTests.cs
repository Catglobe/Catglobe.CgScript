using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
using Catglobe.CgScript.EditorSupport.Lsp.Handlers;
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

   [Fact]
   public void ConvertToNumber_CalledWithString_NoCGS022()
   {
      // convertToNumber("[avatarStore]") is valid; no false-positive CGS022 expected
      var diags = Analyze(
         "number n = convertToNumber(\"[avatarStore]\");",
         functions:           KnownNamesLoader.FunctionNames,
         functionDefinitions: KnownNamesLoader.FunctionDefinitions);

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void Format_CalledWithStringAndArg_NoCGS022()
   {
      // format("{0}foo", location) is valid; no false-positive CGS022 expected
      var diags = Analyze(
         "string location = \"x\"; string s = format(\"{0}foo\", location);",
         functions:           KnownNamesLoader.FunctionNames,
         functionDefinitions: KnownNamesLoader.FunctionDefinitions);

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   // ── CGS022: DocumentStore path (regression for empty-Parameters bug) ──────

   private static IReadOnlyList<Diagnostic> AnalyzeViaDocumentStore(string source)
   {
      var uri   = "file:///test.cgs";
      var store = new DocumentStore(new DefinitionLoader());
      store.Update(uri, source);
      return store.GetParseResult(uri)!.Diagnostics;
   }

   [Fact]
   public void DocumentStore_ConvertToNumber_CalledWithString_NoCGS022()
   {
      // Regression: DocumentStore.BuildFunctionInfos was including functions with
      // empty Parameters arrays, causing false-positive CGS022 for any call with args.
      var diags = AnalyzeViaDocumentStore("number n = convertToNumber(\"[avatarStore]\");");

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void DocumentStore_Format_CalledWithManyArgs_NoCGS022()
   {
      // format("{0}...", a, b, c, d, e, f) — variadic; must not produce CGS022
      var diags = AnalyzeViaDocumentStore(
         "string location = \"x\"; string s = format(\"{0}{1}{2}{3}{4}{5}\", location, location, location, location, location, location);");

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void DocumentStore_IsEmpty_CalledWithAnyArg_NoCGS022()
   {
      // isEmpty(someVar) must not produce CGS022
      var diags = AnalyzeViaDocumentStore("string x = \"hello\"; bool b = isEmpty(x);");

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void DocumentStore_ConvertToString_CalledWithObject_NoCGS022()
   {
      // convertToString(sb) where sb is a StringBuilder — must not produce CGS022
      var diags = AnalyzeViaDocumentStore("StringBuilder sb = new StringBuilder(); string s = convertToString(sb);");

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void CGS022_ErrorMessage_UsesProperEnglish()
   {
      // Error message must say "No overload of 'X' matches (...)", not "Doesn't has X with format (...)"
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

      var d = Assert.Single(diags, x => x.Code == "CGS022");
      Assert.StartsWith("No overload of", d.Message);
      Assert.Contains("myFunc", d.Message);
   }

   // ── CGS022: new-style functions with Variants are validated ──────────────

   [Fact]
   public void NewStyleFunction_CorrectArgs_NoCGS022()
   {
      // AppProduct_getById(int) — correct single Number arg
      var diags = Analyze(
         "AppProduct_getById(1);",
         functions:           KnownNamesLoader.FunctionNames,
         functionDefinitions: KnownNamesLoader.FunctionDefinitions);

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void NewStyleFunction_WrongArgType_ReportsCGS022()
   {
      // AppProduct_getById expects a Number (int); passing a string literal is wrong
      var diags = Analyze(
         "AppProduct_getById(\"not-an-id\");",
         functions:           KnownNamesLoader.FunctionNames,
         functionDefinitions: KnownNamesLoader.FunctionDefinitions);

      Assert.Contains(diags, d => d.Code == "CGS022" && d.Message.Contains("AppProduct_getById"));
   }

   [Fact]
   public void NewStyleFunction_MultipleVariants_FirstVariantValid_NoCGS022()
   {
      // Color_getByRGB has two variants: (string) and (int, int, int)
      // Calling with a single string should match the first variant.
      var diags = Analyze(
         "Color_getByRGB(\"#ff0000\");",
         functions:           KnownNamesLoader.FunctionNames,
         functionDefinitions: KnownNamesLoader.FunctionDefinitions);

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void NewStyleFunction_MultipleVariants_SecondVariantValid_NoCGS022()
   {
      // Color_getByRGB(int, int, int) — second variant
      var diags = Analyze(
         "Color_getByRGB(255, 0, 128);",
         functions:           KnownNamesLoader.FunctionNames,
         functionDefinitions: KnownNamesLoader.FunctionDefinitions);

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void NewStyleFunction_WrongArgCount_ReportsCGS022()
   {
      // Color_getByRGB has variants (string) and (int, int, int); 2 args matches neither
      var diags = Analyze(
         "Color_getByRGB(1, 2);",
         functions:           KnownNamesLoader.FunctionNames,
         functionDefinitions: KnownNamesLoader.FunctionDefinitions);

      Assert.Contains(diags, d => d.Code == "CGS022" && d.Message.Contains("Color_getByRGB"));
   }

   [Fact]
   public void DocumentStore_NewStyleFunction_CorrectArgs_NoCGS022()
   {
      // AppProduct_getById(1) — new-style function via DocumentStore path
      var diags = AnalyzeViaDocumentStore("AppProduct_getById(1);");
      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void DocumentStore_NewStyleFunction_WrongArgType_ReportsCGS022()
   {
      // AppProduct_getById expects int; passing string should be flagged
      var diags = AnalyzeViaDocumentStore("AppProduct_getById(\"bad\");");
      Assert.Contains(diags, d => d.Code == "CGS022" && d.Message.Contains("AppProduct_getById"));
   }

   // ── CGS022: empty keyword as parameter — must never produce false-positive ──

   [Theory]
   [InlineData("Workflow_call(123, empty, 123);")]
   [InlineData("Workflow_call(123, empty);")]
   [InlineData("Workflow_call(123);")]
   [InlineData("Workflow_call(123, {}, empty);")]
   [InlineData("Workflow_call(123, empty, empty);")]
   public void WorkflowCall_WithEmpty_NoCGS022(string source)
   {
      // Workflow_call is an old-style function:
      //   (Number workflowResourceId [required], Array? parameter, All? schedule)
      // 'empty' must not produce a false-positive CGS022 for any optional parameter.
      var diags = Analyze(
         source,
         functions:           KnownNamesLoader.FunctionNames,
         objects:             KnownNamesLoader.ObjectNames,
         functionDefinitions: KnownNamesLoader.FunctionDefinitions);

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void NewStyleFunction_EmptyParam_NoCGS022()
   {
      // Tenant_getByTenantId is a new-style function (Variants) taking a single "string" argument.
      // Passing 'empty' must not produce a false-positive CGS022.
      var diags = Analyze(
         "Tenant_getByTenantId(empty);",
         functions:           KnownNamesLoader.FunctionNames,
         objects:             KnownNamesLoader.ObjectNames,
         functionDefinitions: KnownNamesLoader.FunctionDefinitions);

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
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

   // ── CGS024: empty keyword as method parameter — must never produce false-positive ──

   [Fact]
   public void MethodCall_EmptyParam_NoCGS024()
   {
      // Tenant t; t.AddPasswordCompatTenantUsers(empty)
      // AddPasswordCompatTenantUsers expects a Dictionary argument.
      // 'empty' infers as null (unknown type) and must not produce a false-positive CGS024.
      var result = CgScriptParseService.Parse("Tenant t; t.AddPasswordCompatTenantUsers(empty);");
      var diags = SemanticAnalyzer.Analyze(
         result.Tree,
         KnownNamesLoader.FunctionNames,
         KnownNamesLoader.ObjectNames,
         KnownNamesLoader.ConstantNames,
         objectDefinitions: KnownNamesLoader.ObjectDefinitions);

      Assert.DoesNotContain(diags, d => d.Code == "CGS024");
   }
}
