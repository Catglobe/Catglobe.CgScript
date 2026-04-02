using Catglobe.CgScript.EditorSupport.Lsp.Handlers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

/// <summary>
/// Verifies that signature help is provided when the cursor is inside an indexer
/// expression such as <c>a[</c> (where <c>a</c> is a variable of an indexable type).
/// </summary>
public class IndexerSignatureHelpTests
{
   // ── Test infrastructure ───────────────────────────────────────────────────

   private static (CgScriptLanguageTarget Target, string Uri) CreateTarget(string source)
   {
      var uri         = "file:///test.cgs";
      var definitions = new CgScriptDefinitions();
      var store       = new DocumentStore(definitions);
      store.Update(uri, source);
      var target = new CgScriptLanguageTarget(store, definitions);
      return (target, uri);
   }

   private static SignatureHelp? GetSignatureHelp(
      CgScriptLanguageTarget target, string uri, int line, int character)
   {
      var p = new SignatureHelpParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = new Uri(uri) },
         Position     = new Position(line, character),
      };
      return target.OnSignatureHelp(p);
   }

   private static CompletionItem[] GetCompletions(
      CgScriptLanguageTarget target, string uri, int line, int character)
   {
      var p = new CompletionParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = new Uri(uri) },
         Position     = new Position(line, character),
      };
      var result = target.OnCompletion(p);
      if (result is null) return [];
      if (result.Value.TryGetFirst(out var arr))  return arr  ?? [];
      if (result.Value.TryGetSecond(out var list)) return list?.Items ?? [];
      return [];
   }

   // ── Signature help: array indexer ────────────────────────────────────────

   [Fact]
   public void IndexerSignatureHelp_Array_ReturnsSignature()
   {
      const string source = "array a;\na[";
      var (target, uri) = CreateTarget(source);

      var sig = GetSignatureHelp(target, uri, line: 1, character: 2);

      Assert.NotNull(sig);
      Assert.NotEmpty(sig.Signatures);
   }

   [Fact]
   public void IndexerSignatureHelp_Array_HasIndexParameter()
   {
      const string source = "array a;\na[";
      var (target, uri) = CreateTarget(source);

      var sig = GetSignatureHelp(target, uri, line: 1, character: 2);

      Assert.NotNull(sig);
      Assert.NotEmpty(sig.Signatures);
      // At least one signature should have a parameter named 'index'
      Assert.Contains(sig.Signatures, s =>
         s.Parameters != null &&
         s.Parameters.Any(p => p.Label.TryGetFirst(out var lbl) && lbl.Contains("index")));
   }

   [Fact]
   public void IndexerSignatureHelp_Array_ActiveParameterIsZero()
   {
      const string source = "array a;\na[";
      var (target, uri) = CreateTarget(source);

      var sig = GetSignatureHelp(target, uri, line: 1, character: 2);

      Assert.NotNull(sig);
      Assert.Equal(0, sig.ActiveParameter);
   }

   [Fact]
   public void IndexerSignatureHelp_Dictionary_ReturnsSignature()
   {
      const string source = "Dictionary d;\nd[";
      var (target, uri) = CreateTarget(source);

      var sig = GetSignatureHelp(target, uri, line: 1, character: 2);

      Assert.NotNull(sig);
      Assert.NotEmpty(sig.Signatures);
   }

   [Fact]
   public void IndexerSignatureHelp_UnknownType_ReturnsNull()
   {
      // 'foo' is not a declared variable — should return null
      const string source = "foo[";
      var (target, uri) = CreateTarget(source);

      var sig = GetSignatureHelp(target, uri, line: 0, character: 4);

      Assert.Null(sig);
   }

   [Fact]
   public void IndexerSignatureHelp_NestedCall_DoesNotConfuse()
   {
      // Signature help inside a method call should still work for the paren, not the bracket
      const string source = "array a;\na.IndexOf(";
      var (target, uri) = CreateTarget(source);

      // Line 1 is "a.IndexOf(", cursor after the '('
      var sig = GetSignatureHelp(target, uri, line: 1, character: "a.IndexOf(".Length);

      Assert.NotNull(sig);
      // Verify it's for IndexOf, not the indexer
      Assert.Contains(sig.Signatures, s => s.Label.Contains("IndexOf"));
   }

   // ── Completion: [] methods filtered from dot-notation list ───────────────

   [Fact]
   public void DotNotationCompletion_Array_DoesNotShowBracketMethods()
   {
      // When typing "a.", the "[]" methods should NOT appear as completion items
      const string source = "array a;\na.";
      var (target, uri) = CreateTarget(source);

      var items = GetCompletions(target, uri, line: 1, character: 2);

      Assert.NotEmpty(items);
      // No item should have FilterText "[]"
      Assert.DoesNotContain(items, i => i.FilterText == "[]");
   }

   [Fact]
   public void DotNotationCompletion_Array_StillShowsNormalMethods()
   {
      // "a." should still show regular methods like Add, Contains, etc.
      const string source = "array a;\na.";
      var (target, uri) = CreateTarget(source);

      var items = GetCompletions(target, uri, line: 1, character: 2);

      Assert.Contains(items, i => i.FilterText == "Add");
      Assert.Contains(items, i => i.FilterText == "Contains");
   }
}
