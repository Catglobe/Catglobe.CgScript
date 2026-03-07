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
      run:   { command: serverExe, transport: TransportKind.stdio },
      debug: { command: serverExe, transport: TransportKind.stdio },
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

/** Resolves the platform-specific self-contained server binary. */
function resolveServerExe(context: ExtensionContext): string | undefined {
   const platform = process.platform; // 'win32' | 'linux' | 'darwin'
   const arch     = process.arch;     // 'x64' | 'arm64'

   const rid = platform === 'win32'  ? `win-${arch}` :
               platform === 'linux'  ? `linux-${arch}` :
               platform === 'darwin' ? `osx-${arch}` : undefined;

   if (!rid) return undefined;

   const exeName = platform === 'win32'
      ? 'Catglobe.CgScript.EditorSupport.Lsp.Server.exe'
      : 'Catglobe.CgScript.EditorSupport.Lsp.Server';

   const candidate = path.join(context.extensionPath, 'server', rid, exeName);
   return fs.existsSync(candidate) ? candidate : undefined;
}
