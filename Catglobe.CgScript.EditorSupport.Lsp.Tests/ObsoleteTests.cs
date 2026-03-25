using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
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

   private static DefinitionLoader BuildDefinitions(
      Dictionary<string, ObjectDefinition>?  objects   = null,
      Dictionary<string, FunctionDefinition>? functions = null,
      Dictionary<string, EnumDefinition>?    enums     = null)
   {
      return new TestDefinitionLoader(
         functions ?? new Dictionary<string, FunctionDefinition>(),
         objects   ?? new Dictionary<string, ObjectDefinition>(),
         constants:  enums?.Values
            .SelectMany(e => e.Values.Select(v => v.Name))
            .ToList() ?? new List<string>(),
         globalVariables: new Dictionary<string, string>(),
         enums:  enums ?? new Dictionary<string, EnumDefinition>());
   }

   private static (CgScriptLanguageTarget Target, string Uri) CreateTarget(
      string source, DefinitionLoader definitions)
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
      IEnumerable<string>? functions       = null,
      IEnumerable<string>? objects         = null,
      IEnumerable<string>? constants       = null,
      IReadOnlyDictionary<string, ObjectMemberInfo>? objectDefinitions = null,
      IReadOnlyDictionary<string, string?>? obsoleteFunctions = null,
      IReadOnlyDictionary<string, string?>? obsoleteConstants  = null)
   {
      var result = CgScriptParseService.Parse(source);
      return SemanticAnalyzer.Analyze(
         result.Tree,
         functions ?? [],
         objects   ?? [],
         constants ?? [],
         objectDefinitions: objectDefinitions,
         obsoleteFunctions: obsoleteFunctions,
         obsoleteConstants:  obsoleteConstants);
   }

   // ── Helper test DefinitionLoader ─────────────────────────────────────────────

   private sealed class TestDefinitionLoader : DefinitionLoader
   {
      public TestDefinitionLoader(
         Dictionary<string, FunctionDefinition> functions,
         Dictionary<string, ObjectDefinition>   objects,
         IReadOnlyCollection<string>            constants,
         IReadOnlyDictionary<string, string>    globalVariables,
         Dictionary<string, EnumDefinition>     enums)
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
         Constructors: [],
         Methods: [new MethodDefinition("OldMethod", "Old doc", [], "void", IsObsolete: true)],
         StaticMethods: [],
         Properties: []);

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
         Constructors: [],
         Methods: [new MethodDefinition("NewMethod", "New doc", [], "void", IsObsolete: false)],
         StaticMethods: [],
         Properties: []);

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
         Constructors: [],
         Methods: [],
         StaticMethods: [],
         Properties: [new PropertyDefinition("OldProp", "Old prop doc", true, false, "string", IsObsolete: true)]);

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
         Constructors: [],
         Methods: [],
         StaticMethods: [],
         Properties: [new PropertyDefinition("OldProp", "Old prop", true, false, "string", IsObsolete: true)]);

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
         Constructors: [],
         Methods: [new MethodDefinition("OldMethod", "Old method", [], "void", IsObsolete: true)],
         StaticMethods: [],
         Properties: []);

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
         Values: [new EnumValueDefinition("TEST_OLD", "old value", 1, IsObsolete: true)]);

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
         Values: [new EnumValueDefinition("TEST_OLD", "old value", 1, IsObsolete: true)]);

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
