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
/// C# XML doc annotations (preferred):
///   /// &lt;summary&gt;Gets the count.&lt;/summary&gt;
///   /// &lt;param name="companyFolderId"&gt;The folder.&lt;/param&gt;
///   /// &lt;param name="tags" type="TagItem[]"&gt;Tag items.&lt;/param&gt;
///   /// &lt;returns type="TagSummary"&gt;The result.&lt;/returns&gt;
///   function(number companyFolderId, array tags) { ... }.Invoke(Workflow_getParameters()[0]);
///
/// Legacy annotations (still supported):
///   // @summary Gets the count
///   // @return TagSummary The result
///   // @param tags TagItem[] Tag items
/// </summary>
internal static class ScriptParser
{
   // ── C# XML doc format (/// style) ─────────────────────────────────────────────
   // Extract only ///–prefixed lines and strip the prefix, producing pure XML fragments.
   private static readonly Regex TripleSlashLine =
      new(@"^[ \t]*///[ \t]?(.*)", RegexOptions.Compiled | RegexOptions.Multiline);

   private static readonly Regex XmlSummary =
      new(@"<summary>(.*?)</summary>", RegexOptions.Compiled | RegexOptions.Singleline);

   // <param name="name"> or <param name="name" type="CsType">description</param>
   private static readonly Regex XmlParam =
      new(@"<param\s+name=""(\w+)""(?:\s+type=""([^""]+)"")?\s*>(.*?)</param>",
          RegexOptions.Compiled | RegexOptions.Singleline);

   // <returns> or <returns type="CsType">description</returns>
   private static readonly Regex XmlReturns =
      new(@"<returns(?:\s+type=""([^""]+)"")?\s*>(.*?)</returns>",
          RegexOptions.Compiled | RegexOptions.Singleline);

   // ── Legacy // @ format (backward compat) ──────────────────────────────────────
   private static readonly Regex ReturnComment =
      new(@"//\s*@return\s+(\S+)(.*)", RegexOptions.Compiled);

   private static readonly Regex SummaryComment =
      new(@"//\s*@summary\s+(.*)", RegexOptions.Compiled);

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
      // Collect annotations — try C# XML doc format first, fall back to // @ legacy
      var docContent = ExtractTripleSlashContent(source);
      var summary     = ExtractXmlSummary(docContent) ?? ExtractLegacySummary(source);
      var (returnType0, returnDoc) = ExtractXmlReturns(docContent) is { } xmlRet
         ? xmlRet
         : ExtractLegacyReturns(source);

      var paramOverrides = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
      var paramDocs      = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

      // Populate from XML doc params
      foreach (Match m in XmlParam.Matches(docContent))
      {
         var name    = m.Groups[1].Value;
         var typeStr = m.Groups[2].Value;  // empty string when attribute absent
         var doc     = CollapseWhitespace(m.Groups[3].Value);
         if (typeStr.Length > 0)
            paramOverrides[name] = AnnotatedToCsType(typeStr);
         if (doc.Length > 0)
            paramDocs[name] = doc;
      }

      // Overlay with legacy // @param (allows mixing old annotations in new files)
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

   // ── doc extraction helpers ────────────────────────────────────────────────────

   /// <summary>
   /// Extracts only <c>///</c>-prefixed lines, stripping the prefix, giving clean XML fragments.
   /// Regular <c>//</c> comments are excluded so they cannot accidentally match XML patterns.
   /// </summary>
   private static string ExtractTripleSlashContent(string source)
   {
      var sb = new System.Text.StringBuilder();
      foreach (Match m in TripleSlashLine.Matches(source))
         sb.AppendLine(m.Groups[1].Value);
      return sb.ToString();
   }

   /// <summary>Extracts summary text from already-stripped XML doc content.</summary>
   private static string? ExtractXmlSummary(string docContent)
   {
      var m = XmlSummary.Match(docContent);
      if (!m.Success) return null;
      return CollapseWhitespace(m.Groups[1].Value);
   }

   /// <summary>Extracts return type+doc from already-stripped XML doc content.</summary>
   private static (string? Type, string? Doc)? ExtractXmlReturns(string docContent)
   {
      var m = XmlReturns.Match(docContent);
      if (!m.Success) return null;
      var type = m.Groups[1].Value;  // empty when attribute absent
      var doc  = CollapseWhitespace(m.Groups[2].Value);
      return (type.Length > 0 ? type : null, doc.Length > 0 ? doc : null);
   }

   private static string? ExtractLegacySummary(string source)
   {
      var m = SummaryComment.Match(source);
      return m.Success ? m.Groups[1].Value.Trim() : null;
   }

   private static (string? Type, string? Doc) ExtractLegacyReturns(string source)
   {
      var m = ReturnComment.Match(source);
      if (!m.Success) return (null, null);
      var doc = m.Groups[2].Value.Trim();
      return (m.Groups[1].Value, doc.Length > 0 ? doc : null);
   }

   /// <summary>Collapses leading/trailing whitespace and internal newlines to single spaces.</summary>
   private static string CollapseWhitespace(string s)
   {
      // Strip leading /// prefix remnants that may survive on continuation lines
      s = System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"\s+", " ");
      return s;
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
