using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Catglobe.CgScript.EditorSupport.SourceGenerator;

/// <summary>
/// Detected parameter from a .cgs script.
/// </summary>
internal sealed record ScriptParam(string CgsType, string Name);

/// <summary>
/// Extracted metadata from a .cgs script file.
/// </summary>
internal sealed record ScriptMetadata(
   string ScriptName,
   string ReturnType,
   IReadOnlyList<ScriptParam> Parameters);

/// <summary>
/// Extracts the script name, return type and parameter list from a .cgs source file.
///
/// Pattern A — function-based workflow:
///   // @return int Gets the count
///   function(number companyFolderId, string name) { ... }.Invoke(Workflow_getParameters()[0]);
///
/// Pattern B — array-based workflow:
///   array params = Workflow_getParameters();
///   string name = params[0]["name"];
///   bool active = params[0]["active"];
/// </summary>
internal static class ScriptParser
{
   // Pattern A: optional @return comment + function declaration
   private static readonly Regex ReturnComment =
      new(@"//\s*@return\s+(\w+)", RegexOptions.Compiled);

   private static readonly Regex FunctionDecl =
      new(@"function\s*\(([^)]*)\)\s*\{", RegexOptions.Compiled);

   // Pattern B: params[0]["key"] with type on left-hand side
   private static readonly Regex ParamsBRead =
      new(@"(?m)^\s*(\w+)\s+(\w+)\s*=\s*params\s*\[\s*0\s*\]\s*\[\s*""(\w+)""\s*\]",
          RegexOptions.Compiled | RegexOptions.Multiline);

   // CgScript → C# type map
   private static readonly Dictionary<string, string> TypeMap = new()
   {
      ["number"]   = "double",
      ["string"]   = "string",
      ["bool"]     = "bool",
      ["array"]    = "object[]",
      ["object"]   = "object",
      ["question"] = "object",
      ["void"]     = "void",
      ["int"]      = "int",      // @return can say int
   };

   public static ScriptMetadata? TryParse(string scriptName, string source)
   {
      // ── Pattern B first (more distinctive) ──────────────────────────────────
      if (source.Contains("Workflow_getParameters()") &&
          source.Contains("params[0]["))
      {
         var bParams = new List<ScriptParam>();
         foreach (Match m in ParamsBRead.Matches(source))
         {
            var cgsType = m.Groups[1].Value;
            // Use the dict-key name (group 3) as the canonical parameter name
            bParams.Add(new ScriptParam(cgsType, m.Groups[3].Value));
         }

         if (bParams.Count > 0)
         {
            // Return type: look for @return comment
            string returnType = ExtractReturnType(source) ?? "void";
            return new ScriptMetadata(scriptName, ToCsType(returnType), bParams);
         }
      }

      // ── Pattern A ────────────────────────────────────────────────────────────
      var fnMatch = FunctionDecl.Match(source);
      if (fnMatch.Success && source.Contains(".Invoke("))
      {
         var aParams = ParseFunctionParams(fnMatch.Groups[1].Value);
         string returnType = ExtractReturnType(source) ?? "void";
         return new ScriptMetadata(scriptName, ToCsType(returnType), aParams);
      }

      return null;
   }

   // ── helpers ──────────────────────────────────────────────────────────────────

   private static string? ExtractReturnType(string source)
   {
      var m = ReturnComment.Match(source);
      return m.Success ? m.Groups[1].Value : null;
   }

   private static IReadOnlyList<ScriptParam> ParseFunctionParams(string paramList)
   {
      var result = new List<ScriptParam>();
      if (string.IsNullOrWhiteSpace(paramList)) return result;

      foreach (var part in paramList.Split(','))
      {
         var tokens = part.Trim().Split(new[] { ' ', '\t' },
            System.StringSplitOptions.RemoveEmptyEntries);
         if (tokens.Length >= 2)
            result.Add(new ScriptParam(tokens[0], tokens[1]));
      }

      return result;
   }

   internal static string ToCsType(string cgsType)
      => TypeMap.TryGetValue(cgsType.ToLowerInvariant(), out var cs) ? cs : "object";
}
