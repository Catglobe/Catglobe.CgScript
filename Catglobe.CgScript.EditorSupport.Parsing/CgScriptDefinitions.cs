using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Catglobe.CgScript.EditorSupport.Parsing;

#if !NET5_0_OR_GREATER
/// <summary>
/// Polyfill for <c>System.Collections.Generic.IReadOnlySet&lt;T&gt;</c>, available from .NET 5.
/// </summary>
public interface IReadOnlySet<T> : IReadOnlyCollection<T>
{
   /// <summary>Determines whether the set contains a specific value.</summary>
   bool Contains(T item);
}

file sealed class ReadOnlySet<T>(HashSet<T> inner) : IReadOnlySet<T>
{
   public int    Count    => inner.Count;
   public bool   Contains(T item) => inner.Contains(item);
   public System.Collections.Generic.IEnumerator<T> GetEnumerator() => inner.GetEnumerator();
   System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
      ((System.Collections.IEnumerable)inner).GetEnumerator();
}
#endif

// ── JSON models ───────────────────────────────────────────────────────────────

/// <summary>A parameter in a function variant.</summary>
public sealed record FunctionVariantParam(string Name, string Doc, string Type);

/// <summary>One overload variant of a function.</summary>
public sealed record FunctionVariant(
   string                Doc,
   FunctionVariantParam[] Param,
   string                ReturnType,
   bool                  IsObsolete  = false,
   string?               ObsoleteDoc = null);

/// <summary>A built-in CgScript function with one or more overload variants.</summary>
public sealed record FunctionDefinition(FunctionVariant[] Variants);

/// <summary>A global variable pre-declared by the runtime.</summary>
public sealed record GlobalVariableDefinition(string TypeName, string Doc = "", bool IsObsolete = false, string? ObsoleteDoc = null);

/// <summary>A parameter in a method or constructor.</summary>
public sealed record MethodParam(string Name, string Doc, string Type);

/// <summary>A method or constructor on a CgScript object type.</summary>
public sealed record MethodDefinition(string Name, string Doc, MethodParam[] Param, string ReturnType, bool IsObsolete = false, string? ObsoleteDoc = null);

/// <summary>A property on a CgScript object type.</summary>
public sealed record PropertyDefinition(
   string  Name,
   string  Doc,
   bool    HasGetter,
   bool    HasSetter,
   string  ReturnType,
   bool    IsObsolete  = false,
   string? ObsoleteDoc = null);

/// <summary>A CgScript object type with constructors, methods, static methods, and properties.</summary>
public sealed record ObjectDefinition(
   string               Doc,
   MethodDefinition[]   Constructors,
   MethodDefinition[]   Methods,
   MethodDefinition[]   StaticMethods,
   PropertyDefinition[] Properties);

/// <summary>One value within a CgScript enum (e.g. <c>COLOR_RED = 1</c>).</summary>
public sealed record EnumValueDefinition(string Name, string Doc, int Value, bool IsObsolete, string? ObsoleteDoc = null);

/// <summary>
/// A CgScript enum type (e.g. <c>[Cg("COLOR",…)]</c>).
/// <see cref="Prefix"/> is the constant-name prefix (e.g. <c>"COLOR_"</c>).
/// </summary>
public sealed record EnumDefinition(string Prefix, string Doc, EnumValueDefinition[] Values);

/// <summary>
/// The raw payload record that maps 1:1 to the JSON produced by the definitions API endpoint.
/// Used for serialization/deserialization of the CgScript definitions JSON.
/// </summary>
/// <param name="Functions">Known built-in functions keyed by name.</param>
/// <param name="Objects">Known built-in object types keyed by type name.</param>
/// <param name="Constants">Known constant names (enum values).</param>
/// <param name="GlobalVariables">Global variables pre-declared by the runtime.</param>
/// <param name="Enums">Enum types with their prefixed constant values.</param>
public sealed record CgScriptDefinitionsPayload(
   Dictionary<string, FunctionDefinition>?                Functions,
   Dictionary<string, ObjectDefinition>?                  Objects,
   IReadOnlyList<string>?                                 Constants,
   Dictionary<string, GlobalVariableDefinition>?          GlobalVariables,
   Dictionary<string, EnumDefinition>?                    Enums);

