using Catglobe.CgScript.EditorSupport.Lsp.Handlers;
using Catglobe.CgScript.EditorSupport.Parsing;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using System.Linq;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

/// <summary>
/// Tests that IsObsolete is surfaced correctly in completion (Detail shows "(deprecated)"),
/// hover (shows "⚠ Deprecated"), and static analysis (CGS026 diagnostic).
/// </summary>
public class ObsoleteTests
{
   // ── Test helpers ─────────────────────────────────────────────────────────────

   private static CgScriptDefinitions BuildDefinitions(
      Dictionary<string, ObjectDefinition>?  objects   = null,
      Dictionary<string, MethodOverload[]>?  functions = null,
      Dictionary<string, EnumDefinition>?    enums     = null)
   {
      return new TestCgScriptDefinitions(
         functions ?? new Dictionary<string, MethodOverload[]>(),
         objects   ?? new Dictionary<string, ObjectDefinition>(),
         constants:  enums?.Values
            .SelectMany(e => e.Values.Select(v => v.Name))
            .ToList() ?? new List<string>(),
         globalVariables: new Dictionary<string, GlobalVariableDefinition>(),
         enums:  enums ?? new Dictionary<string, EnumDefinition>());
   }

   private static (CgScriptLanguageTarget Target, string Uri) CreateTarget(
      string source, CgScriptDefinitions definitions)
   {
      var uri   = "file:///test.cgs";
      var store = new DocumentStore(definitions);
      store.Update(uri, source);
      return (new CgScriptLanguageTarget(store, definitions), uri);
   }

