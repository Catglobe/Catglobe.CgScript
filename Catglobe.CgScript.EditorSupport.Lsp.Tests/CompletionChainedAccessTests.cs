using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
using Catglobe.CgScript.EditorSupport.Lsp.Handlers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

/// <summary>
/// Verifies that completion and signature help work for chained property access
/// such as <c>Catglobe.Json.</c> (where <c>Catglobe</c> is a global variable of
/// type <c>GlobalNamespace</c> and <c>Json</c> is a property returning <c>JsonNamespace</c>).
/// </summary>
public class CompletionChainedAccessTests
{
   // ── Test infrastructure ───────────────────────────────────────────────────

   private static (CgScriptLanguageTarget Target, string Uri) CreateTarget(string source)
   {
      var uri         = "file:///test.cgs";
      var definitions = new DefinitionLoader(); // real definitions
      var store       = new DocumentStore(definitions);
      store.Update(uri, source);
      var target = new CgScriptLanguageTarget(store, definitions);
      return (target, uri);
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

   // ── Completion: Catglobe.Json. ─────────────────────────────────────────

   [Fact]
   public void ChainedCompletion_JsonNamespace_ShowsParseMethod()
   {
      // Position the cursor right after "Catglobe.Json."
      const string source = "Catglobe.Json.";
      var (target, uri) = CreateTarget(source);

      var items = GetCompletions(target, uri, line: 0, character: source.Length);

      Assert.Contains(items, i => i.FilterText == "Parse");
   }

   [Fact]
   public void ChainedCompletion_JsonNamespace_ShowsEncodeMethod()
   {
      const string source = "Catglobe.Json.";
      var (target, uri) = CreateTarget(source);

      var items = GetCompletions(target, uri, line: 0, character: source.Length);

      Assert.Contains(items, i => i.FilterText == "Encode");
   }

   [Fact]
   public void ChainedCompletion_JsonNamespace_DoesNotShowUnrelatedMethods()
   {
      // Should only show JsonNamespace members, not all methods of all types
      const string source = "Catglobe.Json.";
      var (target, uri) = CreateTarget(source);

      var items = GetCompletions(target, uri, line: 0, character: source.Length);

      // There should be members (not empty)
      Assert.NotEmpty(items);
      // All returned items should be members of JsonNamespace, not from e.g. Dictionary
      Assert.DoesNotContain(items, i => i.FilterText == "ContainsKey");
   }

   [Fact]
   public void ChainedCompletion_JsonNamespace_PrefixFilter_Works()
   {
      // "Catglobe.Json.P" — only Parse/ParseWcf should show, not Encode
      const string source = "Catglobe.Json.P";
      var (target, uri)  = CreateTarget(source);

      var items = GetCompletions(target, uri, line: 0, character: source.Length);

      Assert.Contains(items,    i => i.FilterText == "Parse");
      Assert.DoesNotContain(items, i => i.FilterText == "Encode");
   }

   // ── Signature help: Catglobe.Json.Parse( ──────────────────────────────

   [Fact]
   public void ChainedSignatureHelp_Parse_ReturnsSignature()
   {
      // Cursor is right after the '('
      const string source = "Catglobe.Json.Parse(";
      var (target, uri) = CreateTarget(source);

      var sig = GetSignatureHelp(target, uri, line: 0, character: source.Length);

      Assert.NotNull(sig);
      Assert.NotEmpty(sig.Signatures);
   }

   [Fact]
   public void ChainedSignatureHelp_Parse_HasJsonParameter()
   {
      const string source = "Catglobe.Json.Parse(";
      var (target, uri) = CreateTarget(source);

      var sig = GetSignatureHelp(target, uri, line: 0, character: source.Length);

      Assert.NotNull(sig);
      var signature = Assert.Single(sig.Signatures);
      Assert.NotNull(signature.Parameters);
      // The single parameter is named 'json'
      Assert.Contains(signature.Parameters,
         p => p.Label.TryGetFirst(out var lbl) && lbl.Contains("json"));
   }

   [Fact]
   public void ChainedSignatureHelp_UnknownMethod_ReturnsNull()
   {
      // 'Read' is not a method on JsonNamespace — no signature help expected
      const string source = "Catglobe.Json.Read(";
      var (target, uri) = CreateTarget(source);

      var sig = GetSignatureHelp(target, uri, line: 0, character: source.Length);

      Assert.Null(sig);
   }
}
