using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Catglobe.CgScript.EditorSupport.SourceGenerator;

/// <summary>
/// Detected parameter from a .cgs script.
/// </summary>
internal sealed record ScriptParam(string CsType, string Name, string? Doc = null);

/// <summary>
/// Extracted metadata from a .cgs script file.
/// </summary>
internal sealed record ScriptMetadata(
   string ScriptName,
   string ReturnType,
   IReadOnlyList<ScriptParam> Parameters,
   string? Summary   = null,
   string? ReturnDoc = null);

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
   // @return <CsType> [optional description]
   private static readonly Regex ReturnComment =
      new(@"//\s*@return\s+(\S+)(.*)", RegexOptions.Compiled);

   // @summary <description>
   private static readonly Regex SummaryComment =
      new(@"//\s*@summary\s+(.*)", RegexOptions.Compiled);

   // @param <name> <CsType> [optional description]
   private static readonly Regex ParamComment =
      new(@"//\s*@param\s+(\w+)\s+(\S+)(.*)", RegexOptions.Compiled);

   private static readonly Regex FunctionDecl =
      new(@"function\s*\(([^)]*)\)\s*\{", RegexOptions.Compiled);

   // Pattern B: params[0]["key"] with type on left-hand side
   private static readonly Regex ParamsBRead =
      new(@"(?m)^\s*(\w+)\s+(\w+)\s*=\s*params\s*\[\s*0\s*\]\s*\[\s*""(\w+)""\s*\]",
          RegexOptions.Compiled | RegexOptions.Multiline);

   // Pattern C: dictVar = Workflow_getParameters()[0]; type name = dictVar["key"]
   private static readonly Regex DictAssignDecl =
      new(@"\b(\w+)\s*=\s*Workflow_getParameters\s*\(\s*\)\s*\[\s*0\s*\]",
          RegexOptions.Compiled);

   private static readonly Regex DictRead =
      new(@"(?m)^\s*(\w+)\s+(\w+)\s*=\s*(\w+)\s*\[\s*""(\w+)""\s*\]",
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

   /// <summary>
   /// Parses a .cgs script file.
   /// Returns <c>(metadata, empty)</c> on success, <c>(null, missing)</c> when one or more parameters
   /// use an ambiguous CgScript type (<c>array</c>, <c>Dictionary</c>, etc.) without a
   /// <c>// @param name CsType</c> annotation — the caller should report CGS012 per missing entry.
   /// Returns <c>(null, empty)</c> when no recognisable workflow pattern is found.
   /// </summary>
   public static (ScriptMetadata? Meta, IReadOnlyList<(string Name, string CsType)> MissingAnnotations)
      TryParse(string scriptName, string source)
   {
      // Collect @summary, @return doc, and @param overrides + docs
      var summary     = ExtractSummary(source);
      var (returnType0, returnDoc) = ExtractReturnTypeAndDoc(source);

      var paramOverrides = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
      var paramDocs      = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
      foreach (Match m in ParamComment.Matches(source))
      {
         paramOverrides[m.Groups[1].Value] = AnnotatedToCsType(m.Groups[2].Value);
         var doc = m.Groups[3].Value.Trim();
         if (doc.Length > 0)
            paramDocs[m.Groups[1].Value] = doc;
      }

      ScriptMetadata Make(IReadOnlyList<ScriptParam> parms, string retType) =>
         new ScriptMetadata(scriptName, retType, parms, summary, returnDoc);

      // ── Pattern C: dictVar = Workflow_getParameters()[0]; type name = dictVar["key"] ──
      var dictAssign = DictAssignDecl.Match(source);
      if (dictAssign.Success)
      {
         var dictVar = dictAssign.Groups[1].Value;
         var cParams = new List<ScriptParam>();
         foreach (Match m in DictRead.Matches(source))
         {
            if (m.Groups[3].Value == dictVar)
            {
               var name   = m.Groups[4].Value;
               var csType = paramOverrides.TryGetValue(name, out var ov) ? ov : ToCsType(m.Groups[1].Value);
               cParams.Add(new ScriptParam(csType, name, paramDocs.TryGetValue(name, out var d) ? d : null));
            }
         }
         if (cParams.Count > 0)
         {
            var missing = FindMissingAnnotations(cParams, paramOverrides);
            if (missing.Count > 0) return (null, missing);
            return (Make(cParams, AnnotatedToCsType(returnType0 ?? "void")), _empty);
         }
      }

      // ── Pattern B first (more distinctive) ──────────────────────────────────
      if (source.Contains("Workflow_getParameters()") &&
          source.Contains("params[0]["))
      {
         var bParams = new List<ScriptParam>();
         foreach (Match m in ParamsBRead.Matches(source))
         {
            var cgsType = m.Groups[1].Value;
            var name    = m.Groups[3].Value;
            var csType  = paramOverrides.TryGetValue(name, out var ov) ? ov : ToCsType(cgsType);
            bParams.Add(new ScriptParam(csType, name, paramDocs.TryGetValue(name, out var d) ? d : null));
         }

         if (bParams.Count > 0)
         {
            var missing = FindMissingAnnotations(bParams, paramOverrides);
            if (missing.Count > 0) return (null, missing);
            return (Make(bParams, AnnotatedToCsType(returnType0 ?? "void")), _empty);
         }
      }

      // ── Pattern A ────────────────────────────────────────────────────────────
      var fnMatch = FunctionDecl.Match(source);
      if (fnMatch.Success && source.Contains(".Invoke("))
      {
         var aParams = ParseFunctionParams(fnMatch.Groups[1].Value, paramOverrides, paramDocs);
         var missing = FindMissingAnnotations(aParams, paramOverrides);
         if (missing.Count > 0) return (null, missing);
         return (Make(aParams, AnnotatedToCsType(returnType0 ?? "void")), _empty);
      }

      return (null, _empty);
   }

   private static readonly IReadOnlyList<(string Name, string CsType)> _empty =
      new (string, string)[0];

   /// <summary>
   /// Returns the subset of <paramref name="parms"/> whose C# type is ambiguous
   /// (<c>object</c> or <c>object[]</c>) and for which no <c>@param</c> override was supplied.
   /// </summary>
   private static IReadOnlyList<(string Name, string CsType)> FindMissingAnnotations(
      IReadOnlyList<ScriptParam> parms,
      Dictionary<string, string> paramOverrides)
   {
      List<(string, string)>? missing = null;
      foreach (var p in parms)
      {
         if ((p.CsType == "object" || p.CsType == "object[]") &&
             !paramOverrides.ContainsKey(p.Name))
         {
            (missing ??= new List<(string, string)>()).Add((p.Name, p.CsType));
         }
      }
      return missing ?? (IReadOnlyList<(string, string)>)_empty;
   }

   // ── helpers ──────────────────────────────────────────────────────────────────

   private static string? ExtractSummary(string source)
   {
      var m = SummaryComment.Match(source);
      return m.Success ? m.Groups[1].Value.Trim() : null;
   }

   private static (string? Type, string? Doc) ExtractReturnTypeAndDoc(string source)
   {
      var m = ReturnComment.Match(source);
      if (!m.Success) return (null, null);
      var doc = m.Groups[2].Value.Trim();
      return (m.Groups[1].Value, doc.Length > 0 ? doc : null);
   }

   private static IReadOnlyList<ScriptParam> ParseFunctionParams(
      string paramList,
      Dictionary<string, string> paramOverrides,
      Dictionary<string, string> paramDocs)
   {
      var result = new List<ScriptParam>();
      if (string.IsNullOrWhiteSpace(paramList)) return result;

      foreach (var part in paramList.Split(','))
      {
         var tokens = part.Trim().Split(new[] { ' ', '\t' },
            System.StringSplitOptions.RemoveEmptyEntries);
         if (tokens.Length >= 2)
         {
            var name   = tokens[1];
            var csType = paramOverrides.TryGetValue(name, out var ov) ? ov : ToCsType(tokens[0]);
            result.Add(new ScriptParam(csType, name, paramDocs.TryGetValue(name, out var d) ? d : null));
         }
      }

      return result;
   }

   /// <summary>
   /// Maps a CgScript type keyword to its C# equivalent.
   /// Unknown types fall back to <c>"object"</c> (safe for inferred types from script source).
   /// </summary>
   internal static string ToCsType(string cgsType)
      => TypeMap.TryGetValue(cgsType.ToLowerInvariant(), out var cs) ? cs : "object";

   /// <summary>
   /// Maps a type string from an <c>@return</c> or <c>@param</c> annotation.
   /// Known CgScript keywords are mapped (e.g. <c>number</c> → <c>double</c>);
   /// anything else is passed through as-is so that qualified C# type names are preserved.
   /// </summary>
   internal static string AnnotatedToCsType(string annotated)
      => TypeMap.TryGetValue(annotated.ToLowerInvariant(), out var cs) ? cs : annotated;
}
