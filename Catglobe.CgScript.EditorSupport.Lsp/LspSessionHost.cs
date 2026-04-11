using Catglobe.CgScript.EditorSupport.Lsp.Handlers;
using Catglobe.CgScript.EditorSupport.Parsing;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;
using System.IO.Pipelines;
using System.Net.WebSockets;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace Catglobe.CgScript.EditorSupport.Lsp;

/// <summary>
/// Hosts an in-process LSP session over a WebSocket or a pipe pair.
/// The caller creates the <see cref="CgScriptLanguageTarget"/> (or a subclass) to inject custom definitions.
/// </summary>
public static class LspSessionHost
{
   public static async Task RunAsync(
      WebSocket               webSocket,
      CgScriptLanguageTarget  target,
      CancellationToken       cancellationToken = default)
   {
      var handler = new WebSocketMessageHandler(webSocket, new JsonMessageFormatter());
      using var rpc = new JsonRpc(handler);

      rpc.TraceSource.Switch.Level = System.Diagnostics.SourceLevels.Warning;
      rpc.TraceSource.Listeners.Clear();
      rpc.TraceSource.Listeners.Add(new System.Diagnostics.DefaultTraceListener());

      CgScriptDefinitions.TraceSource.Listeners.Add(new System.Diagnostics.DefaultTraceListener());

      target.Rpc = rpc;
      RegisterHandlers(rpc, target);
      rpc.StartListening();

      using var reg = cancellationToken.Register(() => rpc.Dispose());
      await rpc.Completion;
   }

   public static async Task RunAsync(
      IDuplexPipe             pipe,
      CgScriptLanguageTarget  target,
      CancellationToken       cancellationToken = default)
   {
      var handler = new HeaderDelimitedMessageHandler(pipe.Output.AsStream(), pipe.Input.AsStream(), new JsonMessageFormatter());
      using var rpc = new JsonRpc(handler);

      rpc.TraceSource.Switch.Level = System.Diagnostics.SourceLevels.Information;
      rpc.TraceSource.Listeners.Clear();
      rpc.TraceSource.Listeners.Add(new System.Diagnostics.DefaultTraceListener());

      CgScriptDefinitions.TraceSource.Listeners.Add(new System.Diagnostics.DefaultTraceListener());
      rpc.Disconnected += (_, e) =>
      {
         if (e.Exception != null)
            System.Diagnostics.Debug.WriteLine($"[CgScript LSP] Disconnected: {e.Reason}\n{e.Exception}");
      };

      target.Rpc = rpc;
      RegisterHandlers(rpc, target);
      rpc.StartListening();

      using var reg = cancellationToken.Register(() => rpc.Dispose());
      await rpc.Completion;
   }

   public static async Task RunQslAsync(
      WebSocket            webSocket,
      QslLanguageTarget    target,
      CancellationToken    cancellationToken = default)
   {
      var handler = new WebSocketMessageHandler(webSocket, new JsonMessageFormatter());
      using var rpc = new JsonRpc(handler);
      rpc.TraceSource.Switch.Level = System.Diagnostics.SourceLevels.Warning;
      rpc.TraceSource.Listeners.Clear();
      rpc.TraceSource.Listeners.Add(new System.Diagnostics.DefaultTraceListener());
      target.Rpc = rpc;
      RegisterQslHandlers(rpc, target);
      rpc.StartListening();
      using var reg = cancellationToken.Register(() => rpc.Dispose());
      await rpc.Completion;
   }

   public static async Task RunQslAsync(
      IDuplexPipe          pipe,
      QslLanguageTarget    target,
      CancellationToken    cancellationToken = default)
   {
      var handler = new HeaderDelimitedMessageHandler(pipe.Output.AsStream(), pipe.Input.AsStream(), new JsonMessageFormatter());
      using var rpc = new JsonRpc(handler);
      rpc.TraceSource.Switch.Level = System.Diagnostics.SourceLevels.Information;
      rpc.TraceSource.Listeners.Clear();
      rpc.TraceSource.Listeners.Add(new System.Diagnostics.DefaultTraceListener());
      rpc.Disconnected += (_, e) =>
      {
         if (e.Exception != null)
            System.Diagnostics.Debug.WriteLine($"[QSL LSP] Disconnected: {e.Reason}\n{e.Exception}");
      };
      target.Rpc = rpc;
      RegisterQslHandlers(rpc, target);
      rpc.StartListening();
      using var reg = cancellationToken.Register(() => rpc.Dispose());
      await rpc.Completion;
   }

   private static void RegisterQslHandlers(JsonRpc rpc, QslLanguageTarget t)
   {
      rpc.AddLspHandler(Methods.Initialize,                   t.Initialize);
      rpc.AddLspNotification(Methods.Initialized,             t.Initialized);
      rpc.AddLspHandler(Methods.Shutdown,                     t.Shutdown);
      rpc.AddLspNotification(Methods.Exit,                    t.Exit);
      rpc.AddLspNotification(Methods.TextDocumentDidOpen,     t.OnDidOpen);
      rpc.AddLspNotification(Methods.TextDocumentDidChange,   t.OnDidChange);
      rpc.AddLspNotification(Methods.TextDocumentDidSave,     t.OnDidSave);
      rpc.AddLspNotification(Methods.TextDocumentDidClose,    t.OnDidClose);
      rpc.AddLspHandler(Methods.TextDocumentSemanticTokensFull,      t.OnSemanticTokensFull);
      rpc.AddLspHandler(Methods.TextDocumentSemanticTokensFullDelta,  t.OnSemanticTokensFullDelta);
      rpc.AddLspHandler(Methods.TextDocumentSemanticTokensRange,     t.OnSemanticTokensRange);
      rpc.AddLspHandler(Methods.TextDocumentHover,                   t.OnHover);
      rpc.AddLspHandler(Methods.TextDocumentDefinition,              t.OnDefinition);
      rpc.AddLspHandler(Methods.TextDocumentReferences,              t.OnReferences);
      rpc.AddLspHandler("textDocument/prepareRename",                (Func<TextDocumentPositionParams, LspRange?>)t.OnPrepareRename);
      rpc.AddLspHandler(Methods.TextDocumentRename,                  t.OnRename);
      rpc.AddLspHandler(Methods.TextDocumentDocumentHighlight,       t.OnDocumentHighlight);
      rpc.AddLspHandler(Methods.TextDocumentDocumentSymbol,          t.OnDocumentSymbol);
      rpc.AddLspHandler(Methods.TextDocumentCompletion,              t.OnCompletion);
      rpc.AddLspHandler(Methods.TextDocumentFoldingRange,            t.OnFoldingRange);
   }