   private static CompletionItem[] GetCompletions(
      CgScriptLanguageTarget target, string uri, int line, int character)
   {
      var p = new CompletionParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = new Uri(uri) },
         Position     = new Position(line, character),
      };
      var result = target.OnCompletion(p);
      if (result is null) return [];
      if (result.Value.TryGetFirst(out var arr)) return arr ?? [];
      if (result.Value.TryGetSecond(out var list)) return list?.Items ?? [];
      return [];
   }

   private static string? GetHoverText(
      CgScriptLanguageTarget target, string uri, int line, int character)
   {
      var hover = target.OnHover(new TextDocumentPositionParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = new Uri(uri) },
         Position     = new Position(line, character),
      });
      if (hover?.Contents.TryGetThird(out var markup) == true) return markup?.Value;
      return null;
   }

   private static IReadOnlyList<Catglobe.CgScript.EditorSupport.Parsing.Diagnostic> AnalyzeDirect(
      string source,
      IEnumerable<string>? functions        = null,
      IEnumerable<string>? objects          = null,
      IEnumerable<string>? constants        = null,
      IReadOnlyDictionary<string, ObjectMemberInfo>? objectDefinitions = null,
      IReadOnlyDictionary<string, string?>? obsoleteFunctions  = null,
      IReadOnlyDictionary<string, string?>? obsoleteConstants   = null)
   {
      var result = CgScriptParseService.Parse(source);

      // Build MethodOverload[] arrays (obsolete functions get a single obsolete overload)
      var funcDefs = new Dictionary<string, MethodOverload[]>(StringComparer.Ordinal);
      foreach (var fn in functions ?? [])
      {
         if (obsoleteFunctions?.ContainsKey(fn) == true)
         {
            obsoleteFunctions.TryGetValue(fn, out var obsDoc);
            funcDefs[fn] = [new MethodOverload("", [], "", ObsoleteDoc: obsDoc ?? "")];
         }
         else
         {
            funcDefs[fn] = [];
         }
      }

      // Build ObjectDefinitions from ObjectMemberInfo
      var objDefs = new Dictionary<string, ObjectDefinition>(StringComparer.Ordinal);
      foreach (var obj in objects ?? [])
      {
         if (objectDefinitions?.TryGetValue(obj, out var info) == true)
         {
            var propDict = new Dictionary<string, PropertyDefinition>(StringComparer.Ordinal);
            foreach (var kvp in info.Properties)
            {
               info.ObsoletePropertyNames.TryGetValue(kvp.Key, out var obsMsg);
               bool isObs = info.ObsoletePropertyNames.ContainsKey(kvp.Key);
               propDict[kvp.Key] = new PropertyDefinition("", kvp.Value, "",
                  ObsoleteDoc: isObs ? (obsMsg ?? "") : null);
            }

            var methodDict = new Dictionary<string, MethodOverload[]>(StringComparer.Ordinal);
            if (info.MethodOverloads != null)
               foreach (var (methodName, overloads) in info.MethodOverloads)
               {
                  bool isObs = info.ObsoleteMethodNames.ContainsKey(methodName);
                  info.ObsoleteMethodNames.TryGetValue(methodName, out var obsMsg);
                  methodDict[methodName] = overloads.Select(overload =>
                     new MethodOverload("",
                        overload.Select((t, i) => new MethodParam($"p{i}", "", t)).ToArray(),
                        "", ObsoleteDoc: isObs ? (obsMsg ?? "") : null)).ToArray();
               }
            foreach (var (methodName, obsMsg) in info.ObsoleteMethodNames)
               if (info.MethodOverloads?.ContainsKey(methodName) != true)
                  methodDict[methodName] = [new MethodOverload("", [], "", ObsoleteDoc: obsMsg ?? "")];

            objDefs[obj] = new ObjectDefinition("",
               Methods: methodDict.Count > 0 ? methodDict : null,
               Properties: propDict.Count > 0 ? propDict : null);
         }
         else
         {
            objDefs[obj] = new ObjectDefinition("");
         }
      }

      // Build enums from constants; obsolete constants get ObsoleteDoc set
      var enums = new Dictionary<string, EnumDefinition>(StringComparer.Ordinal);
      foreach (var c in constants ?? [])
      {
         if (obsoleteConstants?.TryGetValue(c, out var obsMsg) == true)
            enums["_" + c] = new EnumDefinition("", "",
               [new EnumValueDefinition(c, "", 0, ObsoleteDoc: obsMsg ?? "")]);
         else
            enums["_plain_" + c] = new EnumDefinition("", "",
               [new EnumValueDefinition(c, "", 0)]);
      }
      var allConstants = enums.Values.SelectMany(e => e.Values.Select(v => v.Name)).ToList();

      var defs = new TestCgScriptDefinitions(
         funcDefs, objDefs, allConstants,
         new Dictionary<string, GlobalVariableDefinition>(), enums);
      return SemanticAnalyzer.Analyze(result.Tree, defs);
   }

   // ── Helper test CgScriptDefinitions ─────────────────────────────────────────────

   private sealed class TestCgScriptDefinitions : CgScriptDefinitions
   {
      public TestCgScriptDefinitions(
         Dictionary<string, MethodOverload[]>            functions,
         Dictionary<string, ObjectDefinition>            objects,
         IReadOnlyCollection<string>                     constants,
         IReadOnlyDictionary<string, GlobalVariableDefinition> globalVariables,
         Dictionary<string, EnumDefinition>              enums)
         : base(functions, objects, constants, globalVariables, enums)
      {
      }
   }

   // ── Completion: obsolete method shows "(deprecated)" in Detail ────────────────

   [Fact]
   public void Completion_ObsoleteMethod_HasDeprecatedDetail()
   {
      var objDef = new ObjectDefinition(
         "",
         Methods: new Dictionary<string, MethodOverload[]>
         {
            ["OldMethod"] = [new MethodOverload("Old doc", [], "void", ObsoleteDoc: "")]
         });

      var defs = BuildDefinitions(objects: new Dictionary<string, ObjectDefinition>
      {
         ["MyType"] = objDef,
      });

      var source = "MyType x;\nx.";
      var (target, uri) = CreateTarget(source, defs);
      var items = GetCompletions(target, uri, line: 1, character: 2);

      var item = items.FirstOrDefault(i => i.FilterText == "OldMethod");
      Assert.NotNull(item);
      Assert.Contains("deprecated", item.Detail, StringComparison.OrdinalIgnoreCase);
   }

   // ── Completion: non-obsolete method does NOT show "(deprecated)" in Detail ────

   [Fact]
   public void Completion_NonObsoleteMethod_NoDeprecatedDetail()
   {
      var objDef = new ObjectDefinition(
         "",
         Methods: new Dictionary<string, MethodOverload[]>
         {
            ["NewMethod"] = [new MethodOverload("New doc", [], "void")]
         });

      var defs = BuildDefinitions(objects: new Dictionary<string, ObjectDefinition>
      {
         ["MyType"] = objDef,
      });

      var source = "MyType x;\nx.";
      var (target, uri) = CreateTarget(source, defs);
      var items = GetCompletions(target, uri, line: 1, character: 2);

      var item = items.FirstOrDefault(i => i.FilterText == "NewMethod");
      Assert.NotNull(item);
      Assert.DoesNotContain("deprecated", item.Detail ?? "", StringComparison.OrdinalIgnoreCase);
   }

   // ── Completion: obsolete property shows "(deprecated)" in Detail ─────────────

   [Fact]
   public void Completion_ObsoleteProperty_HasDeprecatedDetail()
   {
      var objDef = new ObjectDefinition(
         "",
         Properties: new Dictionary<string, PropertyDefinition>
         {
            ["OldProp"] = new PropertyDefinition("Old prop doc", false, "string", ObsoleteDoc: "")
         });

      var defs = BuildDefinitions(objects: new Dictionary<string, ObjectDefinition>
      {
         ["MyType"] = objDef,
      });

      var source = "MyType x;\nx.";
      var (target, uri) = CreateTarget(source, defs);
      var items = GetCompletions(target, uri, line: 1, character: 2);

      var item = items.FirstOrDefault(i => i.FilterText == "OldProp");
      Assert.NotNull(item);
      Assert.Contains("deprecated", item.Detail, StringComparison.OrdinalIgnoreCase);
   }

   // ── Hover: obsolete property shows "⚠ Deprecated" ───────────────────────────

   [Fact]
   public void Hover_ObsoleteProperty_ShowsDeprecatedWarning()
   {
      var objDef = new ObjectDefinition(
         "",
         Properties: new Dictionary<string, PropertyDefinition>
         {
            ["OldProp"] = new PropertyDefinition("Old prop", false, "string", ObsoleteDoc: "")
         });

      var defs = BuildDefinitions(objects: new Dictionary<string, ObjectDefinition>
      {
         ["MyType"] = objDef,
      });

      var source = "MyType x;\nx.OldProp;";
      var (target, uri) = CreateTarget(source, defs);
      var text = GetHoverText(target, uri, line: 1, character: 3);

      Assert.NotNull(text);
      Assert.Contains("Deprecated", text, StringComparison.OrdinalIgnoreCase);
   }

   // ── Hover: obsolete method shows "⚠ Deprecated" ─────────────────────────────

   [Fact]
   public void Hover_ObsoleteMethod_ShowsDeprecatedWarning()
   {
      var objDef = new ObjectDefinition(
         "",
         Methods: new Dictionary<string, MethodOverload[]>
         {
            ["OldMethod"] = [new MethodOverload("Old method", [], "void", ObsoleteDoc: "")]
         });

      var defs = BuildDefinitions(objects: new Dictionary<string, ObjectDefinition>
      {
         ["MyType"] = objDef,
      });

      var source = "MyType x;\nx.OldMethod();";
      var (target, uri) = CreateTarget(source, defs);
      var text = GetHoverText(target, uri, line: 1, character: 3);

      Assert.NotNull(text);
      Assert.Contains("Deprecated", text, StringComparison.OrdinalIgnoreCase);
   }

   // ── Static check: obsolete function emits CGS026 ─────────────────────────────

   [Fact]
   public void Diagnostic_ObsoleteFunction_EmitsCGS026()
   {
      var diags = AnalyzeDirect(
         "oldFunc();",
         functions:         ["oldFunc"],
         obsoleteFunctions: new Dictionary<string, string?> { ["oldFunc"] = null });

      Assert.Contains(diags, d => d.Code == "CGS026" && d.Message.Contains("oldFunc"));
   }

   // ── Static check: non-obsolete function does NOT emit CGS026 ─────────────────

   [Fact]
   public void Diagnostic_NonObsoleteFunction_NoCGS026()
   {
      var diags = AnalyzeDirect(
         "newFunc();",
         functions: ["newFunc"]);

      Assert.DoesNotContain(diags, d => d.Code == "CGS026");
   }

   // ── Static check: obsolete constant emits CGS026 ─────────────────────────────

   [Fact]
   public void Diagnostic_ObsoleteConstant_EmitsCGS026()
   {
      var diags = AnalyzeDirect(
         "number x = OLD_CONST; print(x);",
         functions:         ["print"],
         constants:         ["OLD_CONST"],
         obsoleteConstants: new Dictionary<string, string?> { ["OLD_CONST"] = null });

      Assert.Contains(diags, d => d.Code == "CGS026" && d.Message.Contains("OLD_CONST"));
   }

   // ── Static check: obsolete property access emits CGS026 ──────────────────────

   [Fact]
   public void Diagnostic_ObsoleteProperty_EmitsCGS026()
   {
      var members = new ObjectMemberInfo(
         new Dictionary<string, bool> { ["OldProp"] = false },
         methodNames: [],
         obsoletePropertyNames: new Dictionary<string, string?> { ["OldProp"] = null });

      var objDefs = new Dictionary<string, ObjectMemberInfo>
      {
         ["MyType"] = members,
      };

      var diags = AnalyzeDirect(
         "MyType x;\nx.OldProp;",
         objects:          ["MyType"],
         objectDefinitions: objDefs);

      Assert.Contains(diags, d => d.Code == "CGS026" && d.Message.Contains("OldProp"));
   }

   // ── Static check: obsolete method call emits CGS026 ──────────────────────────

   [Fact]
   public void Diagnostic_ObsoleteMethod_EmitsCGS026()
   {
      var members = new ObjectMemberInfo(
         properties:  new Dictionary<string, bool>(),
         methodNames: ["OldMethod"],
         obsoleteMethodNames: new Dictionary<string, string?> { ["OldMethod"] = null });

      var objDefs = new Dictionary<string, ObjectMemberInfo>
      {
         ["MyType"] = members,
      };

      var diags = AnalyzeDirect(
         "MyType x;\nx.OldMethod();",
         objects:          ["MyType"],
         objectDefinitions: objDefs);

      Assert.Contains(diags, d => d.Code == "CGS026" && d.Message.Contains("OldMethod"));
   }

   // ── Hover: obsolete enum constant shows "⚠ Deprecated" ──────────────────────

   [Fact]
   public void Hover_ObsoleteEnumConstant_ShowsDeprecatedWarning()
   {
      var enumDef = new EnumDefinition(
         "TEST_", "",
         Values: [new EnumValueDefinition("TEST_OLD", "old value", 1, ObsoleteDoc: "")]);

      var defs = BuildDefinitions(enums: new Dictionary<string, EnumDefinition>
      {
         ["TEST"] = enumDef,
      });

      var source = "TEST_OLD;";
      var (target, uri) = CreateTarget(source, defs);
      var text = GetHoverText(target, uri, line: 0, character: 1);

      Assert.NotNull(text);
      Assert.Contains("Deprecated", text, StringComparison.OrdinalIgnoreCase);
   }

   // ── Completion: obsolete enum constant shows "(deprecated)" in Detail ────────

   [Fact]
   public void Completion_ObsoleteEnumConstant_HasDeprecatedDetail()
   {
      var enumDef = new EnumDefinition(
         "TEST_", "",
         Values: [new EnumValueDefinition("TEST_OLD", "old value", 1, ObsoleteDoc: "")]);

      var defs = BuildDefinitions(enums: new Dictionary<string, EnumDefinition>
      {
         ["TEST"] = enumDef,
      });

      var source = "TEST_OLD";
      var (target, uri) = CreateTarget(source, defs);
      var items = GetCompletions(target, uri, line: 0, character: source.Length);

      var item = items.FirstOrDefault(i => i.Label == "TEST_OLD");
      Assert.NotNull(item);
      Assert.Contains("deprecated", item.Detail ?? "", StringComparison.OrdinalIgnoreCase);
   }
}
