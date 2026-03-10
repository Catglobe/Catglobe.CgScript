using System.Text.RegularExpressions;
using Catglobe.CgScript.EditorSupport.Parsing;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace Catglobe.CgScript.EditorSupport.Lsp.Handlers;

public partial class CgScriptLanguageTarget
{
   // ── Xmldoc param name detection ───────────────────────────────────────────────

   /// <summary>
   /// If the cursor is inside the value of a <c>name="xxx"</c> attribute on a <c>///</c>
   /// comment line, returns <c>(paramName, nameStartCol, nameEndCol)</c> (all 0-based
   /// column values); otherwise returns <c>null</c>.
   /// </summary>
   private static (string Name, int Start, int End)? TryGetDocParamNameAtCursor(
      string[] lines, int lspLine, int lspCol)
   {
      if (lspLine >= lines.Length) return null;
      var lineText = lines[lspLine];
      if (!lineText.TrimStart().StartsWith("///")) return null;

      const string prefix = "name=\"";
      int idx = lineText.IndexOf(prefix, StringComparison.Ordinal);
      if (idx < 0) return null;
      int nameStart = idx + prefix.Length;
      int nameEnd   = lineText.IndexOf('"', nameStart);
      if (nameEnd < 0) return null;

      if (lspCol >= nameStart && lspCol <= nameEnd)
         return (lineText[nameStart..nameEnd], nameStart, nameEnd);
      return null;
   }

   /// <summary>
   /// Scans backward through the contiguous <c>///</c> block that immediately precedes
   /// <paramref name="functionLineIdx"/> (0-based) and collects every occurrence of
   /// <c>name="<paramref name="paramName"/>"</c>, returning each as
   /// <c>(lspLine, nameStartCol, nameLen)</c> — all 0-based.
   /// </summary>
   private static List<(int LspLine, int NameStart, int NameLen)> FindXmlDocNameOccurrences(
      string[] lines, int functionLineIdx, string paramName)
   {
      var result = new List<(int, int, int)>();
      const string prefix = "name=\"";
      string target = prefix + paramName + "\"";

      for (int i = functionLineIdx - 1; i >= 0; i--)
      {
         var t = lines[i].TrimStart();
         if (!t.StartsWith("///")) break;

         int searchFrom = 0;
         while (true)
         {
            int pos = lines[i].IndexOf(target, searchFrom, StringComparison.Ordinal);
            if (pos < 0) break;
            result.Add((i, pos + prefix.Length, paramName.Length));
            searchFrom = pos + 1;
         }
      }
      return result;
   }

   /// <summary>
   /// Scans forward from <paramref name="lspLine"/> (0-based) to find the
   /// <c>function</c> declaration line that immediately follows the doc block, then
   /// locates <paramref name="paramName"/> as a whole word in its parameter list.
   /// Returns <c>(antlrLine, col)</c> — 1-based line, 0-based column, suitable for
   /// <see cref="ReferenceAnalyzer"/> — or <c>null</c> when not found.
   /// </summary>
   private static (int AntlrLine, int Col)? FindParamInFunctionForDocBlock(
      string[] lines, int lspLine, string paramName)
   {
      // Find the function declaration line below the doc block.
      int funcLine = -1;
      for (int i = lspLine + 1; i < lines.Length; i++)
      {
         var t = lines[i].TrimStart();
         if (t.StartsWith("///") || t.Length == 0) continue;
         if (t.IndexOf("function", StringComparison.Ordinal) >= 0)
            funcLine = i;
         break;
      }
      if (funcLine < 0) return null;

      // Accumulate function lines and search for the param name as a whole word.
      var sb      = new System.Text.StringBuilder();
      var pattern = new Regex($@"\b{Regex.Escape(paramName)}\b");
      for (int i = funcLine; i < lines.Length && i < funcLine + 16; i++)
      {
         sb.Append(lines[i]);
         var m = pattern.Match(sb.ToString());
         if (!m.Success) { sb.Append('\n'); continue; }

         // Convert the flat match index back to (line, col).
         var accumulated = sb.ToString()[..m.Index];
         int extraLines  = accumulated.Count(c => c == '\n');
         int lastNl      = accumulated.LastIndexOf('\n');
         int col         = m.Index - (lastNl + 1);
         return (funcLine + extraLines + 1, col); // ANTLR 1-based line
      }
      return null;
   }

   /// <summary>
   /// For a code symbol (typically a function parameter), locates the doc block
   /// above its containing <c>function</c> declaration and returns every
   /// <c>name="<paramref name="paramName"/>"</c> occurrence as
   /// <c>(lspLine, nameStartCol, nameLen)</c>.
   /// </summary>
   private static List<(int LspLine, int NameStart, int NameLen)> FindXmlDocOccurrencesForCodeParam(
      string text, string paramName, IReadOnlyList<SymbolRef> refs)
   {
      var lines = text.Split('\n');
      var decl  = refs.FirstOrDefault(r => r.IsDeclaration);
      if (decl is null) return [];

      int declLine0 = decl.Line - 1; // convert ANTLR 1-based → 0-based
      int funcLine  = -1;
      for (int i = declLine0; i >= 0 && i >= declLine0 - 5; i--)
      {
         if (lines[i].Contains("function", StringComparison.Ordinal))
         { funcLine = i; break; }
      }
      if (funcLine < 0) return [];

      return FindXmlDocNameOccurrences(lines, funcLine, paramName);
   }
}
