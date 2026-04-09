using System.Text.RegularExpressions;

namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>
/// Pre-processes raw QSL text before ANTLR parsing.
/// QSL files store question text, SQ/AO text, IF conditions, and GOTO IF conditions
/// as unquoted free-form text; the grammar requires them to be double-quoted strings.
/// This preprocessor wraps those regions in double quotes (escaping as needed) so
/// the ANTLR grammar can parse them without modification.
/// Ported from CatGlobe.Framework.Qsl.QslPreprocessor.
/// </summary>
internal static partial class QslPreprocessor
{
   // Wrap the CGScript expression inside IF (expr) → IF "expr"
   private const string CleanupIfPat =
      /*language=regex*/ @"^(\s*IF\s*)(\(.*?\))\s*(\r?$|REPLACE|INC_AO|EXC_AO|INC_SQ|EXC_SQ|INC_AO_FROM|EXC_AO_FROM|ORCLEARCURRENT)";

   // Wrap unquoted SQ / answer-option text: `SQ:[props] free text` → `SQ:[props] "free text"`
   private const string CleanupSqPat =
      /*language=regex*/ @"(^(?:\d+|SQ)\s*:(?:\[.+?\s*;\s*\])?)(.*?)\r?$";

   // Wrap the expression in `GOTO LABEL IF expr [ANDCLEAR [...]]` → `GOTO LABEL IF "expr" [ANDCLEAR [...]]`
   private const string CleanupGotoPat =
      /*language=regex*/ @"(^\s*GOTO\s+[\S]+\s+IF\s+)(.+?)(\s*ANDCLEAR\s*\[[^\]]+\])?\r?$";

   // Wrap the unquoted question text line that follows the QUESTION header + optional [properties] block.
   private const string CleanupQtestPat =
      /*language=regex*/ @"(^\s*(?:HIDDEN)?\s*QUESTION\s+.*(?:\r?\n)(?:^IF\s+\(.*[\r\n]+)*(?:^\[[\s\S]+?(?:^\][ \t]*(?:\r?\n)))?)(.*?)\r?$";

#if NET7_0_OR_GREATER
   [GeneratedRegex(CleanupIfPat,    RegexOptions.Multiline | RegexOptions.Compiled, -1)]
   private static partial Regex CleanupIf();

   [GeneratedRegex(CleanupSqPat,    RegexOptions.Multiline | RegexOptions.Compiled, -1)]
   private static partial Regex CleanupAosq();

   [GeneratedRegex(CleanupGotoPat,  RegexOptions.Multiline | RegexOptions.Compiled, -1)]
   private static partial Regex CleanupGoto();

   [GeneratedRegex(CleanupQtestPat, RegexOptions.Multiline | RegexOptions.Compiled, -1)]
   private static partial Regex CleanupQtext();
#else
   private static readonly Regex _cleanupIf    = new(CleanupIfPat,    RegexOptions.Multiline | RegexOptions.Compiled);
   private static readonly Regex _cleanupAosq  = new(CleanupSqPat,    RegexOptions.Multiline | RegexOptions.Compiled);
   private static readonly Regex _cleanupGoto  = new(CleanupGotoPat,  RegexOptions.Multiline | RegexOptions.Compiled);
   private static readonly Regex _cleanupQtext = new(CleanupQtestPat, RegexOptions.Multiline | RegexOptions.Compiled);
   private static Regex CleanupIf()    => _cleanupIf;
   private static Regex CleanupAosq()  => _cleanupAosq;
   private static Regex CleanupGoto()  => _cleanupGoto;
   private static Regex CleanupQtext() => _cleanupQtext;
#endif

   /// <summary>
   /// Transforms raw QSL into the form expected by the ANTLR grammar.
   /// Order: GOTO expressions → question text → SQ/AO text → IF conditions.
   /// </summary>
   public static string Clean(string qsl) =>
      CleanupIf().Replace(
         CleanupAosq().Replace(
            CleanupQtext().Replace(
               CleanupGoto().Replace(qsl, Replacer),
               Replacer),
            Replacer),
         IfReplacer);

   // Wraps group[2] in double-quotes, escaping backslashes and internal quotes.
   private static string Replacer(Match match) =>
      match.Groups[1] + "\"" +
      match.Groups[2].ToString().Replace("\\", "\\\\").Replace("\"", "\\\"") +
      (match.Groups.Count > 3 ? "\" " + match.Groups[3] : "\"");

   // Like Replacer but also strips the outer parentheses from group[2] (used for IF conditions).
   private static string IfReplacer(Match match) =>
      match.Groups[1] + "\"" +
      match.Groups[2].ToString()
         .Substring(1, match.Groups[2].Length - 2)   // strip ( )
         .Replace("\\", "\\\\").Replace("\"", "\\\"") +
      (match.Groups.Count > 3 ? "\" " + match.Groups[3] : "\"");
}
