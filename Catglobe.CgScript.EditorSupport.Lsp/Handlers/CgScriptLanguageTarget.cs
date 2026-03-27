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

   // ── Enum constant lookup (lazily built once) ──────────────────────────────────
   private Dictionary<string, (EnumDefinition Enum, EnumValueDefinition Value)>? _enumByConstant;

   private Dictionary<string, (EnumDefinition Enum, EnumValueDefinition Value)> EnumByConstant
      => _enumByConstant ??= _definitions.Enums.Values
            .SelectMany(e => e.Values.Select(v => (Key: v.Name, Enum: e, Value: v)))
            .ToDictionary(x => x.Key, x => (x.Enum, x.Value), StringComparer.OrdinalIgnoreCase);

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
      try
      {
         _store.Update(p.TextDocument.Uri.ToString(), p.TextDocument.Text);
      }
      catch (Exception ex)
      {
         _ = PublishErrorDiagnosticAsync(p.TextDocument.Uri, ex);
         return;
      }
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
   private async Task PublishDiagnosticsAsync(Uri uri)
   {
      if (Rpc is null) return;

      // Each stage is isolated: a failure in one does not discard results from others.
      var allDiags = new List<LspDiagnostic>();
      bool fatalError = false;
      Exception? fatalException = null;

      // Stage 0: surface a persistent error if the live definition fetch failed to parse
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

      // Stage 1: convert stored parse+semantic diagnostics to LSP format
      try
      {
         var result = _store.GetParseResult(uri.ToString());
         if (result is null) return;

         allDiags.AddRange(result.Diagnostics.Select(d => new LspDiagnostic
         {
            Severity = d.Severity == Parsing.DiagnosticSeverity.Error
               ? Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Error
               : d.Severity == Parsing.DiagnosticSeverity.Information
               ? Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Information
               : Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Warning,
            Code    = string.IsNullOrEmpty(d.Code) ? default : new SumType<int, string>(d.Code),
            Message = d.Message,
            Range   = new LspRange
            {
               Start = new Position(d.Line - 1, d.Column),
               End   = new Position(d.Line - 1, d.Column + d.Length),
            },
            Source = "cgscript",
         }));
      }
      catch (Exception ex)
      {
         fatalError     = true;
         fatalException = ex;
      }

      if (!fatalError)
      {
         // Stage 2: CGS014 — warn on empty type="" attributes in XML doc comments
         try
         {
            var text = _store.GetText(uri.ToString());
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
            // XML-doc scanning failed — add an error diagnostic but keep all other results.
            allDiags.Add(new LspDiagnostic
            {
               Severity = Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Error,
               Code     = new SumType<int, string>("CGS000"),
               Message  = $"Internal LSP error (XML doc scan): {ex.Message}",
               Range    = new LspRange { Start = new Position(0, 0), End = new Position(0, 0) },
               Source   = "cgscript",
            });
         }
      }
      else
      {
         // Stage 1 failed entirely — surface only the error so the user is not left with stale diagnostics.
         allDiags.Add(new LspDiagnostic
         {
            Severity = Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Error,
            Code     = new SumType<int, string>("CGS000"),
            Message  = $"Internal LSP error: {fatalException!.Message}",
            Range    = new LspRange { Start = new Position(0, 0), End = new Position(0, 0) },
            Source   = "cgscript",
         });
      }

      // Stage 3: send the notification (with whatever diagnostics we managed to collect)
      try
      {
         await Rpc.NotifyWithParameterObjectAsync(Methods.TextDocumentPublishDiagnosticsName,
            new PublishDiagnosticParams { Uri = uri, Diagnostics = allDiags.ToArray() });
      }
      catch
      {
         // If the RPC send itself fails, swallow silently to avoid terminating the connection.
      }
   }

   /// <summary>
   /// Publishes a single CGS000 error diagnostic for <paramref name="uri"/> when an unexpected
   /// exception occurs outside the normal parse/analyze pipeline (e.g. during text application).
   /// </summary>
   private Task PublishErrorDiagnosticAsync(Uri uri, Exception ex)
   {
      if (Rpc is null) return Task.CompletedTask;
      return Rpc.NotifyWithParameterObjectAsync(Methods.TextDocumentPublishDiagnosticsName,
         new PublishDiagnosticParams
         {
            Uri = uri,
            Diagnostics =
            [
               new LspDiagnostic
               {
                  Severity = Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Error,
                  Code     = new SumType<int, string>("CGS000"),
                  Message  = $"Internal LSP error: {ex.Message}",
                  Range    = new LspRange { Start = new Position(0, 0), End = new Position(0, 0) },
                  Source   = "cgscript",
               },
            ],
         });
   }
}
