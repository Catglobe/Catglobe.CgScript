import { LSPClient, languageServerExtensions, languageServerSupport, } from "@codemirror/lsp-client";
export { LSPClient };
/** LSP language identifier sent to the server. */
export const CGSCRIPT_LANGUAGE_ID = "cgscript";
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
export async function webSocketTransport(uri) {
    let handlers = [];
    const sock = new WebSocket(uri);
    sock.onmessage = e => { for (const h of handlers)
        h(e.data); };
    return new Promise((resolve, reject) => {
        sock.onopen = () => resolve({
            send(message) { sock.send(message); },
            subscribe(h) { handlers.push(h); },
            unsubscribe(h) { handlers = handlers.filter(x => x !== h); },
        });
        sock.onerror = () => reject(new Error(`WebSocket connection to ${uri} failed`));
    });
}
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
export function cgscriptLspClient(transport) {
    return new LSPClient({ extensions: languageServerExtensions() })
        .connect(transport);
}
/**
 * Returns a CodeMirror {@link Extension} that activates all LSP features
 * (completions, hover, diagnostics, semantic tokens, …) for one `.cgs` file.
 *
 * @param client      A connected LSP client from {@link cgscriptLspClient}.
 * @param documentUri The `file://` URI of the document being edited.
 */
export function cgscriptSupport(client, documentUri) {
    return languageServerSupport(client, documentUri, CGSCRIPT_LANGUAGE_ID);
}
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
export async function cgscript(serverUri) {
    const transport = await webSocketTransport(serverUri);
    const client = cgscriptLspClient(transport);
    return (documentUri) => cgscriptSupport(client, documentUri);
}