// ── Loader ────────────────────────────────────────────────────────────────────

/// <summary>
/// Loads and exposes CgScript definitions from the embedded <c>CgScriptDefinitions.json</c>.
/// Subclass (in the Lsp layer) to add HTTP fetching or live runtime data.
/// </summary>
public class CgScriptDefinitions
{
   private static readonly Assembly _asm = typeof(CgScriptDefinitions).Assembly;

   /// <summary>TraceSource for definition loading events.</summary>
   public static readonly System.Diagnostics.TraceSource TraceSource =
      new("CgScript.Definitions", System.Diagnostics.SourceLevels.Information);

   /// <summary>
   /// Set when definitions were fetched from a URL but the response could not be parsed.
   /// The LSP surfaces this as a persistent CGS001 error diagnostic on every open document.
   /// </summary>
   public string? LoadError { get; protected init; }

   /// <summary>Known built-in functions keyed by name.</summary>
   public IReadOnlyDictionary<string, FunctionDefinition>   Functions       { get; protected init; }
   /// <summary>Known built-in object types keyed by type name.</summary>
   public IReadOnlyDictionary<string, ObjectDefinition>     Objects         { get; protected init; }
   /// <summary>Known constant names (enum values), sorted case-insensitively for binary-search prefix lookup. Rich metadata is in <see cref="Enums"/>.</summary>
   public IReadOnlyList<string>                              Constants       { get; protected init; }
   /// <summary>Global variables pre-declared by the runtime.</summary>
   public IReadOnlyDictionary<string, GlobalVariableDefinition> GlobalVariables { get; protected init; }
   /// <summary>Enum types with their prefixed constant values.</summary>
   public IReadOnlyDictionary<string, EnumDefinition>        Enums           { get; protected init; }

   // ── Derived / computed (built once, consistent with raw data) ─────────────
   /// <summary>Pre-built member info per object type, ready for the semantic analyzer.</summary>
   public IReadOnlyDictionary<string, ObjectMemberInfo> ObjectMemberInfos { get; init; }
   /// <summary>Pre-built function signature info, ready for the semantic analyzer.</summary>
   public IReadOnlyDictionary<string, FunctionInfo>     FunctionInfos     { get; init; }
   /// <summary>Names of fully-obsolete functions mapped to their optional deprecation message.</summary>
   public IReadOnlyDictionary<string, string?>          ObsoleteFunctions { get; init; }
   /// <summary>Obsolete constant names (enum values) mapped to their optional deprecation message.</summary>
   public IReadOnlyDictionary<string, string?>          ObsoleteConstants { get; init; }
   /// <summary>All constant names as a case-insensitive set for O(1) membership tests.</summary>
   public IReadOnlySet<string>                               ConstantsSet      { get; init; }
   /// <summary>Enum value name → (parent enum, value definition) for O(1) constant detail lookup.</summary>
   public IReadOnlyDictionary<string, (EnumDefinition Enum, EnumValueDefinition Value)> EnumByConstant { get; init; }
   /// <summary>Function names sorted case-insensitively for binary-search prefix lookup.</summary>
   public IReadOnlyList<string>                          FunctionKeys      { get; init; }
   /// <summary>Object type names sorted case-insensitively for binary-search prefix lookup.</summary>
   public IReadOnlyList<string>                          ObjectKeys        { get; init; }
   /// <summary>Global variable names sorted case-insensitively for binary-search prefix lookup.</summary>
   public IReadOnlyList<string>                          GlobalVariableKeys { get; init; }

