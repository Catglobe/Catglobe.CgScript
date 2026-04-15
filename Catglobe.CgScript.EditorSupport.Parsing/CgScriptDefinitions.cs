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

/// <summary>A parameter in a method, constructor, or built-in function.</summary>
public sealed record MethodParam(
   string  Name,
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   string? Doc  = null,
   string  Type = "");

/// <summary>One overload of a method, constructor, or built-in function.</summary>
public sealed record MethodOverload(
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   string?        Doc        = null,
   MethodParam[]  Param      = null!,
   string         ReturnType = "",
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   string?        ObsoleteDoc = null,
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   string?        InheritedFrom = null)
{
   /// <summary>The parameter list; never null after construction.</summary>
   public MethodParam[] Param { get; init; } = Param ?? [];
   /// <summary>Returns <c>true</c> when <see cref="ObsoleteDoc"/> is non-null.</summary>
   [System.Text.Json.Serialization.JsonIgnore]
   public bool IsObsolete => ObsoleteDoc != null;
}

/// <summary>A global variable pre-declared by the runtime.</summary>
public sealed record GlobalVariableDefinition(
   string  TypeName,
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   string? Doc         = null,
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   string? ObsoleteDoc = null)
{
   /// <summary>Returns <c>true</c> when <see cref="ObsoleteDoc"/> is non-null.</summary>
   [System.Text.Json.Serialization.JsonIgnore]
   public bool IsObsolete => ObsoleteDoc != null;
}

/// <summary>A property on a CgScript object type.</summary>
public sealed record PropertyDefinition(
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   string? Doc,
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   bool    HasSetter   = false,
   string  ReturnType  = "",
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   string? ObsoleteDoc = null,
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   string? InheritedFrom = null)
{
   /// <summary>Returns <c>true</c> when <see cref="ObsoleteDoc"/> is non-null.</summary>
   [System.Text.Json.Serialization.JsonIgnore]
   public bool IsObsolete => ObsoleteDoc != null;
}

/// <summary>A CgScript object type with constructors, methods, static methods, and properties.</summary>
public sealed record ObjectDefinition(
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   string? Doc,
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   MethodOverload[]?                       Constructors  = null,
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   Dictionary<string, MethodOverload[]>?   Methods       = null,
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   Dictionary<string, MethodOverload[]>?   StaticMethods = null,
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   Dictionary<string, PropertyDefinition>? Properties    = null,
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   string? Parent = null);

/// <summary>One value within a CgScript enum.</summary>
public sealed record EnumValueDefinition(
   string  Name,
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   string? Doc,
   int     Value,
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   string? ObsoleteDoc = null)
{
   /// <summary>Returns <c>true</c> when <see cref="ObsoleteDoc"/> is non-null.</summary>
   [System.Text.Json.Serialization.JsonIgnore]
   public bool IsObsolete => ObsoleteDoc != null;
}

/// <summary>
/// A CgScript enum type (e.g. <c>[Cg("COLOR",…)]</c>).
/// <see cref="Prefix"/> is the constant-name prefix (e.g. <c>"COLOR_"</c>).
/// </summary>
public sealed record EnumDefinition(
   string                Prefix,
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   string?               Doc,
   EnumValueDefinition[] Values);

/// <summary>
/// Describes one parameter of a <c>where</c>-expression aggregate function.
/// </summary>
/// <param name="Name">Parameter name shown in hover / signature help.</param>
/// <param name="Doc">Optional documentation string.</param>
/// <param name="IsColumnName">
/// <c>true</c> when the argument is a DCS column name identifier (not a CgScript expression).
/// Column-name parameters are not validated against the script's variable scope.
/// </param>
/// <param name="IsVarArgs">
/// <c>true</c> when this is the last parameter and may be repeated one or more times
/// (e.g. <c>selectColumn(col1, col2, …)</c>).
/// </param>
public sealed record WhereExpressionParam(
   string  Name,
   string? Doc         = null,
   bool    IsColumnName = true,
   bool    IsVarArgs    = false);

