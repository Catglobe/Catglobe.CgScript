using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Catglobe.CgScript.EditorSupport.Lsp;
using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
using Catglobe.CgScript.EditorSupport.Lsp.Handlers;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Editor;
using Microsoft.VisualStudio.Extensibility.LanguageServer;
using Microsoft.VisualStudio.RpcContracts.LanguageServerProvider;
namespace Catglobe.CgScript.EditorSupport.VisualStudio;

#pragma warning disable VSEXTPREVIEW_LSP // API is in preview

/// <summary>
/// Provides an in-process CgScript LSP session to Visual Studio.
/// A pipe pair is created on each activation; <see cref="LspSessionHost"/> runs the server
/// side in a background task, and the client side is returned to VS as an <see cref="IDuplexPipe"/>.
/// </summary>
[VisualStudioContribution]
public sealed class CgScriptLanguageServerProvider : LanguageServerProvider
{
   /// <summary>Document type for <c>.cgs</c> files — required by the LSP activation filter.</summary>
   [VisualStudioContribution]
   public static DocumentTypeConfiguration CgScriptDocumentType => new("cgscript")
   {
      FileExtensions = [".cgs"],
      BaseDocumentType = LanguageServerBaseDocumentType,
   };

   /// <inheritdoc/>
   public override LanguageServerProviderConfiguration LanguageServerProviderConfiguration =>
      new("%Catglobe.CgScript.EditorSupport.VisualStudio.CgScriptLanguageServerProvider.DisplayName%",
          [DocumentFilter.FromDocumentType(CgScriptDocumentType)]);

   /// <inheritdoc/>
   public override Task<IDuplexPipe?> CreateServerConnectionAsync(CancellationToken cancellationToken)
   {
      // Two pipes: VS writes to clientToServer; server reads from it (and vice versa).
      var clientToServer = new Pipe();
      var serverToClient = new Pipe();

      var serverSide = new InProcessDuplexPipe(clientToServer.Reader, serverToClient.Writer);
      var clientSide = new InProcessDuplexPipe(serverToClient.Reader, clientToServer.Writer);

      var definitions = new DefinitionLoader();
      var target = new CgScriptLanguageTarget(new DocumentStore(definitions), definitions);
      _ = LspSessionHost.RunAsync(serverSide, target, cancellationToken);

      return Task.FromResult<IDuplexPipe?>(clientSide);
   }

   /// <inheritdoc/>
   public override Task OnServerInitializationResultAsync(
      ServerInitializationResult serverInitializationResult,
      LanguageServerInitializationFailureInfo? initializationFailureInfo,
      CancellationToken cancellationToken)
   {
      if (serverInitializationResult == ServerInitializationResult.Failed)
         Enabled = false;

      return base.OnServerInitializationResultAsync(serverInitializationResult, initializationFailureInfo, cancellationToken);
   }
}

#pragma warning restore VSEXTPREVIEW_LSP

/// <summary>Minimal <see cref="IDuplexPipe"/> wrapper around a <see cref="PipeReader"/>/<see cref="PipeWriter"/> pair.</summary>
file sealed class InProcessDuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe
{
   public PipeReader Input  => input;
   public PipeWriter Output => output;
}
