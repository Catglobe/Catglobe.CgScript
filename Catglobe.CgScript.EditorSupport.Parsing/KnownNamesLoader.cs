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
