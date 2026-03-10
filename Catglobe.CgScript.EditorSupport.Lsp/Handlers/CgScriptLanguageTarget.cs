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

/// <summary>
/// Handles all LSP requests/notifications from the client.
/// Methods are dispatched by StreamJsonRpc via [JsonRpcMethod] attributes.
/// StreamJsonRpc deserialises parameters using the <see cref="LspSessionHost"/>-configured formatter,
/// which handles the <c>IProgress&lt;T&gt;</c> progress-token properties on LSP types.
/// </summary>
public partial class CgScriptLanguageTarget
{
   protected readonly DocumentStore    _store;
   protected readonly DefinitionLoader _definitions;

   // Set after construction to break the circular dependency (JsonRpc needs target, target needs JsonRpc).
   public JsonRpc? Rpc { get; set; }

   // ── Semantic tokens delta cache ───────────────────────────────────────────────
   private readonly ConcurrentDictionary<string, (int[] Data, string ResultId)> _semanticCache = new();
   private int _semanticResultIdCounter;

   // ── Client capability flags (set during Initialize) ───────────────────────────
   private bool _clientSupportsMarkdownHover;
   private bool _clientSupportsSnippets;

   public CgScriptLanguageTarget(DocumentStore store, DefinitionLoader definitions)
   {
      _store       = store;
      _definitions = definitions;
   }

   // ── initialize ──────────────────────────────────────────────────────────────

   public InitializeResult Initialize(InitializeParams? p = null)
   {
      var hoverFormats = p?.Capabilities?.TextDocument?.Hover?.ContentFormat;
      _clientSupportsMarkdownHover = hoverFormats?.Contains(MarkupKind.Markdown) ?? false;
      _clientSupportsSnippets        = p?.Capabilities?.TextDocument?.Completion?.CompletionItem?.SnippetSupport ?? false;
      return new InitializeResult
      {
         Capabilities = new ServerCapabilities
         {
            TextDocumentSync = new TextDocumentSyncOptions
            {
               OpenClose = true,
               Change    = TextDocumentSyncKind.Incremental,
            },
            CompletionProvider = new CompletionOptions
            {
               ResolveProvider   = false,
               TriggerCharacters = [".", "/"],
            },
            HoverProvider              = true,
            SignatureHelpProvider      = new SignatureHelpOptions { TriggerCharacters = ["(", ","] },
            DefinitionProvider         = new SumType<bool, DefinitionOptions>(true),
            ReferencesProvider         = new SumType<bool, ReferenceOptions>(true),
            RenameProvider             = new SumType<bool, RenameOptions>(new RenameOptions { PrepareProvider = true }),
            DocumentHighlightProvider  = new SumType<bool, DocumentHighlightOptions>(true),
            DocumentSymbolProvider     = new SumType<bool, DocumentSymbolOptions>(true),
            FoldingRangeProvider       = new SumType<bool, FoldingRangeOptions>(true),
            CodeActionProvider         = new SumType<bool, CodeActionOptions>(true),
            SemanticTokensOptions = new SemanticTokensOptions
            {
               Full  = new SemanticTokensFullOptions { Delta = true },
               Range = true,
               Legend = new SemanticTokensLegend
               {
                  TokenTypes     = SemanticTokensBuilder.TokenTypes,
                  TokenModifiers = SemanticTokensBuilder.TokenModifiers,
               },
            },
         },
      };
   }

   public void Initialized(InitializedParams? _ = null) { }

   // ── shutdown / exit ──────────────────────────────────────────────────────────

   public object Shutdown(object? _ = null) => null!;

   public void Exit(object? _ = null) => Rpc?.Dispose();

   // ── text document sync ───────────────────────────────────────────────────────

   public void OnDidOpen(DidOpenTextDocumentParams p)
   {
      _store.Update(p.TextDocument.Uri.ToString(), p.TextDocument.Text);
      _ = PublishDiagnosticsAsync(p.TextDocument.Uri);
   }

   public void OnDidChange(DidChangeTextDocumentParams p)
   {
      var uri     = p.TextDocument.Uri.ToString();
      var changes = p.ContentChanges ?? [];
      // Apply incremental changes to build the new document text.
      var newText = changes.Aggregate(_store.GetText(uri) ?? string.Empty, ApplyChange);
      _store.Update(uri, newText);
      _ = PublishDiagnosticsAsync(p.TextDocument.Uri);
   }

   public void OnDidSave(DidSaveTextDocumentParams? _ = null) { }

   public void OnDidClose(DidCloseTextDocumentParams p)
   {
      _store.Remove(p.TextDocument.Uri.ToString());
      _ = Rpc?.NotifyWithParameterObjectAsync(Methods.TextDocumentPublishDiagnosticsName,
         new PublishDiagnosticParams { Uri = p.TextDocument.Uri, Diagnostics = [] });
   }

   // ── completion → see CgScriptLanguageTarget.Completion.cs ──────────────────
   // ── hover      → see CgScriptLanguageTarget.Hover.cs ────────────────────────
   // ── signature  → see CgScriptLanguageTarget.SignatureHelp.cs ─────────────────
   // ── navigation → see CgScriptLanguageTarget.Navigation.cs ───────────────────
   // ── semantic tokens → see CgScriptLanguageTarget.SemanticTokens.cs ──────────
   // ── doc features    → see CgScriptLanguageTarget.DocumentFeatures.cs ─────────
   // ── helpers    → see CgScriptLanguageTarget.Helpers.cs ───────────────────────

   // ── diagnostics (server-push notification) ────────────────────────────────────
   private Task PublishDiagnosticsAsync(Uri uri)
   {
      if (Rpc is null) return Task.CompletedTask;

      var result = _store.GetParseResult(uri.ToString());
      if (result is null) return Task.CompletedTask;

      var diags = result.Diagnostics.Select(d => new LspDiagnostic
         {
            Severity = d.Severity == Parsing.DiagnosticSeverity.Error
               ? Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Error
               : Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Warning,
            Code    = string.IsNullOrEmpty(d.Code) ? default : new SumType<int, string>(d.Code),
            Message = d.Message,
            Range   = new LspRange
            {
               Start = new Position(d.Line - 1, d.Column),
               End   = new Position(d.Line - 1, d.Column + d.Length),
            },
         Source = "cgscript",
         }).ToArray();

      // ── CGS014: warn on empty type="" attributes in XML doc comments ──────────────
      var text = _store.GetText(uri.ToString());
      if (text is not null)
      {
         var xmlDocDiags = new List<LspDiagnostic>();
         var lines       = text.Split('\n');
         for (int i = 0; i < lines.Length; i++)
         {
            var lineText = lines[i];
            if (!lineText.TrimStart().StartsWith("///")) continue;

            int col = lineText.IndexOf("type=\"\"", StringComparison.Ordinal);
            if (col < 0) continue;

            xmlDocDiags.Add(new LspDiagnostic
            {
               Severity = Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Warning,
               Code     = new SumType<int, string>("CGS014"),
               Message  = "XML doc comment 'type' attribute should not be empty - specify the parameter type (for example 'number', 'string', 'array')",
               Range    = new LspRange
               {
                  Start = new Position(i, col),
                  End   = new Position(i, col + "type=\"\"".Length),
               },
               Source = "cgscript",
            });
         }
         if (xmlDocDiags.Count > 0)
            diags = diags.Concat(xmlDocDiags).ToArray();
      }

      return Rpc.NotifyWithParameterObjectAsync(Methods.TextDocumentPublishDiagnosticsName,
         new PublishDiagnosticParams { Uri = uri, Diagnostics = diags });
   }
}
