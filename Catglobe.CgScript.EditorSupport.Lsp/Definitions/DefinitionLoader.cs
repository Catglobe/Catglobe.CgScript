using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Catglobe.CgScript.EditorSupport.Lsp.Definitions;

// ── JSON model for CgScriptFunctionDefinitions.json ───────────────────────────

public sealed record FunctionParam(
   string Name,
   bool IsOptional,
   string ConstantType,
   string EnumType,
   string ObjectType,
   bool IsAllowedEmpty,
   bool IsInteger,
   bool IsPositive);

/// <summary>One variant/overload in a new-style function definition.</summary>
public sealed record FunctionVariantParam(string Name, string Doc, string Type);

public sealed record FunctionVariant(
   string                Name,
   string?               Doc,
   FunctionVariantParam[]? Param,
   string                ReturnType,
   bool                  IsObsolete = false);

public sealed record FunctionDefinition(
   string           Name,
   // Old-style fields (IsNewStyle == false)
   string?          ReturnType,
   int              NumberOfRequiredArguments,
   FunctionParam[]? Parameters,
   // New-style fields (IsNewStyle == true)
   bool             IsNewStyle,
   FunctionVariant[]? Variants);

// ── JSON model for CgScriptObjectDefinitions.json ────────────────────────────

public sealed record MethodParam(string Name, string Doc, string Type);

public sealed record MethodDefinition(string Name, string? Doc, MethodParam[]? Param, string ReturnType, bool IsObsolete = false);

public sealed record PropertyDefinition(
   string Name,
   string? Doc,
   bool HasGetter,
   bool HasSetter,
   string ReturnType,
   bool IsObsolete = false);

public sealed record ObjectDefinition(
   string Name,
   string? Doc,
   MethodDefinition[]? Constructors,
   MethodDefinition[]? Methods,
   MethodDefinition[]? StaticMethods,
   PropertyDefinition[]? Properties);

// ── JSON model for enums ──────────────────────────────────────────────────────

public sealed record EnumValueDefinition(string Name, string? Doc, int Value, bool IsObsolete);

/// <summary>
/// A CgScript enum type (e.g. ColorCGO.Constants with [Cg("COLOR",…)]).
/// <see cref="Name"/> is the unique display name (e.g. "ColorCGO");
/// <see cref="Prefix"/> is the constant-name prefix (e.g. "COLOR_");
/// the values already carry the full prefixed name (e.g. "COLOR_RED").
/// </summary>
public sealed record EnumDefinition(string Name, string Prefix, string? Doc, EnumValueDefinition[] Values);

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
