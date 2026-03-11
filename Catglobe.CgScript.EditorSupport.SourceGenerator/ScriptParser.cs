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
   // Also handles 2-step: Array pVar = Workflow_getParameters(); dictVar = pVar[0]
   private static readonly Regex DictAssignDecl =
      new(@"\b(\w+)\s*=\s*Workflow_getParameters\s*\(\s*\)\s*\[\s*0\s*\]",
          RegexOptions.Compiled);

   // Matches the intermediate params variable in 2-step form: pVar = Workflow_getParameters()
   private static readonly Regex ParamsVarAssign =
      new(@"\b(\w+)\s*=\s*Workflow_getParameters\s*\(\s*\)",
          RegexOptions.Compiled);

   // Matches assignment of dictVar from a known array variable: dictVar = arrayVar[0]
   private static readonly Regex DictFromParamsVar =
      new(@"\b(\w+)\s*=\s*(\w+)\s*\[\s*0\s*\]",
          RegexOptions.Compiled);

   private static readonly Regex DictRead =
      new(@"(?m)^\s*(\w+)\s+(\w+)\s*=\s*(\w+)\s*\[\s*""(\w+)""\s*\]",
          RegexOptions.Compiled | RegexOptions.Multiline);

   // Matches optional-param access pattern: type var = dictVar.TryGetValue("key")
   private static readonly Regex DictTryGetRead =
      new(@"(?m)^\s*(\w+)\s+(\w+)\s*=\s*(\w+)\.TryGetValue\s*\(\s*""(\w+)""\s*\)",
          RegexOptions.Compiled | RegexOptions.Multiline);

   // CgScript → C# type map
   // Only string and bool have an unambiguous 1:1 C# equivalent.
   // number/array require an annotation so the caller can say int vs double, TagItem vs plain object, etc.
   private static readonly Dictionary<string, string> TypeMap = new()
   {
      ["number"]   = "double",               // placeholder — requires @param annotation (CGS012)
      ["string"]   = "string",
      ["bool"]     = "bool",
      ["array"]    = "IEnumerable<object>",  // placeholder — requires @param annotation (CGS012)
      ["object"]   = "object",               // requires annotation
      ["question"] = "object",               // requires annotation
      ["void"]     = "void",
      ["int"]      = "int",                  // explicit int allowed in annotations
   };

   /// <summary>
   /// Parses a .cgs script file.
   /// Returns <c>(metadata, empty, empty, empty)</c> on success with no dynamic params.
   /// Returns <c>(metadata, empty, dynamic, empty)</c> when some parameters use dynamic <c>object</c> type (CGS014).
   /// Returns <c>(null, missing, empty, empty)</c> when parameters use ambiguous numeric/collection types without annotation (CGS012).
   /// Returns <c>(null, empty, empty, invalid)</c> when annotations contain malformed type syntax (CGS013).
   /// Returns <c>(null, empty, empty, empty)</c> when no recognisable workflow pattern is found.
   /// </summary>
   public static (ScriptMetadata? Meta,
                  IReadOnlyList<(string Name, string CsType)> MissingAnnotations,
                  IReadOnlyList<(string Name, string CsType)> DynamicParams,
                  IReadOnlyList<string> InvalidAnnotations)
      TryParse(string scriptName, string source)
   {
      List<string>? invalid = null;

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
         {
            var csType = TryAnnotatedToCsType(typeStr.AsSpan());
            if (csType is null) (invalid ??= new List<string>()).Add(typeStr);
            else paramOverrides[name] = csType;
         }
         if (doc.Length > 0)
            paramDocs[name] = doc;
      }

      // Overlay with legacy // @param (allows mixing old annotations in new files)
      foreach (Match m in ParamComment.Matches(source))
      {
         var typeStr = m.Groups[2].Value;
         var csType  = TryAnnotatedToCsType(typeStr.AsSpan());
         if (csType is null) (invalid ??= new List<string>()).Add(typeStr);
         else paramOverrides[m.Groups[1].Value] = csType;
         var doc = m.Groups[3].Value.Trim();
         if (doc.Length > 0)
            paramDocs[m.Groups[1].Value] = doc;
      }

      if (invalid != null) return (null, _emptyMissing, _emptyMissing, invalid);

      string ReturnCs() => TryAnnotatedToCsType((returnType0 ?? "void").AsSpan()) ?? "void";

      ScriptMetadata Make(IReadOnlyList<ScriptParam> parms) =>
         new ScriptMetadata(scriptName, ReturnCs(), parms, summary, returnDoc);

      // ── Pattern C: dictVar = Workflow_getParameters()[0]; type name = dictVar["key"] ──
      // Also handles 2-step: Array pVar = Workflow_getParameters(); dictVar = pVar[0]; type name = dictVar["key"]
      string? cDictVar = null;
      var dictAssign = DictAssignDecl.Match(source);
      if (dictAssign.Success)
      {
         cDictVar = dictAssign.Groups[1].Value;
      }
      else
      {
         var paramsVarMatch = ParamsVarAssign.Match(source);
         if (paramsVarMatch.Success)
         {
            var paramsVar = paramsVarMatch.Groups[1].Value;
            foreach (Match m in DictFromParamsVar.Matches(source))
            {
               if (m.Groups[2].Value == paramsVar)
               {
                  cDictVar = m.Groups[1].Value;
                  break;
               }
            }
         }
      }

      if (cDictVar != null)
      {
         var seen    = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
         var cParams = new List<ScriptParam>();

         void AddParam(string cgsType, string key)
         {
            if (!seen.Add(key)) return;
            var csType = paramOverrides.TryGetValue(key, out var ov) ? ov : ToCsType(cgsType);
            cParams.Add(new ScriptParam(csType, key, paramDocs.TryGetValue(key, out var d) ? d : null));
         }

         foreach (Match m in DictRead.Matches(source))
            if (m.Groups[3].Value == cDictVar) AddParam(m.Groups[1].Value, m.Groups[4].Value);

         foreach (Match m in DictTryGetRead.Matches(source))
            if (m.Groups[3].Value == cDictVar) AddParam(m.Groups[1].Value, m.Groups[4].Value);

         if (cParams.Count > 0)
         {
            var (missing, dynamic) = FindAnnotationIssues(cParams, paramOverrides);
            if (missing.Count > 0) return (null, missing, _emptyMissing, _emptyInvalid);
            return (Make(cParams), _emptyMissing, dynamic, _emptyInvalid);
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
            var (missing, dynamic) = FindAnnotationIssues(bParams, paramOverrides);
            if (missing.Count > 0) return (null, missing, _emptyMissing, _emptyInvalid);
            return (Make(bParams), _emptyMissing, dynamic, _emptyInvalid);
         }
      }

      // ── Pattern A ────────────────────────────────────────────────────────────
      var fnMatch = FunctionDecl.Match(source);
      if (fnMatch.Success && source.Contains(".Invoke("))
      {
         var aParams = ParseFunctionParams(fnMatch.Groups[1].Value, paramOverrides, paramDocs);
         var (missing, dynamic) = FindAnnotationIssues(aParams, paramOverrides);
         if (missing.Count > 0) return (null, missing, _emptyMissing, _emptyInvalid);
         return (Make(aParams), _emptyMissing, dynamic, _emptyInvalid);
      }

      return (null, _emptyMissing, _emptyMissing, _emptyInvalid);
   }

   private static readonly IReadOnlyList<(string Name, string CsType)> _emptyMissing =
      new (string, string)[0];
   private static readonly IReadOnlyList<string> _emptyInvalid =
      new string[0];

   /// <summary>
   /// Partitions parameters into two groups:
   /// <list type="bullet">
   ///   <item><b>missing</b> (CGS012): <c>double</c> or <c>IEnumerable&lt;object&gt;</c> — numeric/collection types where the caller should specify a concrete C# type.</item>
   ///   <item><b>dynamic</b> (CGS014): <c>object</c> — genuinely dynamic/unknown types where reflection-based serialization will be used.</item>
   /// </list>
   /// Parameters that have been overridden by an annotation are excluded from both lists.
   /// </summary>
   private static (IReadOnlyList<(string Name, string CsType)> Missing,
                   IReadOnlyList<(string Name, string CsType)> Dynamic)
      FindAnnotationIssues(
         IReadOnlyList<ScriptParam> parms,
         Dictionary<string, string> paramOverrides)
   {
      List<(string, string)>? missing = null;
      List<(string, string)>? dynamic = null;
      foreach (var p in parms)
      {
         if (paramOverrides.ContainsKey(p.Name)) continue;
         if (p.CsType is "double" or "IEnumerable<object>")
            (missing ??= new List<(string, string)>()).Add((p.Name, p.CsType));
         else if (p.CsType == "object")
            (dynamic ??= new List<(string, string)>()).Add((p.Name, p.CsType));
      }
      return (missing  ?? (IReadOnlyList<(string, string)>)_emptyMissing,
              dynamic  ?? (IReadOnlyList<(string, string)>)_emptyMissing);
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
   /// Maps a type string from a <c>type="…"</c> annotation attribute using
   /// <see cref="ReadOnlySpan{T}"/> slicing — no intermediate string allocations for the bracket logic.
   /// Returns <c>null</c> for syntactically invalid input (e.g. <c>xxx[</c> or <c>xxx[y]</c>).
   /// Known CgScript keywords are mapped; <c>T[]</c> → <c>IEnumerable&lt;T&gt;</c> recursively.
   /// </summary>
   internal static string? TryAnnotatedToCsType(ReadOnlySpan<char> annotated)
   {
      // TypeMap lookup (small allocation for the key; keywords are short ASCII strings)
      if (TypeMap.TryGetValue(annotated.ToString().ToLowerInvariant(), out var cs)) return cs;

      if (annotated.Length >= 2 && annotated[annotated.Length - 1] == ']')
      {
         // Must be "T[]" — the char before ']' must be '['
         if (annotated[annotated.Length - 2] != '[') return null; // e.g. "xxx[y]"
         var inner = TryAnnotatedToCsType(annotated.Slice(0, annotated.Length - 2));
         return inner is null ? null : $"IEnumerable<{inner}>";
      }

      // Any remaining '[' or ']' means malformed (e.g. "xxx[" or "xx]")
      for (int i = 0; i < annotated.Length; i++)
         if (annotated[i] == '[' || annotated[i] == ']') return null;

      return annotated.Length == 0 ? null : annotated.ToString();
   }

   /// <summary>
   /// Non-nullable wrapper for internal CgScript built-in type mapping (TypeMap keywords only).
   /// Always succeeds for types derived from the CgScript runtime itself.
   /// </summary>
   internal static string AnnotatedToCsType(string annotated)
      => TryAnnotatedToCsType(annotated.AsSpan()) ?? annotated;
}
