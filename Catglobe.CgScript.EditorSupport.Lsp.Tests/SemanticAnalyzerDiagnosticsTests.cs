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
   // ── Inner test class ──────────────────────────────────────────────────────

   private sealed class TestCgScriptDefinitions : CgScriptDefinitions
   {
      public TestCgScriptDefinitions(
         Dictionary<string, MethodOverload[]>                  functions,
         Dictionary<string, ObjectDefinition>                  objects,
         IReadOnlyDictionary<string, GlobalVariableDefinition>? globalVariables = null,
         Dictionary<string, EnumDefinition>?                   enums = null)
         : base(functions, objects,
                globalVariables ?? new Dictionary<string, GlobalVariableDefinition>(),
                enums ?? new Dictionary<string, EnumDefinition>(),
                [])
      { }
   }

   private static IReadOnlyList<Diagnostic> Analyze(
      string               source,
      IEnumerable<string>? functions       = null,
      IEnumerable<string>? objects         = null,
      IEnumerable<string>? constants       = null,
      IEnumerable<string>? globalVariables = null,
      IReadOnlyDictionary<string, FunctionInfo>? functionDefinitions = null)
   {
      var result   = CgScriptParseService.Parse(source);
      var funcDefs = new Dictionary<string, MethodOverload[]>(StringComparer.Ordinal);
      foreach (var fn in functions ?? [])
      {
         if (functionDefinitions?.TryGetValue(fn, out var info) == true)
            funcDefs[fn] = info.Variants.Select(overload =>
               new MethodOverload("",
                  overload.Select((t, i) => new MethodParam($"p{i}", "", t)).ToArray(),
                  "")).ToArray();
         else
            funcDefs[fn] = [];
      }
      var objDefs = new Dictionary<string, ObjectDefinition>(StringComparer.Ordinal);
      foreach (var obj in objects ?? [])
         objDefs[obj] = new ObjectDefinition("");
      var globalVarDefs = globalVariables is null
         ? new Dictionary<string, GlobalVariableDefinition>()
         : globalVariables.ToDictionary(v => v, _ => new GlobalVariableDefinition(""), StringComparer.Ordinal);
      // Put constants into enums so ConstantsSet is populated correctly
      var enumDefs = new Dictionary<string, EnumDefinition>(StringComparer.Ordinal);
      foreach (var c in constants ?? [])
         enumDefs["_plain_" + c] = new EnumDefinition("", "", [new EnumValueDefinition(c, "", 0)]);
      return SemanticAnalyzer.Analyze(result.Tree,
         new TestCgScriptDefinitions(funcDefs, objDefs, globalVarDefs, enumDefs));
   }

   private static IReadOnlyList<Diagnostic> AnalyzeWithObjects(
      string source,
      IReadOnlyDictionary<string, ObjectDefinition> objectDefinitions,
      IEnumerable<string>? functions = null)
   {
      var result   = CgScriptParseService.Parse(source);
      var funcDefs = new Dictionary<string, MethodOverload[]>(StringComparer.Ordinal);
      foreach (var fn in functions ?? [])
         funcDefs[fn] = [];
      return SemanticAnalyzer.Analyze(result.Tree,
         new TestCgScriptDefinitions(funcDefs,
            objectDefinitions.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)));
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
      var result = CgScriptParseService.Parse("number b = DATETIME_DAY; print(b);");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

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
      var result = CgScriptParseService.Parse("string s = Catglobe; print(s);");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

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
   public void BoolVar_AssignedBoolLiteral_NoCGS020()
   {
      var diags = Analyze("bool b = true;");

      Assert.DoesNotContain(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void BoolVar_AssignedFalseLiteral_NoCGS020()
   {
      var diags = Analyze("bool b = false;");

      Assert.DoesNotContain(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void ArrayVar_AssignedArrayLiteral_NoCGS020()
   {
      var diags = Analyze("array a = {1, 2, 3};");

      Assert.DoesNotContain(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void ArrayVar_AssignedRangeLiteral_ReportsCGS020()
   {
      // [1, 2, 3] is a Range literal in CgScript, not an array
      var diags = Analyze("array a = [1, 2, 3];");

      Assert.Contains(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void FunctionVar_AssignedFunctionLiteral_NoCGS020()
   {
      var diags = Analyze("function f = function() {};");

      Assert.DoesNotContain(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void ObjectVar_AssignedAnything_NoCGS020()
   {
      // object and ? types accept any value
      var diags = Analyze("object o = 123;");

      Assert.DoesNotContain(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void BoolVar_AssignedNumber_ReportsCGS020()
   {
      var diags = Analyze("bool b = 1;");

      Assert.Contains(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void BoolVar_AssignedString_ReportsCGS020()
   {
      var diags = Analyze("bool b = \"text\";");

      Assert.Contains(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void BoolVar_AssignedNewArray_ReportsCGS020()
   {
      var diags = Analyze("bool b = new Array();", objects: ["Array"]);

      Assert.Contains(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void ArrayVar_AssignedNumber_ReportsCGS020()
   {
      var diags = Analyze("array a = 42;");

      Assert.Contains(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void ArrayVar_AssignedString_ReportsCGS020()
   {
      var diags = Analyze("array a = \"text\";");

      Assert.Contains(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void ArrayVar_AssignedBool_ReportsCGS020()
   {
      var diags = Analyze("array a = true;");

      Assert.Contains(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void FunctionVar_AssignedNumber_ReportsCGS020()
   {
      var diags = Analyze("function f = 1;");

      Assert.Contains(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void FunctionVar_AssignedString_ReportsCGS020()
   {
      var diags = Analyze("function f = \"text\";");

      Assert.Contains(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void NumberVar_AssignedBool_ReportsCGS020()
   {
      var diags = Analyze("number n = true;");

      Assert.Contains(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void StringVar_AssignedBool_ReportsCGS020()
   {
      var diags = Analyze("string s = true;");

      Assert.Contains(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void NumberVar_AssignedClassInstance_ReportsCGS020()
   {
      var diags = Analyze("number n = new Dictionary();", objects: ["Dictionary"]);

      Assert.Contains(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void StringVar_AssignedNewArray_ReportsCGS020()
   {
      var diags = Analyze("string s = new Array();", objects: ["Array"]);

      Assert.Contains(diags, d => d.Code == "CGS020");
   }

   // ── CGS020: class type assignment validation ──────────────────────────────

   [Fact]
   public void DictionaryVar_AssignedNewArray_ReportsCGS020()
   {
      // Dictionary d = new Array() — Array does not inherit from Dictionary
      var result = CgScriptParseService.Parse("Dictionary d = new Array();");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.Contains(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void QuestionVar_AssignedNumber_NoCGS020()
   {
      // question Q1 = 42 — question keyword type accepts any value
      var diags = Analyze("question Q1 = 42;");

      Assert.DoesNotContain(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void QuestionSubclassVar_AssignedArrayLiteral_NoCGS020()
   {
      // MultiGridQuestion Q2 = [2,3] — inherits from Question which accepts anything
      var result = CgScriptParseService.Parse("MultiGridQuestion Q2 = [2,3];");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void ParentClassVar_AssignedChildClassInstance_NoCGS020()
   {
      // CatTaskSchedule t = new CatTaskNeverSchedule() — CatTaskNeverSchedule inherits CatTaskSchedule
      var result = CgScriptParseService.Parse("CatTaskSchedule t = new CatTaskNeverSchedule();");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS020");
   }

   [Fact]
   public void UnrelatedClassVar_AssignedOtherClass_ReportsCGS020()
   {
      // Array a = new Dictionary() — Dictionary does not inherit from Array
      var result = CgScriptParseService.Parse("Array a = new Dictionary();");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.Contains(diags, d => d.Code == "CGS020");
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
         ["DateTime_addDays"] = new FunctionInfo([["Array", "Number"]]),
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
      var getDateTimeFuncInfo = new FunctionInfo([[]]);

      var funcDefs = new Dictionary<string, FunctionInfo>
      {
         ["DateTime_addDays"] = new FunctionInfo([["Array", "Number"]]),
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
         ["myFunc"] = new FunctionInfo([["Number"]]),
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
      var result = CgScriptParseService.Parse("DateTime_addDays(new DateTime(), 1);");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void KnownFunctionsFromLoader_TooFewArgs_ReportsCGS022()
   {
      // DateTime_getLastDateOfMonth requires 2 args (Year:int, Month:int); calling with 1 should be flagged
      var result = CgScriptParseService.Parse("DateTime_getLastDateOfMonth(2024);");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.Contains(diags, d => d.Code == "CGS022"
                                  && d.Message.Contains("DateTime_getLastDateOfMonth"));
   }

   [Fact]
   public void ConvertToNumber_CalledWithString_NoCGS022()
   {
      // convertToNumber("[avatarStore]") is valid; no false-positive CGS022 expected
      var result = CgScriptParseService.Parse("number n = convertToNumber(\"[avatarStore]\");");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void Format_CalledWithStringAndArg_NoCGS022()
   {
      // format("{0}foo", location) is valid; no false-positive CGS022 expected
      var result = CgScriptParseService.Parse("string location = \"x\"; string s = format(\"{0}foo\", location);");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   // ── CGS022: DocumentStore path (regression for empty-Parameters bug) ──────

   private static IReadOnlyList<Diagnostic> AnalyzeViaDocumentStore(string source)
   {
      var uri   = "file:///test.cgs";
      var store = new DocumentStore(new CgScriptDefinitions());
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
   public void QuestionSubclass_AsAnyFunctionParam_NoCGS022()
   {
      // indexOf("Mobile", q) where q is SingleQuestion — question family types can be
      // passed as any parameter type due to implicit runtime conversion.
      var result = CgScriptParseService.Parse("SingleQuestion q; indexOf(\"Mobile\", q);");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void CGS022_ErrorMessage_UsesProperEnglish()
   {
      // Error message must say "No overload of 'X' matches (...)", not "Doesn't has X with format (...)"
      var funcDefs = new Dictionary<string, FunctionInfo>
      {
         ["myFunc"] = new FunctionInfo([["Number"]]),
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
      var result = CgScriptParseService.Parse("AppProduct_getById(1);");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void NewStyleFunction_WrongArgType_ReportsCGS022()
   {
      // AppProduct_getById expects a Number (int); passing a string literal is wrong
      var result = CgScriptParseService.Parse("AppProduct_getById(\"not-an-id\");");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.Contains(diags, d => d.Code == "CGS022" && d.Message.Contains("AppProduct_getById"));
   }

   [Fact]
   public void NewStyleFunction_MultipleVariants_FirstVariantValid_NoCGS022()
   {
      // Color_getByRGB has two variants: (string) and (int, int, int)
      // Calling with a single string should match the first variant.
      var result = CgScriptParseService.Parse("Color_getByRGB(\"#ff0000\");");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void NewStyleFunction_MultipleVariants_SecondVariantValid_NoCGS022()
   {
      // Color_getByRGB(int, int, int) — second variant
      var result = CgScriptParseService.Parse("Color_getByRGB(255, 0, 128);");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void NewStyleFunction_WrongArgCount_ReportsCGS022()
   {
      // Color_getByRGB has variants (string) and (int, int, int); 2 args matches neither
      var result = CgScriptParseService.Parse("Color_getByRGB(1, 2);");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

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
   [InlineData("Workflow_call(123, empty);")]
   [InlineData("Workflow_call(123);")]
   [InlineData("Workflow_call(123, {}, empty);")]
   [InlineData("Workflow_call(123, empty, empty);")]
   public void WorkflowCall_WithEmpty_NoCGS022(string source)
   {
      // Workflow_call is an old-style function:
      //   (Number workflowResourceId [required], Array? parameter, All? schedule)
      // 'empty' must not produce a false-positive CGS022 for any optional parameter.
      var result = CgScriptParseService.Parse(source);
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void NewStyleFunction_EmptyParam_NoCGS022()
   {
      // Tenant_getByTenantId is a new-style function (Variants) taking a single "string" argument.
      // Passing 'empty' must not produce a false-positive CGS022.
      var result = CgScriptParseService.Parse("Tenant_getByTenantId(empty);");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   // ── CGS022: dict literal must infer as Dictionary, not Array ─────────────

   [Fact]
   public void DictLiteral_InferredAsDictionary_NoCGS022()
   {
      // Regression: {"key": 4} was inferred as "Array" instead of "Dictionary",
      // causing false CGS022 when passing to a function expecting Dictionary.
      var funcDefs = new Dictionary<string, FunctionInfo>
      {
         ["myFunc"] = new FunctionInfo([["Dictionary"]]),
      };

      var diags = Analyze(
         "myFunc({\"key\": 4});",
         functions:           ["myFunc"],
         functionDefinitions: funcDefs);

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void ArrayLiteral_StillInferredAsArray_NoCGS022()
   {
      // {1, 2, 3} must still infer as Array when passed to a function expecting Array.
      var funcDefs = new Dictionary<string, FunctionInfo>
      {
         ["myFunc"] = new FunctionInfo([["Array"]]),
      };

      var diags = Analyze(
         "myFunc({1, 2, 3});",
         functions:           ["myFunc"],
         functionDefinitions: funcDefs);

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void MultilineDictLiteral_InferredAsDictionary_NoCGS022()
   {
      // Multi-line form: {\n"key": 4\n} must also infer as Dictionary.
      var funcDefs = new Dictionary<string, FunctionInfo>
      {
         ["myFunc"] = new FunctionInfo([["Dictionary"]]),
      };

      var diags = Analyze(
         "myFunc({\n\"key\": 4\n});",
         functions:           ["myFunc"],
         functionDefinitions: funcDefs);

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void DateLiteral_InferredAsDateTime_NoCGS022()
   {
      // Regression: DATE_LITERAL was incorrectly inferred as "Array" instead of "DateTime".
      var funcDefs = new Dictionary<string, FunctionInfo>
      {
         ["myFunc"] = new FunctionInfo([["DateTime"]]),
      };

      var diags = Analyze(
         "myFunc(#2024-01-15#);",
         functions:           ["myFunc"],
         functionDefinitions: funcDefs);

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void DateLiteral_PassedToArrayParam_NoCGS022()
   {
      // DateTime is a subtype of Array (DateTime.Parent = "Array" in definitions),
      // so passing a date literal where Array is expected must not produce CGS022.
      // Uses full definitions so IsSubtypeOf can walk the DateTime→Array parent chain.
      var diags = AnalyzeViaDocumentStore("DateTime_addDays(#2024-01-15#, 1);");

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   [Fact]
   public void ArrayLiteral_PassedToDateTimeParam_NoCGS022()
   {
      // The runtime accepts old-style {year, month, day} arrays for DateTime params.
      var funcDefs = new Dictionary<string, FunctionInfo>
      {
         ["myFunc"] = new FunctionInfo([["DateTime"]]),
      };

      var diags = Analyze(
         "myFunc({2024, 1, 15});",
         functions:           ["myFunc"],
         functionDefinitions: funcDefs);

      Assert.DoesNotContain(diags, d => d.Code == "CGS022");
   }

   // ── CGS023: constructor argument mismatch ─────────────────────────────────

   private static ObjectDefinition MakeStringDef()
      => new ObjectDefinition("",
         Constructors: [new MethodOverload("", [new MethodParam("p0", "", "string")], "")]);

   [Fact]
   public void Constructor_TooManyArgs_ReportsCGS023()
   {
      // new String(1, 2) — only one constructor taking a single string arg
      var diags = AnalyzeWithObjects(
         "string s = new String(1, 2);",
         new Dictionary<string, ObjectDefinition> { ["String"] = MakeStringDef() });

      Assert.Contains(diags, d => d.Code == "CGS023" && d.Message.Contains("String"));
   }

   [Fact]
   public void Constructor_WrongArgType_ReportsCGS023()
   {
      // new String(1) — constructor expects string, got number
      var diags = AnalyzeWithObjects(
         "string s = new String(1);",
         new Dictionary<string, ObjectDefinition> { ["String"] = MakeStringDef() });

      Assert.Contains(diags, d => d.Code == "CGS023" && d.Message.Contains("String"));
   }

   [Fact]
   public void Constructor_NoArgs_ReportsCGS023()
   {
      // new String() — constructor requires a string arg; zero args is a mismatch
      var diags = AnalyzeWithObjects(
         "string s = new String();",
         new Dictionary<string, ObjectDefinition> { ["String"] = MakeStringDef() });

      Assert.Contains(diags, d => d.Code == "CGS023" && d.Message.Contains("String"));
   }

   [Fact]
   public void Constructor_CorrectArgType_NoCGS023()
   {
      // new String("test") — valid
      var diags = AnalyzeWithObjects(
         "string s = new String(\"test\");",
         new Dictionary<string, ObjectDefinition> { ["String"] = MakeStringDef() });

      Assert.DoesNotContain(diags, d => d.Code == "CGS023");
   }

   [Fact]
   public void KnownObjectsFromLoader_ValidConstructor_NoCGS023()
   {
      // new String("hello") — valid according to embedded JSON definitions
      var result = CgScriptParseService.Parse("string s = new String(\"hello\");");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS023");
   }

   [Fact]
   public void KnownObjectsFromLoader_InvalidConstructorArgs_ReportsCGS023()
   {
      // new String(1, 2) — String has only one constructor that takes a string arg
      var result = CgScriptParseService.Parse("string s = new String(1, 2);");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.Contains(diags, d => d.Code == "CGS023" && d.Message.Contains("String"));
   }

   [Fact]
   public void WorkflowScript_FilenameOverload_NoCGS023()
   {
      // new WorkflowScript("filename") is a preprocessor special form that the source generator
      // replaces with new WorkflowScript(resourceId) on deployment.  No CGS023 should be raised.
      // WithPreprocessorExtensions() must be called — this overload is NOT in the base definitions.
      var result = CgScriptParseService.Parse("WorkflowScript script = new WorkflowScript(\"myScript.cgs\");");
      var diags = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions().WithPreprocessorExtensions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS023");
   }

   [Fact]
   public void WorkflowScript_FilenameOverload_NotInjectedInBaseDefinitions()
   {
      // Without WithPreprocessorExtensions(), new WorkflowScript("filename") should report CGS023
      // because the base definitions only know about the real API constructors.
      var result = CgScriptParseService.Parse("WorkflowScript script = new WorkflowScript(\"myScript.cgs\");");
      var diags = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.Contains(diags, d => d.Code == "CGS023");
   }

   [Fact]
   public void WorkflowScript_TooManyStringArgs_ReportsCGS023()
   {
      // new WorkflowScript("a", "b", "c") — no constructor accepts three strings even with preprocessor extensions
      var result = CgScriptParseService.Parse("WorkflowScript script = new WorkflowScript(\"a\", \"b\", \"c\");");
      var diags = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions().WithPreprocessorExtensions());

      Assert.Contains(diags, d => d.Code == "CGS023" && d.Message.Contains("WorkflowScript"));
   }

   // ── CGS024: method call argument mismatch ─────────────────────────────────

   private static ObjectDefinition MakeStringWithCompareDef()
      => new ObjectDefinition("",
         Methods: new Dictionary<string, MethodOverload[]>
         {
            ["CompareTo"] = [new MethodOverload("", [new MethodParam("p0", "", "string")], "")]
         });

   [Fact]
   public void MethodCall_WrongArgType_ReportsCGS024()
   {
      // s.CompareTo(1) — method expects string, got number
      var diags = AnalyzeWithObjects(
         "String s = new String(); s.CompareTo(1);",
         new Dictionary<string, ObjectDefinition> { ["String"] = MakeStringWithCompareDef() });

      Assert.Contains(diags, d => d.Code == "CGS024"
                                  && d.Message.Contains("String.CompareTo"));
   }

   [Fact]
   public void MethodCall_CorrectArgType_NoCGS024()
   {
      // s.CompareTo("other") — valid
      var diags = AnalyzeWithObjects(
         "String s = new String(); s.CompareTo(\"other\");",
         new Dictionary<string, ObjectDefinition> { ["String"] = MakeStringWithCompareDef() });

      Assert.DoesNotContain(diags, d => d.Code == "CGS024");
   }

   [Fact]
   public void MethodCall_TooManyArgs_ReportsCGS024()
   {
      // s.CompareTo("a", "b") — method only takes one arg
      var diags = AnalyzeWithObjects(
         "String s = new String(); s.CompareTo(\"a\", \"b\");",
         new Dictionary<string, ObjectDefinition> { ["String"] = MakeStringWithCompareDef() });

      Assert.Contains(diags, d => d.Code == "CGS024"
                                  && d.Message.Contains("String.CompareTo"));
   }

   [Fact]
   public void KnownObjectsFromLoader_ValidMethodCall_NoCGS024()
   {
      // s.CompareTo("other") using embedded definitions
      var result = CgScriptParseService.Parse("String s = new String(\"x\"); s.CompareTo(\"other\");");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS024");
   }

   [Fact]
   public void KnownObjectsFromLoader_WrongMethodArgType_ReportsCGS024()
   {
      // s.CompareTo(1) — String.CompareTo expects a string
      var result = CgScriptParseService.Parse("String s = new String(\"x\"); s.CompareTo(1);");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

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
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS024");
   }

   // ── CGS025: indexer argument type mismatch ────────────────────────────────

   private static ObjectDefinition MakeDictionaryDef()
      => new ObjectDefinition("",
         Methods: new Dictionary<string, MethodOverload[]>
         {
            ["[]"] = [
               new MethodOverload("", [new MethodParam("k", "", "string")], ""),
               new MethodOverload("", [new MethodParam("k", "", "int")], ""),
               new MethodOverload("", [new MethodParam("k", "", "string"), new MethodParam("v", "", "object")], ""),
               new MethodOverload("", [new MethodParam("k", "", "int"), new MethodParam("v", "", "object")], ""),
            ]
         });

   [Fact]
   public void Indexer_ValidStringKey_NoCGS025()
   {
      // d["key"] — string key is valid
      var diags = AnalyzeWithObjects(
         "Dictionary d; d[\"key\"];",
         new Dictionary<string, ObjectDefinition> { ["Dictionary"] = MakeDictionaryDef() });

      Assert.DoesNotContain(diags, d => d.Code == "CGS025");
   }

   [Fact]
   public void Indexer_ValidIntKey_NoCGS025()
   {
      // d[1] — int key is valid
      var diags = AnalyzeWithObjects(
         "Dictionary d; d[1];",
         new Dictionary<string, ObjectDefinition> { ["Dictionary"] = MakeDictionaryDef() });

      Assert.DoesNotContain(diags, d => d.Code == "CGS025");
   }

   [Fact]
   public void Indexer_InvalidBoolKey_ReportsCGS025()
   {
      // d[true] — bool key is not valid
      var diags = AnalyzeWithObjects(
         "Dictionary d; d[true];",
         new Dictionary<string, ObjectDefinition> { ["Dictionary"] = MakeDictionaryDef() });

      Assert.Contains(diags, d => d.Code == "CGS025" && d.Message.Contains("Dictionary"));
   }

   [Fact]
   public void IndexerSetter_ValidStringKey_NoCGS025()
   {
      // d["key"] = "v" — string key is valid for setter
      var diags = AnalyzeWithObjects(
         "Dictionary d; d[\"key\"] = \"v\";",
         new Dictionary<string, ObjectDefinition> { ["Dictionary"] = MakeDictionaryDef() });

      Assert.DoesNotContain(diags, d => d.Code == "CGS025");
   }

   [Fact]
   public void IndexerSetter_InvalidBoolKey_ReportsCGS025()
   {
      // d[true] = "v" — bool key is not valid for setter
      var diags = AnalyzeWithObjects(
         "Dictionary d; d[true] = \"v\";",
         new Dictionary<string, ObjectDefinition> { ["Dictionary"] = MakeDictionaryDef() });

      Assert.Contains(diags, d => d.Code == "CGS025" && d.Message.Contains("Dictionary"));
   }

   [Fact]
   public void Indexer_UnknownKeyType_NoCGS025()
   {
      // d[x] — unknown variable type → no false positive
      var diags = AnalyzeWithObjects(
         "Dictionary d; object x; d[x];",
         new Dictionary<string, ObjectDefinition> { ["Dictionary"] = MakeDictionaryDef() },
         functions: []);

      Assert.DoesNotContain(diags, d => d.Code == "CGS025");
   }

   [Fact]
   public void IndexerSetter_FunctionParamShadowsGlobal_NoCGS025()
   {
      // Regression: function parameter 'u' of type Dictionary must shadow the global
      // variable 'u' of type User — the indexer setter u["Name"] = "Remove" should not
      // produce a false CGS025 error referencing the global User type.
      const string source =
         "User u;\n" +
         "u;\n" +
         "someFunc(function(Dictionary u) {\n" +
         "   u[\"Name\"] = \"Remove\";\n" +
         "});";
      var objDefs = new Dictionary<string, ObjectDefinition>
      {
         ["Dictionary"] = MakeDictionaryDef(),
         ["User"]       = new ObjectDefinition("", [], [], [], []),
      };
      var diags = AnalyzeWithObjects(source, objDefs, functions: ["someFunc"]);

      Assert.DoesNotContain(diags, d => d.Code == "CGS025");
   }

   [Fact]
   public void IndexerSetter_ForEachVarShadowsGlobal_NoCGS025()
   {
      // Regression: for-each loop variable 'u' shadows the global 'User u'.
      // Inside the loop body, 'u' is the loop variable (untyped), so accessing
      // u["key"] should NOT be validated against User's indexer.
      const string source =
         "User u;\n" +
         "u;\n" +
         "for (u for 1; 10) {\n" +
         "   u[\"Name\"] = \"Remove\";\n" +
         "}";
      var objDefs = new Dictionary<string, ObjectDefinition>
      {
         ["Dictionary"] = MakeDictionaryDef(),
         ["User"]       = new ObjectDefinition("", [], [], [], []),
      };
      var diags = AnalyzeWithObjects(source, objDefs, functions: []);

      Assert.DoesNotContain(diags, d => d.Code == "CGS025");
   }

   [Fact]
   public void IndexerSetter_CatchVarShadowsGlobal_NoCGS025()
   {
      // Regression: catch variable 'u' shadows the global 'User u'.
      // Inside the catch body, 'u' is the catch variable (untyped), so accessing
      // u["key"] should NOT be validated against User's indexer.
      const string source =
         "User u;\n" +
         "u;\n" +
         "try { } catch (u) {\n" +
         "   u[\"Name\"] = \"Remove\";\n" +
         "}";
      var objDefs = new Dictionary<string, ObjectDefinition>
      {
         ["Dictionary"] = MakeDictionaryDef(),
         ["User"]       = new ObjectDefinition("", [], [], [], []),
      };
      var diags = AnalyzeWithObjects(source, objDefs, functions: []);

      Assert.DoesNotContain(diags, d => d.Code == "CGS025");
   }

   [Fact]
   public void Indexer_RealDictionary_ValidStringKey_NoCGS025()
   {
      // Use the embedded Dictionary definition
      var result = CgScriptParseService.Parse("Dictionary d; d[\"key\"];");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS025");
   }

   [Fact]
   public void Indexer_RealDictionary_InvalidBoolKey_ReportsCGS025()
   {
      // Dictionary getter only accepts string or int, not bool
      var result = CgScriptParseService.Parse("Dictionary d; d[true];");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.Contains(diags, d => d.Code == "CGS025");
   }

   [Fact]
   public void Indexer_RealArray_ValidIntIndex_NoCGS025()
   {
      // 'Array' (capital A, class name) is indexed with int — valid
      var result = CgScriptParseService.Parse("Array a; a[0];");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS025");
   }

   [Fact]
   public void Indexer_RealArray_InvalidStringIndex_ReportsCGS025()
   {
      // Array getter only accepts int, not string
      var result = CgScriptParseService.Parse("Array a; a[\"x\"];");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.Contains(diags, d => d.Code == "CGS025");
   }

   // ── CGS024: variadic methods (Params object) — must never produce false-positive ──

   [Fact]
   public void FunctionCall_WithOneArg_NoCGS024()
   {
      // Function.Call accepts a variadic "Params object" — any number of args is valid.
      var result = CgScriptParseService.Parse("Function f = new Function(\"myFunc\"); f.Call(1);");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS024");
   }

   [Fact]
   public void FunctionCall_WithMultipleArgs_NoCGS024()
   {
      // Function.Call(a, b, c) — variadic, any number of arguments is valid.
      var result = CgScriptParseService.Parse("Function f = new Function(\"myFunc\"); f.Call(1, 2, 3);");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS024");
   }

   [Fact]
   public void WorkflowScriptCall_WithMultipleArgs_NoCGS024()
   {
      // WorkflowScript.Call also accepts "Params object" — variadic.
      var result = CgScriptParseService.Parse("WorkflowScript ws = new WorkflowScript(\"Test\"); ws.Call(1, 2);");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS024");
   }

   [Fact]
   public void StringBuilderAppendFormat_WithMultipleArgs_NoCGS024()
   {
      // StringBuilder.AppendFormat("{0}", value) — variadic "Params object" after format string.
      var result = CgScriptParseService.Parse("StringBuilder sb = new StringBuilder(); sb.AppendFormat(\"{0} {1}\", 1, 2);");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS024");
   }

   [Fact]
   public void FormatFunction_WithMultipleArgs_NoCGS024()
   {
      // format("{0} {1}", a, b) — format accepts "Params object" so any number of args is valid.
      var result = CgScriptParseService.Parse("string s = format(\"{0} {1}\", 1, 2);");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS024");
   }

   [Fact]
   public void WorkflowScriptInvoke_WithArrayArg_NoCGS024()
   {
      // WorkflowScript.Invoke(array) — exactly one array argument is valid.
      // WithPreprocessorExtensions() is needed because new WorkflowScript("filename") is the preprocessor form.
      var result = CgScriptParseService.Parse("WorkflowScript ws = new WorkflowScript(\"test.cgs\"); array args = new array(); ws.Invoke(args);");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions().WithPreprocessorExtensions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS024");
   }

   // ── CGS027: where-expression function validation ──────────────────────────

   [Fact]
   public void WhereExpr_KnownFunction_NoDiagnostics()
   {
      // selectColumn(q1) where condition — no errors; q1 is a column name, not a variable.
      var result = CgScriptParseService.Parse("number n = selectColumn(q1) where 1 == 1;");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code is "CGS004" or "CGS005" or "CGS027");
   }

   [Fact]
   public void WhereExpr_AllKnownAggregators_ProduceNoDiagnostics()
   {
      // Every non-obsolete where-expression function must produce zero diagnostics.
      var functions = new[]
      {
         "average(q1) where 1 == 1",
         "count() where 1 == 1",
         "countAnswer(q1) where 1 == 1",
         "max(q1) where 1 == 1",
         "min(q1) where 1 == 1",
         "sum(q1) where 1 == 1",
         "median(q1) where 1 == 1",
         "variance(q1) where 1 == 1",
         "stdev(q1) where 1 == 1",
         "sterr(q1) where 1 == 1",
         "selectMultiColumn(q1, q2) where 1 == 1",
         "selectMultiColumnReadOnly(q1, q2) where 1 == 1",
         "selectDictionary(q1, q2) where 1 == 1",
         "selectDictionaryReadOnly(q1, q2) where 1 == 1",
         "selectMultiDictionary(q1, q2) where 1 == 1",
         "selectMultiDictionaryReadOnly(q1, q2) where 1 == 1",
         "quantile(q1, 4, 2) where 1 == 1",
         "percentile(q1, 75) where 1 == 1",
         "explainFreeText(10) where 1 == 1",
      };

      foreach (var expr in functions)
      {
         var result = CgScriptParseService.Parse($"{expr};");
         var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());
         Assert.Empty(diags);
      }
   }

   [Fact]
   public void WhereExpr_ObsoleteFunctions_ReportCGS026()
   {
      // select() and selectColumn() are obsolete — each must produce exactly one CGS026 warning.
      foreach (var expr in new[] { "select(q1) where 1 == 1", "selectColumn(q1) where 1 == 1" })
      {
         var result = CgScriptParseService.Parse($"{expr};");
         var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());
         Assert.Single(diags, d => d.Code == "CGS026");
      }
   }

   [Fact]
   public void WhereExpr_UnknownFunction_ReportsCGS027()
   {
      // notAWhereFunc(q1) where condition — should report CGS027.
      var result = CgScriptParseService.Parse("number n = notAWhereFunc(q1) where 1 == 1;");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.Contains(diags, d => d.Code == "CGS027" && d.Message.Contains("notAWhereFunc"));
   }

   [Fact]
   public void WhereExpr_ColumnNameArgs_NoCGS005()
   {
      // Column name identifiers (q1, q2) must not produce CGS005 "Undefined variable".
      var result = CgScriptParseService.Parse("number n = average(undeclaredColumn) where 1 == 1;");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.DoesNotContain(diags, d => d.Code == "CGS005");
   }

   [Fact]
   public void WhereExpr_ExpressionParams_AreValidatedNormally()
   {
      // quantile(col, q, k) — q and k are expressions; an undefined variable there must still produce CGS005.
      var result = CgScriptParseService.Parse("number n = quantile(q1, undeclaredVar, 4) where 1 == 1;");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.Contains(diags, d => d.Code == "CGS005" && d.Message.Contains("undeclaredVar"));
      Assert.DoesNotContain(diags, d => d.Code == "CGS005" && d.Message.Contains("q1"));
   }

   [Fact]
   public void WhereExpr_Condition_IsValidatedNormally()
   {
      // The RHS condition uses an undeclared variable — CGS005 must fire for it.
      var result = CgScriptParseService.Parse("number n = count() where undeclaredCond == 1;");
      var diags  = SemanticAnalyzer.Analyze(result.Tree, new CgScriptDefinitions());

      Assert.Contains(diags, d => d.Code == "CGS005" && d.Message.Contains("undeclaredCond"));
   }
}
