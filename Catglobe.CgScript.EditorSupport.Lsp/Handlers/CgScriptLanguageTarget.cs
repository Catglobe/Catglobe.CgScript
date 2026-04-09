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

/// <summary>
/// Handles all LSP requests/notifications from the client.
/// Methods are dispatched by StreamJsonRpc via [JsonRpcMethod] attributes.
/// StreamJsonRpc deserialises parameters using the <see cref="LspSessionHost"/>-configured formatter,
/// which handles the <c>IProgress&lt;T&gt;</c> progress-token properties on LSP types.
/// </summary>
public partial class CgScriptLanguageTarget
{
   protected readonly DocumentStore    _store;
   protected readonly CgScriptDefinitions _definitions;

   // Set after construction to break the circular dependency (JsonRpc needs target, target needs JsonRpc).
   public JsonRpc? Rpc { get; set; }

   // ── Semantic tokens delta cache ───────────────────────────────────────────────
   private readonly ConcurrentDictionary<string, (int[] Data, string ResultId)> _semanticCache = new();
   private int _semanticResultIdCounter;

   // ── Client capability flags (set during Initialize) ───────────────────────────
   private bool _clientSupportsMarkdownHover;
   private bool _clientSupportsSnippets;

   public CgScriptLanguageTarget(DocumentStore store, CgScriptDefinitions definitions)
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
               TriggerCharacters = [".", "/", "["],
            },
            HoverProvider              = true,
            SignatureHelpProvider      = new SignatureHelpOptions { TriggerCharacters = ["(", ",", "["] },
            DefinitionProvider         = new SumType<bool, DefinitionOptions>(true),
            ReferencesProvider         = new SumType<bool, ReferenceOptions>(true),
            RenameProvider             = new SumType<bool, RenameOptions>(new RenameOptions { PrepareProvider = true }),
            DocumentHighlightProvider  = new SumType<bool, DocumentHighlightOptions>(true),
            DocumentSymbolProvider     = new SumType<bool, DocumentSymbolOptions>(true),
            FoldingRangeProvider       = new SumType<bool, FoldingRangeOptions>(true),
            CodeActionProvider         = new SumType<bool, CodeActionOptions>(true),
            DocumentFormattingProvider = true,
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
      var uri = p.TextDocument.Uri.ToString();
      try
      {
         _store.Update(uri, p.TextDocument.Text);
      }
      catch (Exception ex)
      {
         _ = PublishErrorDiagnosticAsync(p.TextDocument.Uri, ex);
         return;
      }
      // Clear semantic cache to force full semantic tokens request on first access
      _semanticCache.TryRemove(uri, out _);
      _ = PublishDiagnosticsAsync(p.TextDocument.Uri);
   }

   public void OnDidChange(DidChangeTextDocumentParams p)
   {
      var uri     = p.TextDocument.Uri.ToString();
      var changes = p.ContentChanges ?? [];
      string newText;
      try
      {
         // Apply incremental changes to build the new document text.
         newText = changes.Aggregate(_store.GetText(uri) ?? string.Empty, ApplyChange);
      }
      catch (Exception ex)
      {
         _ = PublishErrorDiagnosticAsync(p.TextDocument.Uri, ex);
         return;
      }
      _store.Update(uri, newText);
      _ = PublishDiagnosticsAsync(p.TextDocument.Uri);
   }

   public void OnDidSave(DidSaveTextDocumentParams? _ = null) { }

   public void OnDidClose(DidCloseTextDocumentParams p)
   {
      var uri = p.TextDocument.Uri.ToString();
      _store.Remove(uri);
      _semanticCache.TryRemove(uri, out _);
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

   private async Task PublishDiagnosticsAsync(Uri uri)
   {
      if (Rpc is null) return;

      var uriStr   = uri.ToString();
      var allDiags = new List<LspDiagnostic>();
      bool fatalError = false;
      Exception? fatalException = null;

      // Stage 0: surface a persistent load error from the live definition fetch.
      if (_definitions.LoadError is { } loadError)
      {
         allDiags.Add(new LspDiagnostic
         {
            Severity = Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Error,
            Code     = new SumType<int, string>("CGS027"),
            Message  = loadError,
            Range    = new LspRange { Start = new Position(0, 0), End = new Position(0, 0) },
            Source   = "cgscript",
         });
      }

      // Stage 1: convert stored parse+semantic diagnostics to LSP format.
      try
      {
         var result = _store.GetParseResult(uriStr);
         if (result is null) return;
         allDiags.AddRange(result.Diagnostics.Select(d => LspHelpers.DiagnosticToLsp(d, "cgscript")));
      }
      catch (Exception ex)
      {
         fatalError     = true;
         fatalException = ex;
      }

      if (!fatalError)
      {
         // Stage 2: CGS014 — warn on empty type="" attributes in XML doc comments.
         try
         {
            var text = _store.GetText(uriStr);
            if (text is not null)
            {
               var lines = text.Split('\n');
               for (int i = 0; i < lines.Length; i++)
               {
                  var lineText = lines[i];
                  if (!lineText.TrimStart().StartsWith("///")) continue;

                  int col = lineText.IndexOf("type=\"\"", StringComparison.Ordinal);
                  if (col < 0) continue;

                  allDiags.Add(new LspDiagnostic
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
            }
         }
         catch (Exception ex)
         {
            allDiags.Add(LspHelpers.MakeInternalErrorDiag("CGS000", $"Internal LSP error (XML doc scan): {ex.Message}", "cgscript"));
         }
      }
      else
      {
         allDiags.Add(LspHelpers.MakeInternalErrorDiag("CGS000", $"Internal LSP error: {fatalException!.Message}", "cgscript"));
      }

      try
      {
         await Rpc.NotifyWithParameterObjectAsync(Methods.TextDocumentPublishDiagnosticsName,
            new PublishDiagnosticParams { Uri = uri, Diagnostics = allDiags.ToArray() });
      }
      catch { }
   }

   private Task PublishErrorDiagnosticAsync(Uri uri, Exception ex)
   {
      if (Rpc is null) return Task.CompletedTask;
      return Rpc.NotifyWithParameterObjectAsync(Methods.TextDocumentPublishDiagnosticsName,
         new PublishDiagnosticParams
         {
            Uri = uri,
            Diagnostics = [LspHelpers.MakeInternalErrorDiag("CGS000", $"Internal LSP error: {ex.Message}", "cgscript")],
         });
   }
}
