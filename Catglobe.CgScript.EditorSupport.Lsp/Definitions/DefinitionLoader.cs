using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Catglobe.CgScript.EditorSupport.Lsp.Definitions;

// ── JSON model for CgScriptFunctionDefinitions.json ───────────────────────────

/// <summary>One variant/overload in a new-style function definition.</summary>
public sealed record FunctionVariantParam(string Name, string Doc, string Type);

public sealed record FunctionVariant(
   string                Doc,
   FunctionVariantParam[] Param,
   string                ReturnType,
   bool                  IsObsolete = false,
   string?               ObsoleteDoc = null);

public sealed record FunctionDefinition(
   FunctionVariant[] Variants);

// ── JSON model for CgScriptObjectDefinitions.json ────────────────────────────

public sealed record MethodParam(string Name, string Doc, string Type);

public sealed record MethodDefinition(string Name, string Doc, MethodParam[] Param, string ReturnType, bool IsObsolete = false, string? ObsoleteDoc = null);

public sealed record PropertyDefinition(
   string Name,
   string Doc,
   bool HasGetter,
   bool HasSetter,
   string ReturnType,
   bool IsObsolete = false,
   string? ObsoleteDoc = null);

public sealed record ObjectDefinition(
   string Doc,
   MethodDefinition[] Constructors,
   MethodDefinition[] Methods,
   MethodDefinition[] StaticMethods,
   PropertyDefinition[] Properties);

// ── JSON model for enums ──────────────────────────────────────────────────────

public sealed record EnumValueDefinition(string Name, string Doc, int Value, bool IsObsolete, string? ObsoleteDoc = null);

/// <summary>
/// A CgScript enum type (e.g. ColorCGO.Constants with [Cg("COLOR",…)]).
/// <see cref="Prefix"/> is the constant-name prefix (e.g. "COLOR_");
/// the values already carry the full prefixed name (e.g. "COLOR_RED").
/// </summary>
public sealed record EnumDefinition(string Prefix, string Doc, EnumValueDefinition[] Values);

// ── Combined payload ──────────────────────────────────────────────────────────

public sealed record CgScriptDefinitionsPayload(
   Dictionary<string, FunctionDefinition>? Functions,
   Dictionary<string, ObjectDefinition>?   Objects,
   IReadOnlyList<string>?                  Constants,
   IReadOnlyDictionary<string, string>?    GlobalVariables,
   Dictionary<string, EnumDefinition>?     Enums);

// ── Loader ────────────────────────────────────────────────────────────────────

/// <summary>
/// Loads and exposes CgScript definitions. Subclass to replace with live runtime data.
/// </summary>
public class DefinitionLoader
{
   /// <summary>TraceSource used for definition loading events. Configure listeners in the host process.</summary>
   public static readonly TraceSource TraceSource = new("CgScript.Definitions", SourceLevels.Information);

   /// <summary>
   /// Set when definitions were fetched from a URL but the response could not be parsed.
   /// The LSP surfaces this as a persistent CGS001 error diagnostic on every open document.
   /// </summary>
   public string? LoadError { get; private init; }
   private static readonly Assembly _asm = typeof(Catglobe.CgScript.EditorSupport.Parsing.KnownNamesLoader).Assembly;

   public IReadOnlyDictionary<string, FunctionDefinition> Functions { get; protected init; }
   public IReadOnlyDictionary<string, ObjectDefinition>   Objects   { get; protected init; }
   /// <summary>Known constant names (e.g. enum value names).</summary>
   public IReadOnlyCollection<string>                     Constants { get; protected init; }
   /// <summary>Global variables pre-declared by the runtime, mapped to their type name (e.g. "Catglobe" → "GlobalNamespace").</summary>
   public IReadOnlyDictionary<string, string>             GlobalVariables { get; protected init; }
   /// <summary>Enum types with their prefixed constant values (e.g. "COLOR_" → COLOR_RED, COLOR_GREEN…).</summary>
   public IReadOnlyDictionary<string, EnumDefinition>     Enums { get; protected init; }

   /// <summary>Loads definitions from the embedded JSON resources.</summary>
   public DefinitionLoader()
   {
      var payload = Load<CgScriptDefinitionsPayload>("CgScriptDefinitions.json");
      Functions       = payload?.Functions ?? new Dictionary<string, FunctionDefinition>();
      Objects         = payload?.Objects ?? new Dictionary<string, ObjectDefinition>();
      Constants       = payload?.Constants ?? [];
      GlobalVariables = payload?.GlobalVariables ?? new Dictionary<string, string>();
      Enums           = payload?.Enums ?? new Dictionary<string, EnumDefinition>();
   }

   /// <summary>
   /// Fetches definitions from <paramref name="siteUrl"/>/api/cgscript/definitions.
   /// Falls back to the bundled definitions if the request fails.
   /// </summary>
   public static async Task<DefinitionLoader> CreateFromUrlAsync(string siteUrl, CancellationToken ct = default)
   {
      var url = $"{siteUrl.TrimEnd('/')}/api/cgscript/definitions";
      TraceSource.TraceInformation("Loading CgScript definitions from {0}", url);
      try
      {
         using var http    = new HttpClient();
         await using var stream = await http.GetStreamAsync(url, ct);
         var payload = await JsonSerializer.DeserializeAsync<CgScriptDefinitionsPayload>(
            stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
         var result = new DefinitionLoader(
            payload?.Functions       ?? new Dictionary<string, FunctionDefinition>(),
            payload?.Objects         ?? new Dictionary<string, ObjectDefinition>(),
            payload?.Constants       ?? [],
            payload?.GlobalVariables ?? new Dictionary<string, string>(),
            payload?.Enums           ?? new Dictionary<string, EnumDefinition>());
         TraceSource.TraceInformation(
            "Loaded definitions from {0}: {1} functions, {2} objects, {3} constants",
            url, result.Functions.Count, result.Objects.Count, result.Constants.Count);
         return result;
      }
      catch (JsonException ex)
      {
         TraceSource.TraceEvent(TraceEventType.Error, 0,
            "Failed to parse definitions from {0} — plugin may be out of date: {1}", url, ex.Message);
         return new DefinitionLoader
         {
            LoadError = $"CgScript plugin is out of date: the definition response from '{url}' could not be parsed " +
                        $"({ex.Message}). Please upgrade the CgScript plugin.",
         };
      }
      catch (Exception ex)
      {
         TraceSource.TraceEvent(TraceEventType.Warning, 0,
            "Failed to fetch definitions from {0} — falling back to bundled definitions: {1}", url, ex.Message);
         return new DefinitionLoader();
      }
   }

   /// <summary>Protected constructor for subclasses that supply their own definitions.</summary>
   protected DefinitionLoader(
      Dictionary<string, FunctionDefinition> functions,
      Dictionary<string, ObjectDefinition>   objects,
      IReadOnlyCollection<string>            constants,
      IReadOnlyDictionary<string, string>    globalVariables,
      Dictionary<string, EnumDefinition>     enums)
   {
      Functions       = functions;
      Objects         = objects;
      Constants       = constants;
      GlobalVariables = globalVariables;
      Enums           = enums;
   }

   private static T? Load<T>(string resourceFileName)
   {
      var resourceName = _asm.GetManifestResourceNames()
                             .FirstOrDefault(n => n.EndsWith(resourceFileName, StringComparison.Ordinal));
      if (resourceName is null) return default;

      using var stream = _asm.GetManifestResourceStream(resourceName)!;
      return JsonSerializer.Deserialize<T>(stream, new JsonSerializerOptions
      {
         PropertyNameCaseInsensitive = true,
      });
   }
}
