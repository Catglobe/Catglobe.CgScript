using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
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
   /// When the cursor is inside a <c>name="xxx"</c> attribute of an XML doc comment,
   /// returns both the code references for that parameter <em>and</em> every
   /// <c>name="xxx"</c> occurrence in the enclosing doc block.
   /// When the cursor is on a code symbol, also includes any matching
   /// <c>name="xxx"</c> occurrences in the function's doc block.
   /// Respects <c>context.includeDeclaration</c>.
   /// </summary>
   public Location[] OnReferences(ReferenceParams p)
   {
      var uri    = p.TextDocument.Uri.ToString();
      var result = _store.GetParseResult(uri);
      if (result is null) return [];

      var text  = _store.GetText(uri) ?? string.Empty;
      var lines = text.Split('\n');

      // ── Case 1: cursor is on name="xxx" in an XML doc comment ────────────────────
      var docParam = TryGetDocParamNameAtCursor(lines, p.Position.Line, p.Position.Character);
      if (docParam is not null)
      {
         var codePos  = FindParamInFunctionForDocBlock(lines, p.Position.Line, docParam.Value.Name);
         IReadOnlyList<SymbolRef> codeRefs = codePos is not null
            ? ReferenceAnalyzer.FindReferences(result.Tree, codePos.Value.AntlrLine, codePos.Value.Col)
            : Array.Empty<SymbolRef>();

         bool includeDecl = p.Context?.IncludeDeclaration ?? true;
         var  locations   = new List<Location>();

         foreach (var r in codeRefs)
            if (includeDecl || !r.IsDeclaration)
               locations.Add(new Location { Uri = p.TextDocument.Uri, Range = ToRange(r) });

         // Scan forward to find the function line and collect all xmldoc occurrences.
         int funcLine0 = -1;
         for (int i = p.Position.Line + 1; i < lines.Length; i++)
         {
            var t = lines[i].TrimStart();
            if (t.StartsWith("///") || t.Length == 0) continue;
            if (t.IndexOf("function", StringComparison.Ordinal) >= 0) funcLine0 = i;
            break;
         }
         if (funcLine0 >= 0)
         {
            foreach (var (lspLine, nameStart, nameLen) in FindXmlDocNameOccurrences(lines, funcLine0, docParam.Value.Name))
               locations.Add(new Location
               {
                  Uri   = p.TextDocument.Uri,
                  Range = new LspRange
                  {
                     Start = new Position(lspLine, nameStart),
                     End   = new Position(lspLine, nameStart + nameLen),
                  },
               });
         }

         return locations.ToArray();
      }

      // ── Case 2: cursor is in code — normal refs augmented with xmldoc ─────────────
      var refs = ReferenceAnalyzer.FindReferences(result.Tree, p.Position.Line + 1, p.Position.Character);
      if (refs.Count == 0) return [];

      bool inclDecl = p.Context?.IncludeDeclaration ?? true;
      var  locs     = new List<Location>();
      foreach (var r in refs)
         if (inclDecl || !r.IsDeclaration)
            locs.Add(new Location { Uri = p.TextDocument.Uri, Range = ToRange(r) });

      var wordAtCursor = GetWordAt(text, GetOffset(text, p.Position.Line, p.Position.Character));
      if (!string.IsNullOrEmpty(wordAtCursor))
      {
         foreach (var (lspLine, nameStart, nameLen) in FindXmlDocOccurrencesForCodeParam(text, wordAtCursor, refs))
            locs.Add(new Location
            {
               Uri   = p.TextDocument.Uri,
               Range = new LspRange
               {
                  Start = new Position(lspLine, nameStart),
                  End   = new Position(lspLine, nameStart + nameLen),
               },
            });
      }

      return locs.ToArray();
   }

   // ── prepare rename ────────────────────────────────────────────────────────────

   /// <summary>
   /// Validates that the cursor is on a renameable symbol and returns its current
   /// range, or <see langword="null"/> when rename is not applicable.
   /// Also allows renaming the param name inside a <c>name="xxx"</c> XML doc attribute.
   /// Registered under the raw LSP method name <c>textDocument/prepareRename</c>
   /// since the VS LSP library (17.2.x) does not expose a typed Methods entry for it.
   /// </summary>
   public LspRange? OnPrepareRename(TextDocumentPositionParams p)
   {
      var uri   = p.TextDocument.Uri.ToString();
      var text  = _store.GetText(uri) ?? string.Empty;
      var lines = text.Split('\n');

      // Allow rename when cursor is on name="xxx" in a doc comment.
      var docParam = TryGetDocParamNameAtCursor(lines, p.Position.Line, p.Position.Character);
      if (docParam is not null)
         return new LspRange
         {
            Start = new Position(p.Position.Line, docParam.Value.Start),
            End   = new Position(p.Position.Line, docParam.Value.End),
         };

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
   /// When the cursor is on a <c>name="xxx"</c> doc attribute, also renames the
   /// corresponding code references; when on a code symbol, also renames the
   /// matching <c>name="xxx"</c> attributes in the function's doc block.
   /// </summary>
   public WorkspaceEdit OnRename(RenameParams p)
   {
      var uri    = p.TextDocument.Uri.ToString();
      var result = _store.GetParseResult(uri);
      var text   = _store.GetText(uri) ?? string.Empty;
      var lines  = text.Split('\n');

      var allEdits = new List<TextEdit>();

      var docParam = TryGetDocParamNameAtCursor(lines, p.Position.Line, p.Position.Character);
      IReadOnlyList<SymbolRef> refs;
      int funcLine0 = -1;

      if (docParam is not null)
      {
         // Cursor is on name="xxx" — find code refs and function line.
         var codePos = FindParamInFunctionForDocBlock(lines, p.Position.Line, docParam.Value.Name);
         refs = (result is not null && codePos is not null)
            ? ReferenceAnalyzer.FindReferences(result.Tree, codePos.Value.AntlrLine, codePos.Value.Col)
            : Array.Empty<SymbolRef>();

         for (int i = p.Position.Line + 1; i < lines.Length; i++)
         {
            var t = lines[i].TrimStart();
            if (t.StartsWith("///") || t.Length == 0) continue;
            if (t.IndexOf("function", StringComparison.Ordinal) >= 0) funcLine0 = i;
            break;
         }
      }
      else
      {
         if (result is null) return new WorkspaceEdit();
         refs = ReferenceAnalyzer.FindReferences(result.Tree, p.Position.Line + 1, p.Position.Character);
         if (refs.Count == 0) return new WorkspaceEdit();

         // Locate the containing function's line so we can patch its doc block.
         var decl = refs.FirstOrDefault(r => r.IsDeclaration);
         if (decl is not null)
         {
            int declLine0 = decl.Line - 1; // convert ANTLR 1-based → 0-based
            for (int i = declLine0; i >= 0 && i >= declLine0 - 5; i--)
               if (lines[i].Contains("function", StringComparison.Ordinal)) { funcLine0 = i; break; }
         }
      }

      // Emit a TextEdit for each code reference.
      foreach (var r in refs)
         allEdits.Add(new TextEdit { Range = ToRange(r), NewText = p.NewName });

      // Emit TextEdits for every name="xxx" occurrence in the doc block.
      var paramName = docParam?.Name
                   ?? GetWordAt(text, GetOffset(text, p.Position.Line, p.Position.Character));
      if (!string.IsNullOrEmpty(paramName) && funcLine0 >= 0)
      {
         foreach (var (lspLine, nameStart, nameLen) in FindXmlDocNameOccurrences(lines, funcLine0, paramName))
            allEdits.Add(new TextEdit
            {
               Range   = new LspRange { Start = new Position(lspLine, nameStart), End = new Position(lspLine, nameStart + nameLen) },
               NewText = p.NewName,
            });
      }

      if (allEdits.Count == 0) return new WorkspaceEdit();

      return new WorkspaceEdit
      {
         Changes = new Dictionary<string, TextEdit[]> { [uri] = allEdits.ToArray() },
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
