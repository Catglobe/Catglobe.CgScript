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
}
