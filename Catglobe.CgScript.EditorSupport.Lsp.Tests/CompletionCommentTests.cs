using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
using Catglobe.CgScript.EditorSupport.Lsp.Handlers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

/// <summary>
/// Verifies that no code completions are offered while the cursor is inside a
/// single-line comment (<c>//</c> or <c>///</c>).  Showing completions inside
/// comments is noise and can interfere with the editor's semantic-token refresh
/// cycle, causing syntax colouring to lag behind user input.
/// </summary>
public class CompletionCommentTests
{
   private static CompletionItem[] GetCompletions(string source, int line, int col)
   {
      const string uri  = "file:///test.cgs";
      var definitions   = new DefinitionLoader();
      var store         = new DocumentStore(definitions);
      store.Update(uri, source);
      var target = new CgScriptLanguageTarget(store, definitions);
      var p = new CompletionParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = new Uri(uri) },
         Position     = new Position(line, col),
      };
      var result = target.OnCompletion(p);
      if (result is null) return [];
      if (result.Value.TryGetFirst(out var arr))   return arr  ?? [];
      if (result.Value.TryGetSecond(out var list)) return list?.Items ?? [];
      return [];
   }

   // ── Inside // comment → no completions ───────────────────────────────────

   [Fact]
   public void OnCommentLine_ReturnsNoCompletions()
   {
      const string src = "number a = 1;\n// this is a comment";
      var items = GetCompletions(src, line: 1, col: 3);
      Assert.Empty(items);
   }

   [Fact]
   public void OnCommentLineInsideBlock_ReturnsNoCompletions()
   {
      const string src = "number a = 1;\n{\n// comment inside block\n}";
      var items = GetCompletions(src, line: 2, col: 3);
      Assert.Empty(items);
   }

   [Fact]
   public void OnIndentedCommentLine_ReturnsNoCompletions()
   {
      const string src = "number a = 1;\n  // indented comment";
      var items = GetCompletions(src, line: 1, col: 5);
      Assert.Empty(items);
   }

   [Fact]
   public void OnDocCommentLine_ReturnsNoCompletions()
   {
      // Any comment line (including '///') yields no completions.
      const string src = "number a = 1;\n///";
      var items = GetCompletions(src, line: 1, col: 3);
      Assert.Empty(items);
   }

   // ── Outside comment → completions ARE offered ─────────────────────────────

   [Fact]
   public void OnNonCommentLine_ReturnsCompletions()
   {
      const string src = "number a = 1;\n";
      var items = GetCompletions(src, line: 1, col: 0);
      // Keyword completions (if, while, …) should always be present.
      Assert.NotEmpty(items);
   }
}