   // ── Shared binary-search helper ───────────────────────────────────────────
   /// <summary>
   /// Returns the index of the first element in <paramref name="sorted"/> that is ≥
   /// <paramref name="prefix"/> (case-insensitive).  Used as the start of a prefix scan.
   /// </summary>
   private static int FindPrefixStart(IReadOnlyList<string> sorted, string prefix)
   {
      int lo = 0, hi = sorted.Count - 1;
      while (lo <= hi)
      {
         var mid = (lo + hi) >> 1;
         if (string.Compare(sorted[mid], prefix, StringComparison.OrdinalIgnoreCase) < 0) lo = mid + 1;
         else hi = mid - 1;
      }
      return lo;
   }

   /// <summary>
   /// Returns all constants whose name starts with <paramref name="prefix"/> (case-insensitive),
   /// using binary search on the sorted <see cref="Constants"/> list — O(log n + k).
   /// Pass an empty string (or call with no argument) to enumerate all constants.
   /// </summary>
   public IEnumerable<string> ConstantsStartingWith(string prefix = "")
      => ScanKeys(Constants, prefix);

   /// <summary>
   /// Returns all functions whose name starts with <paramref name="prefix"/> (case-insensitive),
   /// using binary search on the sorted <see cref="FunctionKeys"/> list — O(log n + k).
   /// </summary>
   public IEnumerable<KeyValuePair<string, FunctionDefinition>> FunctionsStartingWith(string prefix = "")
   {
      foreach (var k in ScanKeys(FunctionKeys, prefix))
         yield return new KeyValuePair<string, FunctionDefinition>(k, Functions[k]);
   }

   /// <summary>
   /// Returns all object types whose name starts with <paramref name="prefix"/> (case-insensitive),
   /// using binary search on the sorted <see cref="ObjectKeys"/> list — O(log n + k).
   /// </summary>
   public IEnumerable<KeyValuePair<string, ObjectDefinition>> ObjectsStartingWith(string prefix = "")
   {
      foreach (var k in ScanKeys(ObjectKeys, prefix))
         yield return new KeyValuePair<string, ObjectDefinition>(k, Objects[k]);
   }

   /// <summary>
   /// Returns all global variables whose name starts with <paramref name="prefix"/> (case-insensitive),
   /// using binary search on the sorted <see cref="GlobalVariableKeys"/> list — O(log n + k).
   /// </summary>
   public IEnumerable<KeyValuePair<string, GlobalVariableDefinition>> GlobalVariablesStartingWith(string prefix = "")
   {
      foreach (var k in ScanKeys(GlobalVariableKeys, prefix))
         yield return new KeyValuePair<string, GlobalVariableDefinition>(k, GlobalVariables[k]);
   }

   private static IEnumerable<string> ScanKeys(IReadOnlyList<string> sorted, string prefix)
   {
      var start = string.IsNullOrEmpty(prefix) ? 0 : FindPrefixStart(sorted, prefix);
      for (var i = start; i < sorted.Count && (prefix.Length == 0 || sorted[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)); i++)
         yield return sorted[i];
   }

   /// <summary>Loads definitions from the embedded JSON resource.</summary>
#if NET5_0_OR_GREATER
   [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("JSON deserialization of CgScriptDefinitionsPayload.")]
#endif
   public CgScriptDefinitions()
   {
      var payload  = Load();
      Functions       = payload?.Functions       ?? new Dictionary<string, FunctionDefinition>();
      Objects         = payload?.Objects         ?? new Dictionary<string, ObjectDefinition>();
      Constants       = Sort(payload?.Constants);
      GlobalVariables = payload?.GlobalVariables ?? new Dictionary<string, GlobalVariableDefinition>();
      Enums           = payload?.Enums           ?? new Dictionary<string, EnumDefinition>();
      FunctionKeys       = Sort(Functions.Keys);
      ObjectKeys         = Sort(Objects.Keys);
      GlobalVariableKeys = Sort(GlobalVariables.Keys);
      (ObjectMemberInfos, FunctionInfos, ObsoleteFunctions, ObsoleteConstants, ConstantsSet, EnumByConstant) = BuildDerived(Objects, Functions, Enums, Constants);
   }

