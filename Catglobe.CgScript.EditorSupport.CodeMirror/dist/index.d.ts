import { LSPClient } from "@codemirror/lsp-client";
import type { Transport } from "@codemirror/lsp-client";
import type { Extension } from "@codemirror/state";
export type { Transport, Extension };
export { LSPClient };
/** LSP language identifier sent to the server. */
export declare const CGSCRIPT_LANGUAGE_ID = "cgscript";
/**
 * Creates a WebSocket-based {@link Transport} for the CgScript LSP server.
 *
 * The LSP server speaks JSON-RPC over stdin/stdout; a lightweight WebSocket
 * proxy must run on the backend to bridge the connection. Example using
 * [ws-proxy](https://www.npmjs.com/package/ws-proxy):
 *
 * ```sh
 * ws-proxy --port 4040 -- Catglobe.CgScript.EditorSupport.Lsp
 * ```
 *
 * @param uri  Full WebSocket URI, e.g. `"ws://localhost:4040"`
 */
export declare function webSocketTransport(uri: string): Promise<Transport>;
/**
 * Creates an {@link LSPClient} connected to the CgScript language server.
 *
 * ```ts
 * const transport = await webSocketTransport("ws://localhost:4040");
 * const client = cgscriptLspClient(transport);
 *
 * new EditorView({
 *   extensions: [
 *     basicSetup,
 *     cgscriptSupport(client, "file:///workspace/myScript.cgs"),
 *   ],
 *   parent: document.body,
 * });
 * ```
 */
export declare function cgscriptLspClient(transport: Transport): LSPClient;
/**
 * Returns a CodeMirror {@link Extension} that activates all LSP features
 * (completions, hover, diagnostics, semantic tokens, …) for one `.cgs` file.
 *
 * @param client      A connected LSP client from {@link cgscriptLspClient}.
 * @param documentUri The `file://` URI of the document being edited.
 */
export declare function cgscriptSupport(client: LSPClient, documentUri: string): Extension;
/**
 * One-shot helper: creates the transport, connects, and returns a factory
 * function that produces per-editor extensions.
 *
 * ```ts
 * const support = await cgscript("ws://localhost:4040");
 *
 * new EditorView({
 *   extensions: [basicSetup, support("file:///workspace/myScript.cgs")],
 *   parent: document.body,
 * });
 * ```
 */
export declare function cgscript(serverUri: string): Promise<(documentUri: string) => Extension>;