/// <summary>
/// Describes a <c>where</c>-expression aggregate function (e.g. <c>average</c>, <c>selectColumn</c>).
/// These functions appear on the left-hand side of a <c>where</c> expression and operate over
/// DCS rows that satisfy the right-hand side condition.
/// </summary>
/// <param name="Doc">Documentation string shown on hover / completion.</param>
/// <param name="Params">Ordered parameter descriptors.</param>
/// <param name="ReturnType">Canonical CgScript return type name; empty string means dynamic / context-dependent.</param>
/// <param name="ObsoleteMessage">When non-null, the function is obsolete and this message is shown as a CGS026 warning.</param>
public sealed record WhereExpressionDefinition(
   string?                  Doc,
   WhereExpressionParam[]   Params,
   string                   ReturnType     = "",
   string?                  ObsoleteMessage = null);

/// <summary>
/// The raw payload record that maps 1:1 to the JSON produced by the definitions API endpoint.
/// </summary>
/// <param name="Functions">All globally callable functions keyed by name.</param>
/// <param name="Objects">Known built-in object types keyed by type name.</param>
/// <param name="GlobalVariables">Global variables pre-declared by the runtime.</param>
/// <param name="Enums">Enum types with their prefixed constant values.</param>
/// <param name="WhereExpressions">Where-expression aggregate functions; when present, overrides the hardcoded fallback.</param>
public sealed record CgScriptDefinitionsPayload(
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   Dictionary<string, MethodOverload[]>?         Functions,
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   Dictionary<string, ObjectDefinition>?         Objects,
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   Dictionary<string, GlobalVariableDefinition>? GlobalVariables,
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   Dictionary<string, EnumDefinition>?           Enums,
   [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
   Dictionary<string, WhereExpressionDefinition>? WhereExpressions = null);

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
   /// </summary>
   public string? LoadError { get; protected init; }

   /// <summary>All globally callable functions (standalone + globally-accessible static methods), keyed by name.</summary>
   public IReadOnlyDictionary<string, MethodOverload[]>      Functions       { get; protected init; }
   /// <summary>Names of functions that are globally callable but defined as object static methods.</summary>
   public IReadOnlyList<string>                              GlobalFunctions  { get; protected init; }
   /// <summary>Known built-in object types keyed by type name.</summary>
   public IReadOnlyDictionary<string, ObjectDefinition>      Objects          { get; protected init; }
   /// <summary>Known constant names (enum values), sorted case-insensitively for binary-search prefix lookup.</summary>
   public IReadOnlyList<string>                              Constants        { get; protected init; }
   /// <summary>Global variables pre-declared by the runtime.</summary>
   public IReadOnlyDictionary<string, GlobalVariableDefinition> GlobalVariables { get; protected init; }
   /// <summary>Enum types with their prefixed constant values.</summary>
   public IReadOnlyDictionary<string, EnumDefinition>        Enums            { get; protected init; }

   // ── Derived / computed ─────────────────────────────────────────────────────
   /// <summary>Pre-built member info per object type, ready for the semantic analyzer.</summary>
   public IReadOnlyDictionary<string, ObjectMemberInfo> ObjectMemberInfos { get; init; }
   /// <summary>Pre-built function signature info, ready for the semantic analyzer.</summary>
   public IReadOnlyDictionary<string, FunctionInfo>     FunctionInfos     { get; init; }
   /// <summary>Names of fully-obsolete functions mapped to their optional deprecation message.</summary>
   public IReadOnlyDictionary<string, string?>          ObsoleteFunctions { get; init; }
   /// <summary>Obsolete constant names (enum values) mapped to their optional deprecation message.</summary>
   public IReadOnlyDictionary<string, string?>          ObsoleteConstants { get; init; }
   /// <summary>All constant names as a case-insensitive set for O(1) membership tests.</summary>
   public IReadOnlySet<string>                          ConstantsSet      { get; init; }
   /// <summary>Enum value name → (parent enum, value definition) for O(1) constant detail lookup.</summary>
   public IReadOnlyDictionary<string, (EnumDefinition Enum, EnumValueDefinition Value)> EnumByConstant { get; init; }
   /// <summary>Function names sorted case-insensitively for binary-search prefix lookup.</summary>
   public IReadOnlyList<string>                         FunctionKeys      { get; init; }
   /// <summary>Object type names sorted case-insensitively for binary-search prefix lookup.</summary>
   public IReadOnlyList<string>                         ObjectKeys        { get; init; }
   /// <summary>Global variable names sorted case-insensitively for binary-search prefix lookup.</summary>
   public IReadOnlyList<string>                         GlobalVariableKeys { get; init; }

   // ── Where-expression functions ────────────────────────────────────────────
   /// <summary>All <c>where</c>-expression aggregate functions, keyed by name.</summary>
   public IReadOnlyDictionary<string, WhereExpressionDefinition> WhereExpressions { get; private set; }

   /// <summary>Where-expression function names sorted case-insensitively for binary-search prefix lookup.</summary>
   public IReadOnlyList<string> WhereExpressionKeys { get; private set; }

   /// <summary>Returns where-expression entries whose names start with <paramref name="prefix"/>.</summary>
   public IEnumerable<KeyValuePair<string, WhereExpressionDefinition>> WhereExpressionsStartingWith(string prefix = "")
   {
      foreach (var k in ScanKeys(WhereExpressionKeys, prefix))
         yield return new KeyValuePair<string, WhereExpressionDefinition>(k, WhereExpressions[k]);
   }

   // ── Binary-search helpers ─────────────────────────────────────────────────
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

   /// <summary>Returns constant names starting with <paramref name="prefix"/>.</summary>
   public IEnumerable<string> ConstantsStartingWith(string prefix = "")
      => ScanKeys(Constants, prefix);

   /// <summary>Returns function entries starting with <paramref name="prefix"/>.</summary>
   public IEnumerable<KeyValuePair<string, MethodOverload[]>> FunctionsStartingWith(string prefix = "")
   {
      foreach (var k in ScanKeys(FunctionKeys, prefix))
         yield return new KeyValuePair<string, MethodOverload[]>(k, Functions[k]);
   }

   /// <summary>Returns object entries starting with <paramref name="prefix"/>.</summary>
   public IEnumerable<KeyValuePair<string, ObjectDefinition>> ObjectsStartingWith(string prefix = "")
   {
      foreach (var k in ScanKeys(ObjectKeys, prefix))
         yield return new KeyValuePair<string, ObjectDefinition>(k, Objects[k]);
   }

   /// <summary>Returns global variable entries starting with <paramref name="prefix"/>.</summary>
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
      var objects  = payload?.Objects         ?? new Dictionary<string, ObjectDefinition>();
      var functions = new Dictionary<string, MethodOverload[]>(
         payload?.Functions ?? new Dictionary<string, MethodOverload[]>(),
         StringComparer.Ordinal);
      Functions        = functions;
      GlobalFunctions  = DeriveGlobalFunctions(functions, objects);
      Objects          = objects;
      Constants        = DeriveConstants(payload?.Enums);
      GlobalVariables  = payload?.GlobalVariables ?? new Dictionary<string, GlobalVariableDefinition>();
      Enums            = payload?.Enums           ?? new Dictionary<string, EnumDefinition>();
      FunctionKeys       = Sort(Functions.Keys);
      ObjectKeys         = Sort(Objects.Keys);
      GlobalVariableKeys = Sort(GlobalVariables.Keys);
      (ObjectMemberInfos, FunctionInfos, ObsoleteFunctions, ObsoleteConstants, ConstantsSet, EnumByConstant) = BuildDerived(Objects, Functions, Enums);
      WhereExpressions = payload?.WhereExpressions is { Count: > 0 } we ? we : new Dictionary<string, WhereExpressionDefinition>(StringComparer.Ordinal);
      WhereExpressionKeys = Sort(WhereExpressions.Keys);
   }

   private static string[] Sort(IEnumerable<string>? list)
   {
      if (list is null) return [];
      var arr = list.ToArray();
      if (arr.Length == 0) return arr;
      Array.Sort(arr, StringComparer.OrdinalIgnoreCase);
      return arr;
   }

   /// <summary>Derives the list of function names that are also exposed as static methods on any object type.</summary>
   private static IReadOnlyList<string> DeriveGlobalFunctions(
      IReadOnlyDictionary<string, MethodOverload[]>    functions,
      IReadOnlyDictionary<string, ObjectDefinition>    objects)
   {
      var staticMethodNames = new HashSet<string>(StringComparer.Ordinal);
      foreach (var obj in objects.Values)
         if (obj.StaticMethods != null)
            foreach (var name in obj.StaticMethods.Keys)
               staticMethodNames.Add(name);
      return functions.Keys.Where(staticMethodNames.Contains).ToArray();
   }

   /// <summary>Derives the sorted constant list from enum values.</summary>
   private static string[] DeriveConstants(IReadOnlyDictionary<string, EnumDefinition>? enums)
   {
      if (enums is null) return [];
      var names = enums.Values.SelectMany(e => e.Values.Select(v => v.Name)).ToArray();
      Array.Sort(names, StringComparer.OrdinalIgnoreCase);
      return names;
   }

   /// <summary>Constructor for subclasses that supply their own definitions.</summary>
   protected CgScriptDefinitions(
      Dictionary<string, MethodOverload[]>                  functions,
      Dictionary<string, ObjectDefinition>                  objects,
      IReadOnlyCollection<string>                           constants,
      IReadOnlyDictionary<string, GlobalVariableDefinition> globalVariables,
      Dictionary<string, EnumDefinition>                    enums,
      Dictionary<string, WhereExpressionDefinition>?        whereExpressions = null)
   {
      Functions        = functions;
      GlobalFunctions  = DeriveGlobalFunctions(functions, objects);
      Objects          = objects;
      Constants        = Sort(constants);
      GlobalVariables  = globalVariables;
      Enums            = enums;
      FunctionKeys       = Sort(Functions.Keys);
      ObjectKeys         = Sort(Objects.Keys);
      GlobalVariableKeys = Sort(GlobalVariables.Keys);
      (ObjectMemberInfos, FunctionInfos, ObsoleteFunctions, ObsoleteConstants, ConstantsSet, EnumByConstant) = BuildDerived(Objects, Functions, Enums);
      WhereExpressions = whereExpressions ?? new Dictionary<string, WhereExpressionDefinition>(StringComparer.Ordinal);
      WhereExpressionKeys = Sort(WhereExpressions.Keys);
   }

   /// <summary>Loads definitions from the embedded JSON resource, setting <see cref="LoadError"/> to <paramref name="loadError"/>.</summary>
