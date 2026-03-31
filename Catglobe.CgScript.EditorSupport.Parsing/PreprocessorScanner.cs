using System.Text.RegularExpressions;

namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>
/// Detects and strips CgScript preprocessor directives (<c>#IF env … #ENDIF</c>)
/// from source text before the ANTLR lexer/parser processes it.
/// </summary>
public static class PreprocessorScanner
{
   /// <summary>
   /// Matches a <c>#IF Development|Production|Staging</c> or <c>#ENDIF</c> directive
   /// at the beginning of a line (after optional horizontal whitespace).
   /// The leading whitespace is captured in group "ws"; the directive itself in "text".
   /// </summary>
   private static readonly Regex s_directivePattern = new Regex(
      @"^(?<ws>[^\S\r\n]*)(?<text>#IF\s+(?:Development|Production|Staging)\b[^\r\n]*|#ENDIF\b[^\r\n]*)\r?$",
      RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

   /// <summary>
   /// Returns the source text with all preprocessor directive lines blanked
   /// (directive text replaced with spaces to preserve column offsets and line count),
   /// together with the list of directive token positions for semantic colouring.
   /// </summary>
   /// <param name="text">The raw document text.</param>
   /// <returns>
   /// <c>CleanedText</c> is safe to feed to the ANTLR lexer/parser.
   /// <c>Directives</c> contains zero-based (line, column, length) tuples for each found directive.
   /// </returns>
   public static (string CleanedText, IReadOnlyList<(int Line0, int Col, int Length)> Directives)
      Strip(string text)
   {
      var directives = new List<(int Line0, int Col, int Length)>();
      var lineStarts  = ComputeLineStarts(text);

      var cleaned = s_directivePattern.Replace(text, m =>
      {
         var grp = m.Groups["text"];
         var (line0, col) = PositionOf(lineStarts, grp.Index);
         directives.Add((line0, col, grp.Length));
         // Keep leading whitespace and any trailing \r; replace only the directive text with spaces.
         int wsLen = grp.Index - m.Index;
         return m.Value.Substring(0, wsLen)
              + new string(' ', grp.Length)
              + m.Value.Substring(wsLen + grp.Length);
      });

      return (cleaned, directives);
   }

   private static int[] ComputeLineStarts(string text)
   {
      var starts = new List<int> { 0 };
      for (int i = 0; i < text.Length; i++)
         if (text[i] == '\n') starts.Add(i + 1);
      return starts.ToArray();
   }

   private static (int Line0, int Col) PositionOf(int[] lineStarts, int charIndex)
   {
      int lo = 0, hi = lineStarts.Length - 1;
      while (lo < hi)
      {
         int mid = (lo + hi + 1) / 2;
         if (lineStarts[mid] <= charIndex) lo = mid;
         else hi = mid - 1;
      }
      return (lo, charIndex - lineStarts[lo]);
   }
}