   private static string[] Sort(IEnumerable<string>? list)
   {
      if (list is null) return [];
      var arr = list.ToArray();
      if (arr.Length == 0) return arr;
      Array.Sort(arr, StringComparer.OrdinalIgnoreCase);
      return arr;
   }

   /// <summary>Creates a loader with no definitions and a load error message (e.g. after a failed HTTP parse).</summary>
#if NET5_0_OR_GREATER
   [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("JSON deserialization of CgScriptDefinitionsPayload.")]
#endif
   public CgScriptDefinitions(string loadError) : this() => LoadError = loadError;

   /// <summary>Constructor for subclasses that supply their own definitions.</summary>
   protected CgScriptDefinitions(
      Dictionary<string, FunctionDefinition>                functions,
      Dictionary<string, ObjectDefinition>                  objects,
      IReadOnlyCollection<string>                           constants,
      IReadOnlyDictionary<string, GlobalVariableDefinition> globalVariables,
      Dictionary<string, EnumDefinition>                    enums)
   {
      Functions       = functions;
      Objects         = objects;
      Constants       = Sort(constants);
      GlobalVariables = globalVariables;
      Enums           = enums;
      FunctionKeys       = Sort(Functions.Keys);
      ObjectKeys         = Sort(Objects.Keys);
      GlobalVariableKeys = Sort(GlobalVariables.Keys);
      (ObjectMemberInfos, FunctionInfos, ObsoleteFunctions, ObsoleteConstants, ConstantsSet, EnumByConstant) = BuildDerived(Objects, Functions, Enums, Constants);
   }

   private static (
      IReadOnlyDictionary<string, ObjectMemberInfo>,
      IReadOnlyDictionary<string, FunctionInfo>,
      IReadOnlyDictionary<string, string?>,
      IReadOnlyDictionary<string, string?>,
      IReadOnlySet<string>,
      IReadOnlyDictionary<string, (EnumDefinition Enum, EnumValueDefinition Value)>)
      BuildDerived(
         IReadOnlyDictionary<string, ObjectDefinition>     objects,
         IReadOnlyDictionary<string, FunctionDefinition>   functions,
         IReadOnlyDictionary<string, EnumDefinition>       enums,
         IReadOnlyList<string>                             constants)
   {
      var hs = new HashSet<string>(constants, StringComparer.OrdinalIgnoreCase);
#if NET5_0_OR_GREATER
      IReadOnlySet<string> constantsSet = hs;
#else
      IReadOnlySet<string> constantsSet = new ReadOnlySet<string>(hs);
#endif
      var enumByConstant = enums.Values
         .SelectMany(e => e.Values.Select(v => (Key: v.Name, Enum: e, Value: v)))
         .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
         .ToDictionary(g => g.Key, g => (g.First().Enum, g.First().Value), StringComparer.OrdinalIgnoreCase);
      return (BuildObjectMemberInfos(objects), BuildFunctionInfos(functions),
              BuildObsoleteFunctions(functions), BuildObsoleteConstants(enums),
              constantsSet, enumByConstant);
   }

