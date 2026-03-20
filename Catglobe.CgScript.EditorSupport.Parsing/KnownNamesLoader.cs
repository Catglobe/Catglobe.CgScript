using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;

namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>
/// Loads the names of built-in CgScript functions, object types, and constants from
/// the single JSON definition payload embedded in this assembly.
/// </summary>
public static class KnownNamesLoader
{
   private static readonly Assembly _asm = typeof(KnownNamesLoader).Assembly;
   private static readonly JsonDocument? _definitionsDocument = LoadDefinitionsDocument();

   /// <summary>Names of all known built-in functions (e.g. "print", "knownFunctions").</summary>
   public static IReadOnlyList<string> FunctionNames { get; } = LoadObjectKeys("functions");

   /// <summary>Names of all known built-in object types (e.g. "Tenant", "WorkflowScript").</summary>
   public static IReadOnlyList<string> ObjectNames { get; } = LoadObjectKeys("objects");

   /// <summary>Names of all known built-in constants (e.g. enum member names).</summary>
   public static IReadOnlyList<string> ConstantNames { get; } = LoadStringArray("constants");

   /// <summary>Full member information for all built-in object types, keyed by type name.</summary>
   public static IReadOnlyDictionary<string, ObjectMemberInfo> ObjectDefinitions { get; } = LoadObjectDefinitions();

   /// <summary>Global variables pre-declared by the runtime, mapped to their type name.</summary>
   public static IReadOnlyDictionary<string, string> GlobalVariableTypes { get; } = LoadStringDictionary("globalVariables");

   /// <summary>Names of all known built-in global variables pre-declared by the runtime (e.g. "Catglobe").</summary>
   public static IReadOnlyList<string> GlobalVariableNames { get; } = LoadObjectKeys("globalVariables");

   /// <summary>
   /// Full function definitions for old-style built-in functions, keyed by function name.
   /// Used by <see cref="SemanticAnalyzer"/> to validate call argument types.
   /// New-style functions (with variants/overloads) are excluded.
   /// </summary>
   public static IReadOnlyDictionary<string, FunctionInfo> FunctionDefinitions { get; } = LoadFunctionDefinitions();

   // ── helpers ──────────────────────────────────────────────────────────────────

   private static IReadOnlyDictionary<string, FunctionInfo> LoadFunctionDefinitions()
   {
      if (!TryGetRootProperty("functions", out var functionsElement))
         return new Dictionary<string, FunctionInfo>();

      var result = new Dictionary<string, FunctionInfo>(StringComparer.Ordinal);

      foreach (var funcProp in functionsElement.EnumerateObject())
      {
         // New-style functions have variants (overloads) instead of parameters.
         if (funcProp.Value.TryGetProperty("isNewStyle", out var isNewStyleEl) && isNewStyleEl.GetBoolean()
             && funcProp.Value.TryGetProperty("variants", out var variantsEl))
         {
            var overloads = new List<IReadOnlyList<string>>();
            foreach (var variant in variantsEl.EnumerateArray())
            {
               var paramTypes = new List<string>();
               if (variant.TryGetProperty("param", out var paramEl))
                  foreach (var p in paramEl.EnumerateArray())
                     paramTypes.Add(p.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "");
               overloads.Add(paramTypes);
            }
            result[funcProp.Name] = new FunctionInfo(overloads);
            continue;
         }

         var returnType  = funcProp.Value.TryGetProperty("returnType",                 out var rt)  ? rt.GetString() : null;
         var numRequired = funcProp.Value.TryGetProperty("numberOfRequiredArguments",   out var nra) ? nra.GetInt32() : 0;

         var paramInfos = new List<FunctionParamInfo>();
         if (funcProp.Value.TryGetProperty("parameters", out var paramsEl))
         {
            foreach (var p in paramsEl.EnumerateArray())
            {
               var constantType = p.TryGetProperty("constantType", out var ct) ? ct.GetString() ?? "" : "";
               var objectType   = p.TryGetProperty("objectType",   out var ot) ? ot.GetString() ?? "NONE" : "NONE";
               paramInfos.Add(new FunctionParamInfo(constantType, objectType));
            }
         }

         // Skip functions with no parameter information.  Old-style functions whose
         // runtime signature is null also produce an empty array.
         // We have nothing to validate against and must not emit CGS022.
         if (paramInfos.Count == 0)
            continue;

         result[funcProp.Name] = new FunctionInfo(returnType, numRequired, paramInfos);
      }

      return result;
   }

   private static IReadOnlyList<string> LoadObjectKeys(string propertyName)
   {
      if (!TryGetRootProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object)
         return System.Array.Empty<string>();

      var keys = new List<string>();
      foreach (var prop in element.EnumerateObject())
         keys.Add(prop.Name);
      return keys;
   }

