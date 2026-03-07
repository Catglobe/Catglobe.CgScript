using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
using Catglobe.CgScript.EditorSupport.Parsing;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;
using System.Collections.Concurrent;
using System.Threading;
using LspDiagnostic = Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic;
using LspRange      = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace Catglobe.CgScript.EditorSupport.Lsp.Handlers;

public partial class CgScriptLanguageTarget
{
   // ── position helpers ──────────────────────────────────────────────────────────

   /// <summary>Converts a (line, character) LSP position to a character offset in <paramref name="text"/>.</summary>
   private static int GetOffset(string text, int line, int character)
   {
      int currentLine = 0, i = 0;
      while (i < text.Length && currentLine < line)
         if (text[i++] == '\n') currentLine++;
      return Math.Min(i + character, text.Length);
   }

   /// <summary>Returns the identifier prefix ending at <paramref name="offset"/> (may be empty).</summary>
   private static string GetWordPrefix(string text, int offset)
   {
      int start = offset;
      while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
         start--;
      return text[start..offset];
   }

   /// <summary>
   /// Returns the complete identifier word that <paramref name="offset"/> falls inside
   /// (or touches), looking both left and right from the cursor position.
   /// Returns an empty string when the cursor is not inside an identifier.
   /// </summary>
   private static string GetWordAt(string text, int offset)
   {
      if (offset < 0 || offset > text.Length) return string.Empty;
      int start = offset;
      while (start > 0 && IsWordChar(text[start - 1])) start--;
      int end = offset;
      while (end < text.Length && IsWordChar(text[end])) end++;
      return text[start..end];
   }

   private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

   /// <summary>
   /// Returns the identifier immediately to the left of <paramref name="pos"/>,
   /// skipping any leading whitespace. Returns <c>null</c> if none found.
   /// </summary>
   private static string? GetIdentifierBefore(string text, int pos)
   {
      int end = pos;
      while (end > 0 && text[end - 1] == ' ') end--;
      int start = end;
      while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
         start--;
      return start < end ? text[start..end] : null;
   }
}
