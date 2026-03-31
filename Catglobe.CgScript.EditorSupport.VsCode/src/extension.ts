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

   client = createClient(context, serverExe);
   await client.start();
   context.subscriptions.push({ dispose: () => client?.dispose() });

   context.subscriptions.push(
      workspace.onDidChangeConfiguration(async e => {
         if (e.affectsConfiguration('cgscript.siteUrl')) {
            await client?.restart();
         }
      })
   );
}

export async function deactivate(): Promise<void> {
   await client?.dispose();
   client = undefined;
}

function createClient(context: ExtensionContext, serverExe: string): LanguageClient {
   const siteUrl   = workspace.getConfiguration('cgscript').get<string>('siteUrl', '').trim();
   const extraArgs = siteUrl ? ['--site', siteUrl] : [];

   const serverOptions: ServerOptions = {
      run:   { command: 'dotnet', args: [serverExe, ...extraArgs], transport: TransportKind.stdio },
      debug: { command: 'dotnet', args: [serverExe, ...extraArgs], transport: TransportKind.stdio },
   };

   const clientOptions: LanguageClientOptions = {
      documentSelector: [{ scheme: 'file', language: 'cgscript' }],
      synchronize: {
         fileEvents: workspace.createFileSystemWatcher('**/*.cgs'),
      },
      outputChannelName: 'CgScript Language Server',
   };

   return new LanguageClient(
      'cgscript',
      'CgScript Language Server',
      serverOptions,
      clientOptions
   );
}

/** Resolves the framework-dependent server entry point. Requires dotnet on PATH. */
function resolveServerExe(context: ExtensionContext): string | undefined {
   const dll = path.join(context.extensionPath, 'server', 'Catglobe.CgScript.EditorSupport.Lsp.Server.dll');
   return fs.existsSync(dll) ? dll : undefined;
}