#if NET5_0_OR_GREATER
   [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("JSON deserialization of CgScriptDefinitionsPayload.")]
#endif
   public CgScriptDefinitions(string loadError) : this() => LoadError = loadError;

   private static (
      IReadOnlyDictionary<string, ObjectMemberInfo>,
      IReadOnlyDictionary<string, FunctionInfo>,
      IReadOnlyDictionary<string, string?>,
      IReadOnlyDictionary<string, string?>,
      IReadOnlySet<string>,
      IReadOnlyDictionary<string, (EnumDefinition Enum, EnumValueDefinition Value)>)
      BuildDerived(
         IReadOnlyDictionary<string, ObjectDefinition>    objects,
         IReadOnlyDictionary<string, MethodOverload[]>    functions,
         IReadOnlyDictionary<string, EnumDefinition>      enums)
   {
      var constantNames = enums.Values.SelectMany(e => e.Values.Select(v => v.Name));
      var hs = new HashSet<string>(constantNames, StringComparer.Ordinal);
#if NET5_0_OR_GREATER
      IReadOnlySet<string> constantsSet = hs;
#else
      IReadOnlySet<string> constantsSet = new ReadOnlySet<string>(hs);
#endif
      var enumByConstant = enums.Values
         .SelectMany(e => e.Values.Select(v => (Key: v.Name, Enum: e, Value: v)))
         .GroupBy(x => x.Key, StringComparer.Ordinal)
         .ToDictionary(g => g.Key, g => (g.First().Enum, g.First().Value), StringComparer.Ordinal);
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
            foreach (var propKvp in def.Properties)
            {
               var propName = propKvp.Key;
               var p        = propKvp.Value;
               properties[propName] = p.HasSetter;
               if (!string.IsNullOrEmpty(p.ReturnType))
                  propertyRetTypes[propName] = p.ReturnType;
               if (p.IsObsolete)
                  obsoleteProps[propName] = p.ObsoleteDoc;
            }
         var methods                = new List<string>();
         var obsoleteMethods        = new Dictionary<string, string?>(StringComparer.Ordinal);
         var methodOverloadsDict    = new Dictionary<string, List<IReadOnlyList<string>>>(StringComparer.Ordinal);
         if (def.Methods != null)
            foreach (var methodKvp in def.Methods)
            {
               var methodName = methodKvp.Key;
               var overloads  = methodKvp.Value;
               methods.Add(methodName);
               var overloadList = new List<IReadOnlyList<string>>();
               bool anyObsolete = false;
               string? obsoleteDoc = null;
               foreach (var m in overloads)
               {
                  if (m.IsObsolete) { anyObsolete = true; obsoleteDoc ??= m.ObsoleteDoc; }
                  var paramTypes = new List<string>(m.Param?.Length ?? 0);
                  if (m.Param != null)
                     foreach (var p in m.Param)
                        paramTypes.Add(p.Type ?? "");
                  overloadList.Add(paramTypes);
               }
               if (anyObsolete) obsoleteMethods[methodName] = obsoleteDoc;
               methodOverloadsDict[methodName] = overloadList;
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
      IReadOnlyDictionary<string, MethodOverload[]> functions)
   {
      var result = new Dictionary<string, FunctionInfo>(StringComparer.Ordinal);
      foreach (var kvp in functions)
      {
         var overloads    = new List<IReadOnlyList<string>>(kvp.Value.Length);
         bool allObsolete = kvp.Value.Length > 0;
         string? obsoleteDoc = null;
         foreach (var variant in kvp.Value)
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
      IReadOnlyDictionary<string, MethodOverload[]> functions)
   {
      var result = new Dictionary<string, string?>(StringComparer.Ordinal);
      foreach (var kvp in functions)
         if (kvp.Value.Length > 0 && kvp.Value.All(v => v.IsObsolete))
            result[kvp.Key] = kvp.Value.Select(v => v.ObsoleteDoc).FirstOrDefault(d => d != null);
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
   /// <paramref name="extras"/> merged into <see cref="GlobalVariables"/>.
   /// </summary>
   public CgScriptDefinitions WithExtraGlobalVariables(IReadOnlyDictionary<string, GlobalVariableDefinition> extras)
   {
      if (extras is not { Count: > 0 }) return this;
      var merged = GlobalVariables.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
      foreach (var kv in extras) merged[kv.Key] = kv.Value;
      return new CgScriptDefinitions(
         Functions   is Dictionary<string, MethodOverload[]>    fd ? fd : Functions.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
         Objects     is Dictionary<string, ObjectDefinition>    od ? od : Objects.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
         Constants,
         merged,
         Enums       is Dictionary<string, EnumDefinition>      ed ? ed : Enums.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase));
   }

   /// <summary>
   /// Returns a new <see cref="CgScriptDefinitions"/> with extra constructor overloads only valid
   /// in preprocessor/source-generator context (e.g., <c>new WorkflowScript("filename.cgs")</c>).
   /// </summary>
   public CgScriptDefinitions WithPreprocessorExtensions()
   {
      if (!ObjectMemberInfos.TryGetValue("WorkflowScript", out var wsMemberInfo)) return this;
      var modified = ObjectMemberInfos.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
      modified["WorkflowScript"] = wsMemberInfo.WithExtraConstructorOverload(new[] { "string" });
      return new CgScriptDefinitions(
         Functions   is Dictionary<string, MethodOverload[]>    fd ? fd : Functions.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
         Objects     is Dictionary<string, ObjectDefinition>    od ? od : Objects.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
         Constants,
         GlobalVariables,
         Enums       is Dictionary<string, EnumDefinition>      ed ? ed : Enums.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase))
      { ObjectMemberInfos = modified };
   }

   /// <summary>Creates a <see cref="CgScriptDefinitions"/> by deserializing JSON from <paramref name="stream"/>.</summary>
#if NET5_0_OR_GREATER
   [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("JSON deserialization of CgScriptDefinitionsPayload.")]
#endif
   public static CgScriptDefinitions FromJsonStream(System.IO.Stream stream)
   {
      var payload = System.Text.Json.JsonSerializer.Deserialize<CgScriptDefinitionsPayload>(stream,
         new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
      return FromPayload(payload);
   }

#if NET5_0_OR_GREATER
   [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("JSON deserialization of CgScriptDefinitionsPayload.")]
#endif
   internal static CgScriptDefinitions FromPayload(CgScriptDefinitionsPayload? payload)
   {
      var functions = new Dictionary<string, MethodOverload[]>(
         payload?.Functions ?? new Dictionary<string, MethodOverload[]>(),
         StringComparer.Ordinal);
      var objects   = payload?.Objects         ?? new Dictionary<string, ObjectDefinition>();
      var enums     = payload?.Enums           ?? new Dictionary<string, EnumDefinition>();
      var whereExpressions = payload?.WhereExpressions is { Count: > 0 } we
         ? we
         : null;
      return new CgScriptDefinitions(
         functions, objects, DeriveConstants(enums),
         payload?.GlobalVariables ?? new Dictionary<string, GlobalVariableDefinition>(),
         enums,
         whereExpressions);
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
      return System.Text.Json.JsonSerializer.Deserialize<CgScriptDefinitionsPayload>(stream,
         new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
   }

}
