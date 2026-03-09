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
      messageFormat:      "The [CgScriptSerializer] context must declare [JsonSerializable(typeof({0}))] for the return type of script '{1}'",
      category:           Category,
      defaultSeverity:    DiagnosticSeverity.Error,
      isEnabledByDefault: true);

   /// <summary>Maps a Parsing-project diagnostic severity to a Roslyn DiagnosticSeverity.</summary>
   public static DiagnosticSeverity ToRoslyn(Catglobe.CgScript.EditorSupport.Parsing.DiagnosticSeverity s)
      => s == Catglobe.CgScript.EditorSupport.Parsing.DiagnosticSeverity.Error
         ? DiagnosticSeverity.Error
         : DiagnosticSeverity.Warning;

   /// <summary>
   /// Returns the <see cref="DiagnosticDescriptor"/> that corresponds to a Parsing diagnostic message.
   /// Falls back to <see cref="UndefinedVariable"/> for unrecognised patterns.
   /// </summary>
   public static DiagnosticDescriptor DescriptorFor(Catglobe.CgScript.EditorSupport.Parsing.Diagnostic d)
   {
      var msg = d.Message;
      if (msg.StartsWith("Illegal variable re-declaration")) return DuplicateDeclaration;
      if (msg.StartsWith("Unknown type '") && !msg.Contains("new "))  return UnknownType;
      if (msg.StartsWith("Unknown type '"))                           return UnknownNewType;
      if (msg.StartsWith("Unknown function "))                        return UnknownFunction;
      if (msg.StartsWith("Undefined variable "))                      return UndefinedVariable;
      if (msg.StartsWith("Empty statement "))                         return EmptyStatement;
      if (msg.StartsWith("Unreachable code"))                         return UnreachableCode;
      if (msg.StartsWith("Variable '") && msg.Contains("before its")) return UseBeforeDefine;
      if (msg.StartsWith("Variable '") && msg.Contains("never used")) return UnusedVariable;
      return d.Severity == Catglobe.CgScript.EditorSupport.Parsing.DiagnosticSeverity.Error
         ? DuplicateDeclaration : UndefinedVariable;
   }
}