   private static IReadOnlyDictionary<string, ObjectMemberInfo> BuildObjectMemberInfos(
      IReadOnlyDictionary<string, ObjectDefinition> objects)
   {
      var result = new Dictionary<string, ObjectMemberInfo>(StringComparer.Ordinal);
      foreach (var kvp in objects)
      {
         var def              = kvp.Value;
         var properties       = new Dictionary<string, bool>(StringComparer.Ordinal);
         var propertyRetTypes = new Dictionary<string, string>(StringComparer.Ordinal);
         var obsoleteProps    = new Dictionary<string, string?>(StringComparer.Ordinal);
         if (def.Properties != null)
            foreach (var p in def.Properties)
            {
               properties[p.Name] = p.HasSetter;
               if (!string.IsNullOrEmpty(p.ReturnType))
                  propertyRetTypes[p.Name] = p.ReturnType;
               if (p.IsObsolete)
                  obsoleteProps[p.Name] = p.ObsoleteDoc;
            }
         var methods                = new List<string>();
         var obsoleteMethods        = new Dictionary<string, string?>(StringComparer.Ordinal);
         var methodOverloadsDict    = new Dictionary<string, List<IReadOnlyList<string>>>(StringComparer.Ordinal);
         if (def.Methods != null)
            foreach (var m in def.Methods)
               if (!string.IsNullOrEmpty(m.Name))
               {
                  methods.Add(m.Name);
                  if (m.IsObsolete)
                     obsoleteMethods[m.Name] = m.ObsoleteDoc;
                  if (!methodOverloadsDict.TryGetValue(m.Name, out var overloadList))
                  {
                     overloadList = new List<IReadOnlyList<string>>();
                     methodOverloadsDict[m.Name] = overloadList;
                  }
                  var paramTypes = new List<string>(m.Param?.Length ?? 0);
                  if (m.Param != null)
                     foreach (var p in m.Param)
                        paramTypes.Add(p.Type ?? "");
                  overloadList.Add(paramTypes);
               }

         List<IReadOnlyList<string>>? constructorOverloads = null;
         if (def.Constructors != null && def.Constructors.Length > 0)
         {
            constructorOverloads = new List<IReadOnlyList<string>>(def.Constructors.Length);
            foreach (var c in def.Constructors)
            {
               var paramTypes = new List<string>(c.Param?.Length ?? 0);
               if (c.Param != null)
                  foreach (var p in c.Param)
                     paramTypes.Add(p.Type ?? "");
               constructorOverloads.Add(paramTypes);
            }
         }

         IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<string>>>? methodOverloads = null;
         if (methodOverloadsDict.Count > 0)
            methodOverloads = methodOverloadsDict.ToDictionary(
               kv => kv.Key,
               kv => (IReadOnlyList<IReadOnlyList<string>>)kv.Value,
               StringComparer.Ordinal);

         result[kvp.Key] = new ObjectMemberInfo(
            properties, methods, propertyRetTypes,
            constructorOverloads: constructorOverloads,
            methodOverloads:      methodOverloads,
            obsoletePropertyNames: obsoleteProps.Count > 0 ? obsoleteProps : null,
            obsoleteMethodNames:   obsoleteMethods.Count > 0 ? obsoleteMethods : null);
      }
      return result;
   }

   private static IReadOnlyDictionary<string, FunctionInfo> BuildFunctionInfos(
      IReadOnlyDictionary<string, FunctionDefinition> functions)
   {
      var result = new Dictionary<string, FunctionInfo>(StringComparer.Ordinal);
      foreach (var kvp in functions)
      {
         var def = kvp.Value;
         if (def.Variants == null) continue;
         var overloads    = new List<IReadOnlyList<string>>(def.Variants.Length);
         bool allObsolete = def.Variants.Length > 0;
         string? obsoleteDoc = null;
         foreach (var variant in def.Variants)
         {
            var paramTypes = new List<string>(variant.Param?.Length ?? 0);
            if (variant.Param != null)
               foreach (var p in variant.Param)
                  paramTypes.Add(p.Type ?? "");
            overloads.Add(paramTypes);
            if (!variant.IsObsolete)
               allObsolete = false;
            else
               obsoleteDoc ??= variant.ObsoleteDoc;
         }
         result[kvp.Key] = new FunctionInfo(overloads, isObsolete: allObsolete, obsoleteDoc: obsoleteDoc);
      }
      return result;
   }

