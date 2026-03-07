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
   // ── go-to-definition ──────────────────────────────────────────────────────────

   /// <summary>
   /// Returns the location of the declaration of the symbol under the cursor,
   /// or <see langword="null"/> when the cursor is not on a user-defined symbol.
   /// </summary>
   public SumType<Location, Location[]>? OnDefinition(TextDocumentPositionParams p)
   {
      var uri    = p.TextDocument.Uri.ToString();
      var result = _store.GetParseResult(uri);
      if (result is null) return null;

      var decl = ReferenceAnalyzer.FindDeclaration(
         result.Tree,
         cursorLine:   p.Position.Line + 1,   // ANTLR is 1-based
         cursorColumn: p.Position.Character);

      return decl is null ? (SumType<Location, Location[]>?)null
         : new SumType<Location, Location[]>(
               new Location { Uri = p.TextDocument.Uri, Range = ToRange(decl) });
   }

   // ── find references ───────────────────────────────────────────────────────────

   /// <summary>
   /// Returns all references to the symbol under the cursor.
   /// Respects <c>context.includeDeclaration</c>.
   /// </summary>
   public Location[] OnReferences(ReferenceParams p)
   {
      var uri    = p.TextDocument.Uri.ToString();
      var result = _store.GetParseResult(uri);
      if (result is null) return [];

      var refs = ReferenceAnalyzer.FindReferences(
         result.Tree,
         cursorLine:   p.Position.Line + 1,
         cursorColumn: p.Position.Character);

      if (refs.Count == 0) return [];

      bool includeDecl = p.Context?.IncludeDeclaration ?? true;

      var locations = new List<Location>();
      foreach (var r in refs)
      {
         if (includeDecl || !r.IsDeclaration)
            locations.Add(new Location { Uri = p.TextDocument.Uri, Range = ToRange(r) });
      }

      return locations.ToArray();
   }

   // ── prepare rename ────────────────────────────────────────────────────────────

   /// <summary>
   /// Validates that the cursor is on a renameable symbol and returns its current
   /// range, or <see langword="null"/> when rename is not applicable.
   /// Registered under the raw LSP method name <c>textDocument/prepareRename</c>
   /// since the VS LSP library (17.2.x) does not expose a typed Methods entry for it.
   /// </summary>
   public LspRange? OnPrepareRename(TextDocumentPositionParams p)
   {
      var uri    = p.TextDocument.Uri.ToString();
      var result = _store.GetParseResult(uri);
      if (result is null) return null;

      int antlrLine = p.Position.Line + 1;
      int antlrCol  = p.Position.Character;

      var refs = ReferenceAnalyzer.FindReferences(result.Tree, antlrLine, antlrCol);

      foreach (var r in refs)
      {
         if (r.Line   == antlrLine
             && r.Column <= antlrCol
             && antlrCol  <  r.Column + r.Length)
         {
            return ToRange(r);
         }
      }

      return null;
   }

   // ── rename ────────────────────────────────────────────────────────────────────

   /// <summary>
   /// Produces a <see cref="WorkspaceEdit"/> that renames every occurrence of the
   /// symbol under the cursor to <see cref="RenameParams.NewName"/>.
   /// </summary>
   public WorkspaceEdit OnRename(RenameParams p)
   {
      var uri    = p.TextDocument.Uri.ToString();
      var result = _store.GetParseResult(uri);
      if (result is null) return new WorkspaceEdit();

      var refs = ReferenceAnalyzer.FindReferences(
         result.Tree,
         cursorLine:   p.Position.Line + 1,
         cursorColumn: p.Position.Character);

      if (refs.Count == 0) return new WorkspaceEdit();

      var edits = new TextEdit[refs.Count];
      for (int i = 0; i < refs.Count; i++)
         edits[i] = new TextEdit { Range = ToRange(refs[i]), NewText = p.NewName };

      return new WorkspaceEdit
      {
         Changes = new Dictionary<string, TextEdit[]> { [uri] = edits },
      };
   }

   // ── declaration (alias for go-to-definition) ──────────────────────────────────

   /// <summary>
   /// <c>textDocument/declaration</c> — delegates to <see cref="OnDefinition"/>
   /// because CgScript does not distinguish between declaration and definition sites.
   /// </summary>
   public SumType<Location, Location[]>? OnDeclaration(TextDocumentPositionParams p)
      => OnDefinition(p);
}
