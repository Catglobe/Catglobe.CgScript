import { StateField, type Extension } from "@codemirror/state";
import { EditorView, type DecorationSet } from "@codemirror/view";
import { StreamLanguage } from "@codemirror/language";
import { LSPClient, languageServerExtensions, type Transport } from "@codemirror/lsp-client";
export type { Transport, Extension };
export { LSPClient, languageServerExtensions };
interface CgState {
    tokenize: ((stream: any, state: CgState) => string) | null;
    context: CgContext | null;
    indented: number;
    curPunc: string | null;
}
declare class CgContext {
    indented: number;
    column: number;
    type: string;
    align: boolean | null;
    prev: CgContext | null;
    constructor(indented: number, column: number, type: string, align: boolean | null, prev: CgContext | null);
}
export declare function createCgScriptLanguage(): {
    language: StreamLanguage<CgState>;
    keywordCompletions: {
        label: string;
        type: string;
    }[];
};
export interface SplitTransport {
    rawTransport: Transport;
    lspTransport: Transport;
    closed: Promise<CloseEvent>;
}
export declare function openTransport(uri: string): Promise<SplitTransport>;
export declare const semanticDecoField: StateField<DecorationSet>;
export declare const SEMANTIC_CLASSES: Array<string | null>;
export declare function makeSemanticTokensViewPlugin(transport: Transport, fileUri: string): Extension[];
export declare const signatureHintTheme: Extension;
export declare function resolveTheme(name: string): Extension[];
export interface CgScriptEditorOptions {
    /** Called (debounced) whenever the document changes. */
    onDocChange?: (text: string) => void;
    /** Debounce delay in ms for onDocChange. Default: 2000. */
    saveDelay?: number;
}
export declare class CodeMirrorForCgScript {
    #private;
    constructor(parent: Element, initialContent: string, selectedTheme: string, opts?: CgScriptEditorOptions);
    connectLsp(lspClient: LSPClient, transport: Transport, fileUri: string): void;
    disconnectLsp(): void;
    setTheme(name: string): void;
    getSelection(): string;
    loadContent(text: string): void;
    _setFullscreen(on: boolean): void;
    toggleFullscreen(): void;
    get view(): EditorView;
}
export declare function manageLspConnection(wsUri: string, cm: CodeMirrorForCgScript, fileUri?: string): Promise<never>;
