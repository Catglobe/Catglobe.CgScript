using Microsoft.CodeAnalysis;

namespace Catglobe.CgScript.EditorSupport.SourceGenerator;

internal static class CgScriptDiagnostics
{
   private const string Category = "CgScript";

   public static readonly DiagnosticDescriptor DuplicateDeclaration = new(
      id:                 "CGS001",
      title:              "Duplicate variable declaration",
      messageFormat:      "{0}",
      category:           Category,
      defaultSeverity:    DiagnosticSeverity.Warning,
      isEnabledByDefault: true);

   public static readonly DiagnosticDescriptor UnknownType = new(
      id:                 "CGS002",
      title:              "Unknown type",
      messageFormat:      "{0}",
      category:           Category,
      defaultSeverity:    DiagnosticSeverity.Error,
      isEnabledByDefault: true);

   public static readonly DiagnosticDescriptor UnknownNewType = new(
      id:                 "CGS003",
      title:              "Unknown type in new expression",
      messageFormat:      "{0}",
      category:           Category,
      defaultSeverity:    DiagnosticSeverity.Error,
      isEnabledByDefault: true);

   public static readonly DiagnosticDescriptor UnknownFunction = new(
      id:                 "CGS004",
      title:              "Unknown function",
      messageFormat:      "{0}",
      category:           Category,
      defaultSeverity:    DiagnosticSeverity.Warning,
      isEnabledByDefault: true);

   public static readonly DiagnosticDescriptor UndefinedVariable = new(
      id:                 "CGS005",
      title:              "Undefined variable",
      messageFormat:      "{0}",
      category:           Category,
      defaultSeverity:    DiagnosticSeverity.Warning,
      isEnabledByDefault: true);

   public static readonly DiagnosticDescriptor EmptyStatement = new(
      id:                 "CGS006",
      title:              "Empty statement has no effect",
      messageFormat:      "{0}",
      category:           Category,
      defaultSeverity:    DiagnosticSeverity.Warning,
      isEnabledByDefault: true);

   public static readonly DiagnosticDescriptor UnreachableCode = new(
      id:                 "CGS007",
      title:              "Unreachable code",
      messageFormat:      "{0}",
      category:           Category,
      defaultSeverity:    DiagnosticSeverity.Warning,
      isEnabledByDefault: true);

   public static readonly DiagnosticDescriptor UseBeforeDefine = new(
      id:                 "CGS008",
      title:              "Variable used before its declaration",
      messageFormat:      "{0}",
      category:           Category,
      defaultSeverity:    DiagnosticSeverity.Warning,
      isEnabledByDefault: true);

   public static readonly DiagnosticDescriptor UnusedVariable = new(
      id:                 "CGS009",
      title:              "Declared variable is never used",
      messageFormat:      "{0}",
      category:           Category,
      defaultSeverity:    DiagnosticSeverity.Warning,
      isEnabledByDefault: true);

   public static readonly DiagnosticDescriptor MissingCgScriptSerializer = new(
      id:                 "CGS010",
      title:              "Missing or duplicate [CgScriptSerializer] context",
      messageFormat:      "Exactly one JsonSerializerContext must be marked [CgScriptSerializer]: {0}",
      category:           Category,
      defaultSeverity:    DiagnosticSeverity.Error,
      isEnabledByDefault: true);

   public static readonly DiagnosticDescriptor MissingJsonSerializable = new(
      id:                 "CGS011",
      title:              "Missing [JsonSerializable] on CgScript serializer context",
      messageFormat:      "The [CgScriptSerializer] context must declare [JsonSerializable(typeof({0}))] — required by script '{1}'",
      category:           Category,
      defaultSeverity:    DiagnosticSeverity.Warning,
      isEnabledByDefault: true);

   public static readonly DiagnosticDescriptor MissingParamAnnotation = new(
      id:                 "CGS012",
      title:              "CgScript parameter requires @param annotation",
      messageFormat:      "Parameter '{0}' has ambiguous type '{1}'. Add '// @param {0} YourCsType' to the script to specify the C# type for the generated wrapper.",
      category:           Category,
      defaultSeverity:    DiagnosticSeverity.Error,
      isEnabledByDefault: true);

