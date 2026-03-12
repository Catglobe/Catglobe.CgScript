using Catglobe.CgScript.EditorSupport.Parsing;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace Catglobe.CgScript.EditorSupport.Lsp.Handlers;

public partial class CgScriptLanguageTarget
{
   // ── document formatting ───────────────────────────────────────────────────────

   /// <summary>
   /// Handles <c>textDocument/formatting</c> requests.
   /// Returns a single whole-document <see cref="TextEdit"/> that replaces the
   /// current content with the formatted text, or an empty array when the document
   /// is already formatted or cannot be retrieved.
   /// </summary>
   public TextEdit[] OnDocumentFormatting(DocumentFormattingParams p)
   {
      var uri  = p.TextDocument.Uri.ToString();
      var text = _store.GetText(uri);
      if (text is null) return [];

      var formatted = CgScriptFormatter.Format(text, p.Options);
      if (formatted == text) return [];

      // Replace the whole document with a single edit.
      // Normalise to '\n' so the end-position calculation is correct on all platforms.
      var normalised = text.ReplaceLineEndings("\n");
      var lines      = normalised.Split('\n');
      var lastLine   = lines[^1];
      return
      [
         new TextEdit
         {
            Range   = new LspRange
            {
               Start = new Position(0, 0),
               End   = new Position(lines.Length - 1, lastLine.Length),
            },
            NewText = formatted,
         },
      ];
   }
}
