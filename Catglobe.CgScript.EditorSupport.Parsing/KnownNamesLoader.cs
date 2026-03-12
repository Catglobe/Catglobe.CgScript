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

   // ── helpers ──────────────────────────────────────────────────────────────────

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

            if (typeProp.Value.TryGetProperty("Methods", out var methodsEl))
            {
               foreach (var m in methodsEl.EnumerateArray())
               {
                  var name = m.GetProperty("Name").GetString() ?? "";
                  if (!string.IsNullOrEmpty(name))
                     methods.Add(name);
               }
            }

            result[typeProp.Name] = new ObjectMemberInfo(properties, methods, propertyRetTypes);
         }

         return result;
      }
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