   public static readonly DiagnosticDescriptor InvalidTypeAnnotation = new(
      id:                 "CGS013",
      title:              "Invalid type annotation syntax",
      messageFormat:      "Invalid C# type annotation '{0}' — brackets must appear as '[]' pairs (for example TagItem[] or TagItem[][])",
      category:           Category,
      defaultSeverity:    DiagnosticSeverity.Error,
      isEnabledByDefault: true);

   public static readonly DiagnosticDescriptor DynamicParamType = new(
      id:                 "CGS014",
      title:              "CgScript parameter has dynamic object type",
      messageFormat:      "Parameter '{0}' in script '{1}' has dynamic type 'object'. The wrapper will use reflection-based JSON serialization for this parameter, which is not AOT-safe. Consider annotating with '// @param {0} YourCsType' if the concrete type is known.",
      category:           Category,
      defaultSeverity:    DiagnosticSeverity.Info,
      isEnabledByDefault: true);

   public static readonly DiagnosticDescriptor CStyleForLoop = new(
      id:                 "CGS015",
      title:              "C-style for loop",
      messageFormat:      "{0}",
      category:           Category,
      defaultSeverity:    DiagnosticSeverity.Info,
      isEnabledByDefault: true);

   public static readonly DiagnosticDescriptor UnknownProperty = new(
      id:                 "CGS016",
      title:              "Unknown property name",
      messageFormat:      "{0}",
      category:           Category,
      defaultSeverity:    DiagnosticSeverity.Error,
      isEnabledByDefault: true);

   public static readonly DiagnosticDescriptor UnknownMethod = new(
      id:                 "CGS017",
      title:              "Unknown method name",
      messageFormat:      "{0}",
      category:           Category,
      defaultSeverity:    DiagnosticSeverity.Error,
      isEnabledByDefault: true);

   public static readonly DiagnosticDescriptor ReadonlyProperty = new(
      id:                 "CGS018",
      title:              "Assignment to read-only property",
      messageFormat:      "{0}",
      category:           Category,
      defaultSeverity:    DiagnosticSeverity.Error,
      isEnabledByDefault: true);
   public static DiagnosticSeverity ToRoslyn(Catglobe.CgScript.EditorSupport.Parsing.DiagnosticSeverity s)
      => s == Catglobe.CgScript.EditorSupport.Parsing.DiagnosticSeverity.Error
         ? DiagnosticSeverity.Error
         : s == Catglobe.CgScript.EditorSupport.Parsing.DiagnosticSeverity.Information
         ? DiagnosticSeverity.Info
         : DiagnosticSeverity.Warning;

   /// <summary>
   /// Returns the <see cref="DiagnosticDescriptor"/> that corresponds to a Parsing diagnostic code.
   /// Falls back to <see cref="UndefinedVariable"/> (warning) or <see cref="UnknownType"/> (error)
   /// for unrecognised codes.
   /// </summary>
   public static DiagnosticDescriptor DescriptorFor(Catglobe.CgScript.EditorSupport.Parsing.Diagnostic d)
      => d.Code switch
      {
         "CGS001" => DuplicateDeclaration,
         "CGS002" => UnknownType,
         "CGS003" => UnknownNewType,
         "CGS004" => UnknownFunction,
         "CGS005" => UndefinedVariable,
         "CGS006" => EmptyStatement,
         "CGS007" => UnreachableCode,
         "CGS008" => UseBeforeDefine,
         "CGS009" => UnusedVariable,
         "CGS015" => CStyleForLoop,
         "CGS016" => UnknownProperty,
         "CGS017" => UnknownMethod,
         "CGS018" => ReadonlyProperty,
         _ => d.Severity == Catglobe.CgScript.EditorSupport.Parsing.DiagnosticSeverity.Error
              ? UnknownType : UndefinedVariable,
      };
}
