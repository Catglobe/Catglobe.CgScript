using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;

namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>
/// Loads the names of built-in CgScript functions, object types, and constants from
/// the JSON definition files embedded in this assembly.
/// </summary>
public static class KnownNamesLoader
{
   private static readonly Assembly _asm = typeof(KnownNamesLoader).Assembly;

   /// <summary>Names of all known built-in functions (e.g. "print", "knownFunctions").</summary>
   public static IReadOnlyList<string> FunctionNames { get; } = LoadObjectKeys("CgScriptFunctionDefinitions.json");

   /// <summary>Names of all known built-in object types (e.g. "Tenant", "WorkflowScript").</summary>
   public static IReadOnlyList<string> ObjectNames { get; } = LoadObjectKeys("CgScriptObjectDefinitions.json");

   /// <summary>Names of all known built-in constants (e.g. enum member names).</summary>
   public static IReadOnlyList<string> ConstantNames { get; } = LoadStringArray("CgScriptConstants.json");

   /// <summary>Full member information for all built-in object types, keyed by type name.</summary>
   public static IReadOnlyDictionary<string, ObjectMemberInfo> ObjectDefinitions { get; } = LoadObjectDefinitions();

   /// <summary>Global variables pre-declared by the runtime, mapped to their type name.</summary>
   public static IReadOnlyDictionary<string, string> GlobalVariableTypes { get; } = LoadStringDictionary("CgScriptGlobalVariables.json");

   /// <summary>Names of all known built-in global variables pre-declared by the runtime (e.g. "Catglobe").</summary>
   public static IReadOnlyList<string> GlobalVariableNames { get; } = LoadObjectKeys("CgScriptGlobalVariables.json");

   /// <summary>
   /// Full function definitions for old-style built-in functions, keyed by function name.
   /// Used by <see cref="SemanticAnalyzer"/> to validate call argument types.
   /// New-style functions (with variants/overloads) are excluded.
   /// </summary>
   public static IReadOnlyDictionary<string, FunctionInfo> FunctionDefinitions { get; } = LoadFunctionDefinitions();

   // ── helpers ──────────────────────────────────────────────────────────────────

   private static IReadOnlyDictionary<string, FunctionInfo> LoadFunctionDefinitions()
   {
      var stream = OpenResource("CgScriptFunctionDefinitions.json");
      if (stream is null) return new Dictionary<string, FunctionInfo>();

      using (stream)
      {
         var doc    = JsonDocument.Parse(stream);
         var result = new Dictionary<string, FunctionInfo>(StringComparer.Ordinal);

         foreach (var funcProp in doc.RootElement.EnumerateObject())
         {
            var returnType  = funcProp.Value.TryGetProperty("ReturnType",                  out var rt)  ? rt.GetString()   : null;
            var numRequired = funcProp.Value.TryGetProperty("NumberOfRequiredArguments",    out var nra) ? nra.GetInt32()   : 0;

            var paramInfos = new List<FunctionParamInfo>();
            if (funcProp.Value.TryGetProperty("Parameters", out var paramsEl))
            {
               foreach (var p in paramsEl.EnumerateArray())
               {
                  var constantType = p.TryGetProperty("ConstantType", out var ct) ? ct.GetString() ?? "" : "";
                  var objectType   = p.TryGetProperty("ObjectType",   out var ot) ? ot.GetString() ?? "NONE" : "NONE";
                  paramInfos.Add(new FunctionParamInfo(constantType, objectType));
               }
            }

            // Skip functions with no parameter information.  New-style functions have
            // Variants (not Parameters), so paramInfos stays empty for them.  Old-style
            // functions whose runtime signature is null also produce an empty array.
            // In both cases we have nothing to validate against and must not emit CGS022.
            if (paramInfos.Count == 0)
               continue;

            result[funcProp.Name] = new FunctionInfo(returnType, numRequired, paramInfos);
         }

         return result;
      }
   }

