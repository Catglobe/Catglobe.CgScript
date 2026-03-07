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
   string                ReturnType);

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

public sealed record MethodDefinition(string Name, string? Doc, MethodParam[]? Param, string ReturnType);

public sealed record PropertyDefinition(
   string Name,
   string? Doc,
   bool HasGetter,
   bool HasSetter,
   string ReturnType);

public sealed record ObjectDefinition(
   string Name,
   string? Doc,
   object[]? Constructors,
   MethodDefinition[]? Methods,
   MethodDefinition[]? StaticMethods,
   PropertyDefinition[]? Properties);

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

   /// <summary>Loads definitions from the embedded JSON resources.</summary>
   public DefinitionLoader()
   {
      Functions = Load<Dictionary<string, FunctionDefinition>>("CgScriptFunctionDefinitions.json")
                  ?? new Dictionary<string, FunctionDefinition>();
      Objects   = Load<Dictionary<string, ObjectDefinition>>("CgScriptObjectDefinitions.json")
                  ?? new Dictionary<string, ObjectDefinition>();
      Constants = Load<string[]>("CgScriptConstants.json") ?? [];
   }

   /// <summary>Protected constructor for subclasses that supply their own definitions.</summary>
   protected DefinitionLoader(
      Dictionary<string, FunctionDefinition> functions,
      Dictionary<string, ObjectDefinition>   objects,
      IReadOnlyCollection<string>            constants)
   {
      Functions = functions;
      Objects   = objects;
      Constants = constants;
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
