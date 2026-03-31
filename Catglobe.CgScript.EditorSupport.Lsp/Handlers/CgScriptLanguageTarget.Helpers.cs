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

   /// <summary>
   /// Converts a character offset in <paramref name="text"/> to an ANTLR-style position
   /// (1-based line, 0-based column).  Carriage-return characters (<c>\r</c>) are skipped
   /// so that Windows-style <c>\r\n</c> line endings are treated as a single line break,
   /// matching ANTLR's internal normalization.
   /// </summary>
   private static (int Line, int Column) OffsetToAntlrPosition(string text, int offset)
   {
      int line = 1, col = 0;
      for (int i = 0; i < offset && i < text.Length; i++)
      {
         if (text[i] == '\r') continue; // skip — ANTLR normalises \r\n to \n
         if (text[i] == '\n') { line++; col = 0; }
         else col++;
      }
      return (line, col);
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
   /// Applies a single incremental <see cref="TextDocumentContentChangeEvent"/> to <paramref name="text"/>.
   /// When <c>Range</c> is <see langword="null"/> the event is treated as a full-document replacement.
   /// </summary>
   private static string ApplyChange(string text, TextDocumentContentChangeEvent change)
   {
      if (change.Range is null) return change.Text; // full-document replace fallback
      int start = GetOffset(text, change.Range.Start.Line, change.Range.Start.Character);
      int end   = GetOffset(text, change.Range.End.Line,   change.Range.End.Character);
      return text[..start] + change.Text + text[end..];
   }

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

   /// <summary>
   /// Looks up an <see cref="ObjectDefinition"/> by type name.  Tries an exact (ordinal)
   /// match first, then falls back to a case-insensitive search so that CgScript
   /// lowercase built-in keywords (e.g. <c>array</c>, <c>string</c>) resolve to their
   /// JSON-capitalised counterparts (e.g. <c>Array</c>, <c>String</c>).
   /// </summary>
   private bool TryGetObjectDefinition(string typeName, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ObjectDefinition? def)
   {
      if (_definitions.Objects.TryGetValue(typeName, out def)) return true;
      foreach (var kvp in _definitions.Objects)
      {
         if (string.Equals(kvp.Key, typeName, StringComparison.OrdinalIgnoreCase))
         {
            def = kvp.Value;
            return true;
         }
      }
      def = null;
      return false;
   }
}