   private static IReadOnlyList<string> LoadObjectKeys(string fileName)
   {
      var stream = OpenResource(fileName);
      if (stream is null) return System.Array.Empty<string>();

      using (stream)
      {
         var doc  = JsonDocument.Parse(stream);
         var keys = new List<string>();
         foreach (var prop in doc.RootElement.EnumerateObject())
            keys.Add(prop.Name);
         return keys;
      }
   }

   private static IReadOnlyDictionary<string, ObjectMemberInfo> LoadObjectDefinitions()
   {
      var stream = OpenResource("CgScriptObjectDefinitions.json");
      if (stream is null) return new Dictionary<string, ObjectMemberInfo>();

      using (stream)
      {
         var doc    = JsonDocument.Parse(stream);
         var result = new Dictionary<string, ObjectMemberInfo>(StringComparer.Ordinal);

         foreach (var typeProp in doc.RootElement.EnumerateObject())
         {
            var properties       = new Dictionary<string, bool>(StringComparer.Ordinal);
            var propertyRetTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            var methods          = new List<string>();
            List<IReadOnlyList<string>>? constructorOverloads = null;
            Dictionary<string, List<IReadOnlyList<string>>>? methodOverloads = null;

            if (typeProp.Value.TryGetProperty("Properties", out var propsEl))
            {
               foreach (var p in propsEl.EnumerateArray())
               {
                  var name      = p.GetProperty("Name").GetString() ?? "";
                  var hasSetter = p.TryGetProperty("HasSetter", out var hse) && hse.GetBoolean();
                  if (!string.IsNullOrEmpty(name))
                  {
                     properties[name] = hasSetter;
                     if (p.TryGetProperty("ReturnType", out var rt))
                     {
                        var retType = rt.GetString() ?? "";
                        if (!string.IsNullOrEmpty(retType))
                           propertyRetTypes[name] = retType;
                     }
                  }
               }
            }

            if (typeProp.Value.TryGetProperty("Constructors", out var ctorsEl))
            {
               constructorOverloads = new List<IReadOnlyList<string>>();
               foreach (var ctor in ctorsEl.EnumerateArray())
                  constructorOverloads.Add(ReadParamTypes(ctor));
            }

            if (typeProp.Value.TryGetProperty("Methods", out var methodsEl))
            {
               methodOverloads = new Dictionary<string, List<IReadOnlyList<string>>>(StringComparer.Ordinal);
               foreach (var m in methodsEl.EnumerateArray())
               {
                  var name = m.GetProperty("Name").GetString() ?? "";
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
   }

   /// <summary>Reads the ordered parameter type list from a constructor or method JSON element.</summary>
   private static IReadOnlyList<string> ReadParamTypes(JsonElement element)
   {
      if (!element.TryGetProperty("Param", out var paramEl))
         return System.Array.Empty<string>();
      var types = new List<string>();
      foreach (var p in paramEl.EnumerateArray())
      {
         var type = p.TryGetProperty("Type", out var t) ? t.GetString() ?? "" : "";
         types.Add(type);
      }
      return types;
   }

   private static IReadOnlyDictionary<string, string> LoadStringDictionary(string fileName)
   {
      var stream = OpenResource(fileName);
      if (stream is null) return new Dictionary<string, string>();

      using (stream)
      {
         var doc    = JsonDocument.Parse(stream);
         var result = new Dictionary<string, string>(StringComparer.Ordinal);
         foreach (var prop in doc.RootElement.EnumerateObject())
         {
            var value = prop.Value.GetString();
            if (value != null)
               result[prop.Name] = value;
         }
         return result;
      }
   }

   private static IReadOnlyList<string> LoadStringArray(string fileName)
   {
      var stream = OpenResource(fileName);
      if (stream is null) return System.Array.Empty<string>();

      using (stream)
      {
         var doc   = JsonDocument.Parse(stream);
         var items = new List<string>();
         foreach (var el in doc.RootElement.EnumerateArray())
            items.Add(el.GetString() ?? "");
         return items;
      }
   }

   private static System.IO.Stream? OpenResource(string fileName)
   {
      var name = _asm.GetManifestResourceNames()
                     .FirstOrDefault(n => n.EndsWith(fileName, System.StringComparison.Ordinal));
      return name is null ? null : _asm.GetManifestResourceStream(name);
   }
}
