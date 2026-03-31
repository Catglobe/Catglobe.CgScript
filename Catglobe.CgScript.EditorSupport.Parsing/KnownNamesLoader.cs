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
   /// Full function definitions for built-in functions, keyed by function name.
   /// Used by <see cref="SemanticAnalyzer"/> to validate call argument types.
   /// </summary>
   public static IReadOnlyDictionary<string, FunctionInfo> FunctionDefinitions { get; } = LoadFunctionDefinitions();

   /// <summary>Names of built-in functions that are marked obsolete/deprecated.</summary>
   public static IReadOnlyCollection<string> ObsoleteFunctionNames { get; } = LoadObsoleteFunctionNames();

   /// <summary>Names of built-in constants (enum values) that are marked obsolete/deprecated.</summary>
   public static IReadOnlyCollection<string> ObsoleteConstantNames { get; } = LoadObsoleteConstantNames();

   // ── helpers ──────────────────────────────────────────────────────────────────

   private static IReadOnlyCollection<string> LoadObsoleteFunctionNames()
   {
      if (!TryGetRootProperty("functions", out var functionsElement))
         return System.Array.Empty<string>();

      var result = new HashSet<string>(StringComparer.Ordinal);
      foreach (var funcProp in functionsElement.EnumerateObject())
      {
         if (funcProp.Value.TryGetProperty("isNewStyle", out var isNewStyleEl) && isNewStyleEl.GetBoolean()
             && funcProp.Value.TryGetProperty("variants", out var variantsEl))
         {
            bool allObsolete = true;
            bool hasVariants = false;
            foreach (var variant in variantsEl.EnumerateArray())
            {
               hasVariants = true;
               if (!(variant.TryGetProperty("isObsolete", out var obsEl) && obsEl.GetBoolean()))
               { allObsolete = false; break; }
            }
            if (hasVariants && allObsolete)
               result.Add(funcProp.Name);
         }
      }
      return result;
   }

   private static IReadOnlyCollection<string> LoadObsoleteConstantNames()
   {
      if (!TryGetRootProperty("enums", out var enumsElement))
         return System.Array.Empty<string>();

      var result = new HashSet<string>(StringComparer.Ordinal);
      foreach (var enumProp in enumsElement.EnumerateObject())
      {
         if (!enumProp.Value.TryGetProperty("values", out var valuesEl))
            continue;
         foreach (var v in valuesEl.EnumerateArray())
         {
            if (v.TryGetProperty("isObsolete", out var obsEl) && obsEl.GetBoolean()
                && v.TryGetProperty("name", out var nameEl))
            {
               var name = nameEl.GetString();
               if (!string.IsNullOrEmpty(name))
                  result.Add(name!);
            }
         }
      }
      return result;
   }

   private static IReadOnlyDictionary<string, FunctionInfo> LoadFunctionDefinitions()
   {
      if (!TryGetRootProperty("functions", out var functionsElement))
         return new Dictionary<string, FunctionInfo>();

      var result = new Dictionary<string, FunctionInfo>(StringComparer.Ordinal);

      foreach (var funcProp in functionsElement.EnumerateObject())
      {
         if (!funcProp.Value.TryGetProperty("variants", out var variantsEl))
            continue;

         var overloads    = new List<IReadOnlyList<string>>();
         bool allObsolete = true;
         bool hasVariants = false;
         foreach (var variant in variantsEl.EnumerateArray())
         {
            var paramTypes = new List<string>();
            if (variant.TryGetProperty("param", out var paramEl))
               foreach (var p in paramEl.EnumerateArray())
                  paramTypes.Add(p.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "");
            overloads.Add(paramTypes);
            hasVariants = true;
            if (!(variant.TryGetProperty("isObsolete", out var obsEl) && obsEl.GetBoolean()))
               allObsolete = false;
         }
         result[funcProp.Name] = new FunctionInfo(overloads, isObsolete: hasVariants && allObsolete);
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
         var properties        = new Dictionary<string, bool>(StringComparer.Ordinal);
         var propertyRetTypes  = new Dictionary<string, string>(StringComparer.Ordinal);
         var obsoleteProps     = new Dictionary<string, string?>(StringComparer.Ordinal);
         var methods           = new List<string>();
         var obsoleteMethods   = new Dictionary<string, string?>(StringComparer.Ordinal);
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
                  if (p.TryGetProperty("isObsolete", out var obsEl) && obsEl.GetBoolean())
                   {
                      var doc = p.TryGetProperty("obsoleteDoc", out var docEl) ? docEl.GetString() : null;
                      obsoleteProps[name] = doc;
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
                  if (m.TryGetProperty("isObsolete", out var obsEl) && obsEl.GetBoolean())
                   {
                      var doc2 = m.TryGetProperty("obsoleteDoc", out var docEl2) ? docEl2.GetString() : null;
                      obsoleteMethods[name] = doc2;
                   }
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
            frozenMethodOverloads,
            obsoletePropertyNames: obsoleteProps.Count > 0 ? obsoleteProps : null,
            obsoleteMethodNames:   obsoleteMethods.Count > 0 ? obsoleteMethods : null);
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