   private static void RegisterHandlers(JsonRpc rpc, CgScriptLanguageTarget t)
   {
      rpc.AddLspHandler(Methods.Initialize,                  t.Initialize);
      rpc.AddLspNotification(Methods.Initialized,            t.Initialized);
      rpc.AddLspHandler(Methods.Shutdown,      t.Shutdown);
      rpc.AddLspNotification(Methods.Exit,     t.Exit);
      rpc.AddLspNotification(Methods.WorkspaceDidChangeConfiguration, t.OnDidChangeConfiguration);
      rpc.AddLspNotification(Methods.TextDocumentDidOpen,    t.OnDidOpen);
      rpc.AddLspNotification(Methods.TextDocumentDidChange,  t.OnDidChange);
      rpc.AddLspNotification(Methods.TextDocumentDidSave,    t.OnDidSave);
      rpc.AddLspNotification(Methods.TextDocumentDidClose,   t.OnDidClose);
      rpc.AddLspHandler(Methods.TextDocumentCompletion,             t.OnCompletion);
      rpc.AddLspHandler(Methods.TextDocumentHover,                  t.OnHover);
      rpc.AddLspHandler(Methods.TextDocumentSignatureHelp,          t.OnSignatureHelp);
      rpc.AddLspHandler(Methods.TextDocumentSemanticTokensFull,     t.OnSemanticTokensFull);
      rpc.AddLspHandler(Methods.TextDocumentSemanticTokensFullDelta, t.OnSemanticTokensFullDelta);
      rpc.AddLspHandler(Methods.TextDocumentSemanticTokensRange,    t.OnSemanticTokensRange);
      rpc.AddLspHandler(Methods.TextDocumentDefinition,             t.OnDefinition);
      rpc.AddLspHandler(Methods.TextDocumentReferences,             t.OnReferences);
      rpc.AddLspHandler("textDocument/prepareRename",               (Func<TextDocumentPositionParams, LspRange?>)t.OnPrepareRename);
      rpc.AddLspHandler(Methods.TextDocumentRename,                 t.OnRename);
      rpc.AddLspHandler(Methods.TextDocumentDocumentHighlight,      t.OnDocumentHighlight);
      rpc.AddLspHandler(Methods.TextDocumentDocumentSymbol,         t.OnDocumentSymbol);
      rpc.AddLspHandler(Methods.TextDocumentFoldingRange,           t.OnFoldingRange);
      rpc.AddLspHandler("textDocument/declaration",
         (Func<TextDocumentPositionParams, SumType<Location, Location[]>?>)t.OnDeclaration);
      rpc.AddLspHandler("textDocument/selectionRange",
         (Func<SelectionRangeParams, CgSelectionRange[]?>)t.OnSelectionRange);
      rpc.AddLspHandler(Methods.TextDocumentCodeAction,             t.OnCodeAction);
      rpc.AddLspHandler(Methods.TextDocumentFormatting,            t.OnDocumentFormatting);
   }
}

/// <summary>
/// Extension methods that register typed LSP handlers on a <see cref="JsonRpc"/> instance.
/// Using <see cref="LspRequest{TParams,TResult}"/> / <see cref="LspNotification{TParams}"/> ensures
/// the method name and param/result types are always in sync with the protocol library's definitions.
/// <c>UseSingleObjectParameterDeserialization</c> is set automatically so the whole JSON params object
/// is deserialized into <typeparamref name="TParams"/> rather than matched key-by-key.
/// </summary>
file static class JsonRpcLspExtensions
{
   public static void AddLspHandler<TParams, TResult>(
      this JsonRpc rpc,
      LspRequest<TParams, TResult> request,
      Func<TParams, TResult> handler)
      => rpc.AddLocalRpcMethod(
            handler.Method, handler.Target,
            new JsonRpcMethodAttribute(request.Name) { UseSingleObjectParameterDeserialization = true });

   /// <summary>
   /// Registers a handler for LSP methods not yet exposed as typed
   /// <see cref="LspRequest{TParams,TResult}"/> constants in the protocol library
   /// (e.g. <c>textDocument/prepareRename</c> in VS LSP 17.2.x).
   /// </summary>
   public static void AddLspHandler<TParams, TResult>(
      this JsonRpc rpc,
      string       methodName,
      Func<TParams, TResult> handler)
      => rpc.AddLocalRpcMethod(
            handler.Method, handler.Target,
            new JsonRpcMethodAttribute(methodName) { UseSingleObjectParameterDeserialization = true });

   public static void AddLspNotification<TParams>(
      this JsonRpc rpc,
      LspNotification<TParams> notification,
      Action<TParams> handler)
      => rpc.AddLocalRpcMethod(
            handler.Method, handler.Target,
            new JsonRpcMethodAttribute(notification.Name) { UseSingleObjectParameterDeserialization = true });
}


