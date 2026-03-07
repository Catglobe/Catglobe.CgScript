import * as path from 'path';
import * as fs from 'fs';
import { ExtensionContext, workspace } from 'vscode';
import {
   LanguageClient,
   LanguageClientOptions,
   ServerOptions,
   TransportKind,
} from 'vscode-languageclient/node';

let client: LanguageClient | undefined;

export async function activate(context: ExtensionContext): Promise<void> {
   const serverExe = resolveServerExe(context);
   if (!serverExe) {
      console.error('[CgScript] LSP server executable not found.');
      return;
   }

   const serverOptions: ServerOptions = {
      run:   { command: 'dotnet', args: [serverExe], transport: TransportKind.stdio },
      debug: { command: 'dotnet', args: [serverExe], transport: TransportKind.stdio },
   };

   const clientOptions: LanguageClientOptions = {
      documentSelector: [{ scheme: 'file', language: 'cgscript' }],
      synchronize: {
         fileEvents: workspace.createFileSystemWatcher('**/*.cgs'),
      },
      outputChannelName: 'CgScript Language Server',
   };

   client = new LanguageClient(
      'cgscript',
      'CgScript Language Server',
      serverOptions,
      clientOptions
   );

   await client.start();
   context.subscriptions.push({ dispose: () => client?.dispose() });
}

export async function deactivate(): Promise<void> {
   await client?.dispose();
   client = undefined;
}

/** Resolves the framework-dependent server entry point. Requires dotnet on PATH. */
function resolveServerExe(context: ExtensionContext): string | undefined {
   const dll = path.join(context.extensionPath, 'server', 'Catglobe.CgScript.EditorSupport.Lsp.Server.dll');
   return fs.existsSync(dll) ? dll : undefined;
}