   private static IReadOnlyDictionary<string, ObjectMemberInfo> LoadObjectDefinitions()
   {
      if (!TryGetRootProperty("objects", out var objectsElement))
         return new Dictionary<string, ObjectMemberInfo>();

      var result = new Dictionary<string, ObjectMemberInfo>(StringComparer.Ordinal);

      foreach (var typeProp in objectsElement.EnumerateObject())
      {
         var properties       = new Dictionary<string, bool>(StringComparer.Ordinal);
         var propertyRetTypes = new Dictionary<string, string>(StringComparer.Ordinal);
         var methods          = new List<string>();
         List<IReadOnlyList<string>>? constructorOverloads = null;
         Dictionary<string, List<IReadOnlyList<string>>>? methodOverloads = null;

         if (typeProp.Value.TryGetProperty("properties", out var propsEl))
         {
            foreach (var p in propsEl.EnumerateArray())
            {
               var name      = p.GetProperty("name").GetString() ?? "";
               var hasSetter = p.TryGetProperty("hasSetter", out var hse) && hse.GetBoolean();
               if (!string.IsNullOrEmpty(name))
               {
                  properties[name] = hasSetter;
                  if (p.TryGetProperty("returnType", out var rt))
                  {
                     var retType = rt.GetString() ?? "";
                     if (!string.IsNullOrEmpty(retType))
                        propertyRetTypes[name] = retType;
                  }
               }
            }
         }

         if (typeProp.Value.TryGetProperty("constructors", out var ctorsEl))
         {
            constructorOverloads = new List<IReadOnlyList<string>>();
            foreach (var ctor in ctorsEl.EnumerateArray())
               constructorOverloads.Add(ReadParamTypes(ctor));
         }

         if (typeProp.Value.TryGetProperty("methods", out var methodsEl))
         {
            methodOverloads = new Dictionary<string, List<IReadOnlyList<string>>>(StringComparer.Ordinal);
            foreach (var m in methodsEl.EnumerateArray())
            {
               var name = m.GetProperty("name").GetString() ?? "";
               if (!string.IsNullOrEmpty(name))
               {
                  methods.Add(name);
                  if (!methodOverloads.TryGetValue(name, out var overloads))
                  {
                     overloads = new List<IReadOnlyList<string>>();
                     methodOverloads[name] = overloads;
                  }
                  overloads.Add(ReadParamTypes(m));
               }
            }
         }

         IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<string>>>? frozenMethodOverloads = null;
         if (methodOverloads != null)
         {
            var frozen = new Dictionary<string, IReadOnlyList<IReadOnlyList<string>>>(StringComparer.Ordinal);
            foreach (var kvp in methodOverloads)
               frozen[kvp.Key] = kvp.Value;
            frozenMethodOverloads = frozen;
         }

         result[typeProp.Name] = new ObjectMemberInfo(
            properties,
            methods,
            propertyRetTypes,
            constructorOverloads,
            frozenMethodOverloads);
      }

      return result;
   }

   private static JsonDocument? LoadDefinitionsDocument()
   {
      var stream = OpenResource("CgScriptDefinitions.json");
      if (stream is null)
         return null;

      using (stream)
      {
         return JsonDocument.Parse(stream);
      }
   }

   private static bool TryGetRootProperty(string propertyName, out JsonElement element)
   {
      if (_definitionsDocument?.RootElement.TryGetProperty(propertyName, out element) == true)
         return true;

      element = default;
      return false;
   }

   private static IReadOnlyDictionary<string, string> LoadStringDictionary(string propertyName)
   {
      if (!TryGetRootProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object)
         return new Dictionary<string, string>();

      var result = new Dictionary<string, string>(StringComparer.Ordinal);
      foreach (var prop in element.EnumerateObject())
      {
         var value = prop.Value.GetString();
         if (value != null)
            result[prop.Name] = value;
      }

      return result;
   }

   private static IReadOnlyList<string> LoadStringArray(string propertyName)
   {
      if (!TryGetRootProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
         return System.Array.Empty<string>();

      var items = new List<string>();
      foreach (var el in element.EnumerateArray())
         items.Add(el.GetString() ?? "");

      return items;
   }

   /// <summary>Reads the ordered parameter type list from a constructor or method JSON element.</summary>
   private static IReadOnlyList<string> ReadParamTypes(JsonElement element)
   {
      if (!element.TryGetProperty("param", out var paramEl))
         return System.Array.Empty<string>();
      var types = new List<string>();
      foreach (var p in paramEl.EnumerateArray())
      {
         var type = p.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
         types.Add(type);
      }
      return types;
   }

   private static System.IO.Stream? OpenResource(string fileName)
   {
      var name = _asm.GetManifestResourceNames()
                     .FirstOrDefault(n => n.EndsWith(fileName, System.StringComparison.Ordinal));
      return name is null ? null : _asm.GetManifestResourceStream(name);
   }
}