   private static IReadOnlyDictionary<string, string?> BuildObsoleteFunctions(
      IReadOnlyDictionary<string, FunctionDefinition> functions)
   {
      var result = new Dictionary<string, string?>(StringComparer.Ordinal);
      foreach (var kvp in functions)
      {
         var def = kvp.Value;
         if (def.Variants != null && def.Variants.Length > 0 && def.Variants.All(v => v.IsObsolete))
            result[kvp.Key] = def.Variants.Select(v => v.ObsoleteDoc).FirstOrDefault(d => d != null);
      }
      return result;
   }

   private static IReadOnlyDictionary<string, string?> BuildObsoleteConstants(
      IReadOnlyDictionary<string, EnumDefinition> enums)
   {
      var result = new Dictionary<string, string?>(StringComparer.Ordinal);
      foreach (var enumDef in enums.Values)
         foreach (var v in enumDef.Values)
            if (v.IsObsolete)
               result[v.Name] = v.ObsoleteDoc;
      return result;
   }

   /// <summary>
   /// Returns a new <see cref="CgScriptDefinitions"/> identical to this one but with
   /// <paramref name="extras"/> merged into <see cref="GlobalVariables"/> (extras win on collision).
   /// Returns <c>this</c> unchanged when <paramref name="extras"/> is empty.
   /// </summary>
   public CgScriptDefinitions WithExtraGlobalVariables(IReadOnlyDictionary<string, GlobalVariableDefinition> extras)
   {
      if (extras is not { Count: > 0 }) return this;
      var merged = GlobalVariables.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
      foreach (var kv in extras) merged[kv.Key] = kv.Value;
      return new CgScriptDefinitions(
         Functions is Dictionary<string, FunctionDefinition> fd ? fd : Functions.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
         Objects   is Dictionary<string, ObjectDefinition>   od ? od : Objects.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
         Constants,
         merged,
         Enums     is Dictionary<string, EnumDefinition>     ed ? ed : Enums.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase));
   }

   /// <summary>
   /// Creates a <see cref="CgScriptDefinitions"/> by deserializing JSON from <paramref name="stream"/>.
   /// Used by the Lsp layer to load definitions fetched over HTTP.
   /// </summary>
#if NET5_0_OR_GREATER
   [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("JSON deserialization of CgScriptDefinitionsPayload.")]
#endif
   public static CgScriptDefinitions FromJsonStream(System.IO.Stream stream)
   {
      var payload = JsonSerializer.Deserialize<CgScriptDefinitionsPayload>(stream,
         new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
      return FromPayload(payload);
   }

#if NET5_0_OR_GREATER
   [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("JSON deserialization of CgScriptDefinitionsPayload.")]
#endif
   internal static CgScriptDefinitions FromPayload(CgScriptDefinitionsPayload? payload)
   {
      return new CgScriptDefinitions(
         payload?.Functions       ?? new Dictionary<string, FunctionDefinition>(),
         payload?.Objects         ?? new Dictionary<string, ObjectDefinition>(),
         payload?.Constants       ?? [],
         payload?.GlobalVariables ?? new Dictionary<string, GlobalVariableDefinition>(),
         payload?.Enums           ?? new Dictionary<string, EnumDefinition>());
   }

#if NET5_0_OR_GREATER
   [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("JSON deserialization of CgScriptDefinitionsPayload.")]
#endif
   internal static CgScriptDefinitionsPayload? Load()
   {
      var resourceName = _asm.GetManifestResourceNames()
                             .FirstOrDefault(n => n.EndsWith("CgScriptDefinitions.json", System.StringComparison.Ordinal));
      if (resourceName is null) return null;
      using var stream = _asm.GetManifestResourceStream(resourceName)!;
      return JsonSerializer.Deserialize<CgScriptDefinitionsPayload>(stream,
         new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
   }
}
