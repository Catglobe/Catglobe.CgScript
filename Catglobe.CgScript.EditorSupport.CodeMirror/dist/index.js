var __classPrivateFieldGet = (this && this.__classPrivateFieldGet) || function (receiver, state, kind, f) {
    if (kind === "a" && !f) throw new TypeError("Private accessor was defined without a getter");
    if (typeof state === "function" ? receiver !== state || !f : !state.has(receiver)) throw new TypeError("Cannot read private member from an object whose class did not declare it");
    return kind === "m" ? f : kind === "a" ? f.call(receiver) : f ? f.value : state.get(receiver);
};
var __classPrivateFieldSet = (this && this.__classPrivateFieldSet) || function (receiver, state, value, kind, f) {
    if (kind === "m") throw new TypeError("Private method is not writable");
    if (kind === "a" && !f) throw new TypeError("Private accessor was defined without a setter");
    if (typeof state === "function" ? receiver !== state || !f : !state.has(receiver)) throw new TypeError("Cannot write private member to an object whose class did not declare it");
    return (kind === "a" ? f.call(receiver, value) : f ? f.value = value : state.set(receiver, value)), value;
};
var _CodeMirrorForCgScript_view, _CodeMirrorForCgScript_themeCompartment, _CodeMirrorForCgScript_lspCompartment, _CodeMirrorForCgScript_saveDraftTimer;
import { EditorState, StateEffect, StateField, Compartment } from "@codemirror/state";
import { EditorView, ViewPlugin, lineNumbers, highlightActiveLineGutter, highlightSpecialChars, drawSelection, dropCursor, rectangularSelection, crosshairCursor, Decoration, highlightActiveLine, keymap, } from "@codemirror/view";
import { foldGutter, indentOnInput, syntaxHighlighting, HighlightStyle, bracketMatching, foldKeymap, StreamLanguage, } from "@codemirror/language";
import { history, defaultKeymap, historyKeymap, indentWithTab } from "@codemirror/commands";
import { highlightSelectionMatches, searchKeymap, openSearchPanel } from "@codemirror/search";
import { autocompletion, completionKeymap, completeFromList, closeBrackets, closeBracketsKeymap, } from "@codemirror/autocomplete";
import { tags } from "@lezer/highlight";
import { LSPClient, languageServerExtensions, languageServerSupport, } from "@codemirror/lsp-client";
export { LSPClient, languageServerExtensions };
class CgContext {
    constructor(indented, column, type, align, prev) {
        this.indented = indented;
        this.column = column;
        this.type = type;
        this.align = align;
        this.prev = prev;
    }
}
export function createCgScriptLanguage() {
    const blockkw = new Set("catch else for if try while function switch case".split(" "));
    const keywords = new Set([..."break continue return where new throw".split(" "), ...blockkw]);
    const types = new Set("object bool number string array question".split(" "));
    const atoms = new Set(["true", "false"]);
    const nulls = new Set(["empty"]);
    const keywordCompletions = [
        ...[...keywords].map(w => ({ label: w, type: "keyword" })),
        ...[...types].map(w => ({ label: w, type: "keyword" })),
        ...[...atoms].map(w => ({ label: w, type: "keyword" })),
        ...[...nulls].map(w => ({ label: w, type: "keyword" })),
    ];
    const isOperatorChar = /[+\-*&%=<>!?|/]/;
    function tokenBase(stream, state) {
        const ch = stream.next();
        if (ch === '"') {
            state.tokenize = tokenString(ch);
            return state.tokenize(stream, state);
        }
        if (/[[\]{}()]/.test(ch)) {
            state.curPunc = ch;
            return "bracket";
        }
        if (/[[\]{}(),;:.]/.test(ch)) {
            state.curPunc = ch;
            return "";
        }
        if (/\d/.test(ch)) {
            stream.eatWhile(/[\w.]/);
            return "number";
        }
        if (ch === "/") {
            if (stream.eat("*")) {
                state.tokenize = tokenComment;
                return tokenComment(stream, state);
            }
            if (stream.eat("/")) {
                stream.skipToEnd();
                return "comment";
            }
        }
        if (isOperatorChar.test(ch)) {
            stream.eatWhile(isOperatorChar);
            return "operator";
        }
        stream.eatWhile(/[\w$_\xa1-\uffff]/);
        const cur = stream.current();
        if (keywords.has(cur)) {
            if (blockkw.has(cur))
                state.curPunc = "newstatement";
            return "keyword";
        }
        if (types.has(cur))
            return "cgType";
        if (nulls.has(cur))
            return "cgNull";
        if (atoms.has(cur))
            return "cgAtom";
        return "variable";
    }
    function tokenString(quote) {
        return function (stream, state) {
            let escaped = false, next;
            while ((next = stream.next()) != null) {
                if (next === quote && !escaped) {
                    state.tokenize = null;
                    break;
                }
                escaped = !escaped && next === "\\";
            }
            return "string";
        };
    }
    function tokenComment(stream, state) {
        let maybeEnd = false, ch;
        while ((ch = stream.next())) {
            if (ch === "/" && maybeEnd) {
                state.tokenize = null;
                break;
            }
            maybeEnd = (ch === "*");
        }
        return "comment";
    }
    function pushContext(state, col, type) {
        const indent = state.context?.type === "statement" ? state.context.indented : state.indented;
        return (state.context = new CgContext(indent, col, type, null, state.context));
    }
    function popContext(state) {
        if (!state.context)
            return null;
        const t = state.context.type;
        if (t === ")" || t === "]" || t === "}")
            state.indented = state.context.indented;
        return (state.context = state.context.prev);
    }
    const cgscriptParser = {
        name: "cgscript",
        tokenTable: {
            keyword: tags.keyword,
            cgType: tags.typeName,
            cgAtom: tags.bool,
            cgNull: tags.null,
            number: tags.number,
            string: tags.string,
            comment: tags.comment,
            operator: tags.operator,
            bracket: tags.bracket,
        },
        startState(_baseIndent) {
            return {
                tokenize: null,
                context: new CgContext(-4, 0, "top", false, null),
                indented: 0,
                curPunc: null,
            };
        },
        copyState(state) {
            return { tokenize: state.tokenize, context: state.context, indented: state.indented, curPunc: null };
        },
        token(stream, state) {
            const ctx = state.context;
            if (stream.sol()) {
                if (ctx && ctx.align == null)
                    ctx.align = false;
                state.indented = stream.indentation();
            }
            if (stream.eatSpace())
                return null;
            state.curPunc = null;
            const style = (state.tokenize ?? tokenBase)(stream, state);
            if (style === "comment")
                return style;
            if (ctx && ctx.align == null)
                ctx.align = true;
            const p = state.curPunc;
            if ((p === ";" || p === ":" || p === ",") && state.context?.type === "statement") {
                popContext(state);
            }
            else if (p === "{") {
                pushContext(state, stream.column(), "}");
            }
            else if (p === "[") {
                pushContext(state, stream.column(), "]");
            }
            else if (p === "(") {
                pushContext(state, stream.column(), ")");
            }
            else if (p === "}") {
                let c = state.context;
                while (c && c.type === "statement")
                    c = popContext(state);
                if (c && c.type === "}")
                    c = popContext(state);
                while (c && c.type === "statement")
                    c = popContext(state);
            }
            else if (p === state.context?.type) {
                popContext(state);
            }
            else if (state.context && (state.context.type === "}" || state.context.type === "top") && p !== ";" ||
                state.context?.type === "statement" && p === "newstatement") {
                pushContext(state, stream.column(), "statement");
            }
            return style || null;
        },
        indent(state, textAfter) {
            if (state.tokenize != null)
                return null;
            let ctx = state.context;
            if (!ctx)
                return 0;
            const first = textAfter?.charAt(0);
            if (ctx.type === "statement" && first === "}")
                ctx = ctx.prev;
            if (!ctx)
                return 0;
            const closing = first === ctx.type;
            const unit = 4;
            if (ctx.type === "statement")
                return ctx.indented + (first === "{" ? 0 : unit);
            else if (ctx.align)
                return ctx.column + (closing ? 0 : 1);
            else if (ctx.type === ")" && !closing)
                return ctx.indented + unit;
            else
                return ctx.indented + (closing ? 0 : unit);
        },
        languageData: {
            commentTokens: { line: "//", block: { open: "/*", close: "*/" } },
            indentOnInput: /^\s*[{}]$/,
            closeBrackets: { brackets: ["(", "[", "{", '"'] },
        },
    };
    const language = StreamLanguage.define(cgscriptParser);
    return { language, keywordCompletions };
}
export async function openTransport(uri) {
    const sock = new WebSocket(uri);
    let rawHandlers = [];
    let lspHandlers = [];
    sock.onmessage = e => {
        const msg = e.data;
        for (const h of rawHandlers)
            h(msg);
        try {
            const { id } = JSON.parse(msg);
            if (typeof id === "string" && id.startsWith("sem-"))
                return;
        }
        catch { /* non-JSON — pass through */ }
        for (const h of lspHandlers)
            h(msg);
    };
    const closed = new Promise(resolve => { sock.onclose = resolve; });
    const sendMsg = (message) => { if (sock.readyState === WebSocket.OPEN)
        sock.send(message); };
    return new Promise((resolve, reject) => {
        sock.onopen = () => resolve({
            rawTransport: {
                send: sendMsg,
                subscribe(h) { rawHandlers.push(h); },
                unsubscribe(h) { rawHandlers = rawHandlers.filter(x => x !== h); },
            },
            lspTransport: {
                send: sendMsg,
                subscribe(h) { lspHandlers.push(h); },
                unsubscribe(h) { lspHandlers = lspHandlers.filter(x => x !== h); },
            },
            closed,
        });
        sock.onerror = () => reject(new Error(`LSP WebSocket failed: ${uri}`));
    });
}
// ─── Semantic Tokens ──────────────────────────────────────────────────────────
const setSemanticDecos = StateEffect.define();
export const semanticDecoField = StateField.define({
    create: () => Decoration.none,
    update(decos, tr) {
        decos = decos.map(tr.changes);
        for (const e of tr.effects)
            if (e.is(setSemanticDecos))
                decos = e.value;
        return decos;
    },
    provide: f => EditorView.decorations.from(f),
});
// CSS class per server token type index (nulls use StreamLanguage / default styling)
export const SEMANTIC_CLASSES = [
    null, // 0: keyword
    null, // 1: string
    null, // 2: number
    null, // 3: comment
    null, // 4: operator
    null, // 5: variable
    "cm-cgs-function", // 6: function
    "cm-cgs-type", // 7: class (object type like Dictionary)
    "cm-cgs-constant", // 8: enumMember (constant)
    null, // 9: parameter
    "cm-cgs-function", // 10: method (same style as function)
    "cm-cgs-property", // 11: property
    "cm-cgs-macro", // 12: macro (preprocessor directive)
];
let _semReqCounter = 0;
export function makeSemanticTokensViewPlugin(transport, fileUri) {
    class SemanticPlugin {
        constructor(view) {
            this._pendingId = null;
            this._debounce = null;
            this._started = false;
            this._view = view;
            this._onMsg = this._handleMsg.bind(this);
            transport.subscribe(this._onMsg);
            // Initial request fired on first update() call (after didOpen completes).
        }
        update(upd) {
            const isFirst = !this._started;
            this._started = true;
            if (isFirst || upd.docChanged) {
                if (this._debounce)
                    clearTimeout(this._debounce);
                // First call: 1.5s to let lsp-client complete initialize→initialized→didOpen.
                // Subsequent docChanged: 400ms debounce.
                this._debounce = setTimeout(() => this._request(), isFirst ? 1500 : 400);
            }
        }
        destroy() {
            transport.unsubscribe(this._onMsg);
            if (this._debounce)
                clearTimeout(this._debounce);
        }
        _request() {
            const id = `sem-${++_semReqCounter}`;
            this._pendingId = id;
            transport.send(JSON.stringify({
                jsonrpc: "2.0", id,
                method: "textDocument/semanticTokens/full",
                params: { textDocument: { uri: fileUri } },
            }));
        }
        _handleMsg(raw) {
            try {
                const msg = JSON.parse(raw);
                if (msg.id !== this._pendingId)
                    return;
                const data = msg.result?.data;
                if (!data?.length) {
                    this._view.dispatch({ effects: setSemanticDecos.of(Decoration.none) });
                    return;
                }
                const doc = this._view.state.doc;
                const decos = [];
                let line = 0, col = 0;
                for (let i = 0; i + 4 < data.length; i += 5) {
                    const dl = data[i], dc = data[i + 1], len = data[i + 2], type = data[i + 3];
                    if (dl > 0) {
                        line += dl;
                        col = dc;
                    }
                    else {
                        col += dc;
                    }
                    const cls = type < SEMANTIC_CLASSES.length ? SEMANTIC_CLASSES[type] : null;
                    if (!cls || line + 1 > doc.lines)
                        continue;
                    const lineObj = doc.line(line + 1);
                    const from = lineObj.from + col;
                    const to = from + len;
                    if (to > doc.length)
                        continue;
                    decos.push(Decoration.mark({ class: cls }).range(from, to));
                }
                this._view.dispatch({
                    effects: setSemanticDecos.of(decos.length ? Decoration.set(decos, true) : Decoration.none),
                });
            }
            catch (e) {
                console.error("[cgscript] semantic token decode error", e);
            }
        }
    }
    return [ViewPlugin.fromClass(SemanticPlugin)];
}
// ─── Themes ───────────────────────────────────────────────────────────────────
const defaultHighlight = HighlightStyle.define([
    { tag: tags.keyword, color: "#00447c" },
    { tag: tags.typeName, color: "#F60" },
    { tag: tags.bool, color: "red" },
    { tag: tags.null, color: "blue" },
    { tag: tags.number, color: "#164" },
    { tag: tags.string, color: "#a11" },
    { tag: tags.comment, color: "#A0A0A0" },
    { tag: tags.operator, color: "black" },
    { tag: tags.bracket, color: "#cc7" },
]);
const midnightHighlight = HighlightStyle.define([
    { tag: tags.keyword, color: "#8ccbff" },
    { tag: tags.typeName, color: "#F60" },
    { tag: tags.bool, color: "red" },
    { tag: tags.null, color: "#67f9e4" },
    { tag: tags.number, color: "#36ffaf" },
    { tag: tags.string, color: "#ff8e8e" },
    { tag: tags.comment, color: "#A0A0A0" },
    { tag: tags.operator, color: "yellow" },
    { tag: tags.bracket, color: "#cc7" },
]);
const defaultSemanticTheme = EditorView.theme({
    ".cm-cgs-function": { color: "#170 !important", fontStyle: "italic" },
    ".cm-cgs-type": { color: "#F60 !important" },
    ".cm-cgs-constant": { color: "#30a !important" },
    ".cm-cgs-property": { color: "#555 !important" },
    ".cm-cgs-macro": { color: "#660 !important" },
});
const midnightSemanticTheme = EditorView.theme({
    ".cm-cgs-function": { color: "#81ff6c !important", fontStyle: "italic" },
    ".cm-cgs-type": { color: "#F60 !important" },
    ".cm-cgs-constant": { color: "#ac8afd !important" },
    ".cm-cgs-property": { color: "#aaa !important" },
    ".cm-cgs-macro": { color: "#cc9900 !important" },
});
const defaultEditorTheme = EditorView.theme({
    "&": { backgroundColor: "white" },
    ".cm-gutters": { backgroundColor: "#f8f8f8", borderRight: "1px solid #ddd", color: "#999" },
    ".cm-activeLine": { backgroundColor: "#e8f2ff" },
    ".cm-activeLineGutter": { backgroundColor: "#e8f2ff" },
    ".cm-selectionBackground, ::selection": { backgroundColor: "#b3d4fd" },
    ".cm-matchingBracket": { color: "inherit", backgroundColor: "#c0e0c0", outline: "1px solid #888" },
});
const midnightEditorTheme = EditorView.theme({
    "&": { backgroundColor: "black", color: "white" },
    ".cm-gutters": { backgroundColor: "#111", color: "#888", borderColor: "#333" },
    ".cm-activeLine": { backgroundColor: "#1a1a1a" },
    ".cm-activeLineGutter": { backgroundColor: "#1a1a1a" },
    ".cm-selectionBackground, ::selection": { backgroundColor: "#334" },
    ".cm-cursor": { borderLeftColor: "white" },
    ".cm-matchingBracket": { color: "#fff", backgroundColor: "#3a3a3a", outline: "1px solid #888" },
    ".cm-searchMatch": { backgroundColor: "#555" },
    ".cm-foldPlaceholder": { backgroundColor: "#333", color: "#aaa" },
}, { dark: true });
// Signature help overload cycling hint
export const signatureHintTheme = EditorView.baseTheme({
    ".cm-lsp-signature-num": {
        position: "static !important",
    },
    ".cm-lsp-signature-num::after": {
        content: '" ctrl-shift-↑↓"',
        fontSize: "80%",
        opacity: "0.6",
        fontFamily: "sans-serif",
    },
});
const THEMES = {
    default: [defaultEditorTheme, defaultSemanticTheme, syntaxHighlighting(defaultHighlight)],
    midnight: [midnightEditorTheme, midnightSemanticTheme, syntaxHighlighting(midnightHighlight)],
};
export function resolveTheme(name) {
    return THEMES[name] ?? THEMES["default"];
}
export class CodeMirrorForCgScript {
    constructor(parent, initialContent, selectedTheme, opts) {
        _CodeMirrorForCgScript_view.set(this, void 0);
        _CodeMirrorForCgScript_themeCompartment.set(this, new Compartment());
        _CodeMirrorForCgScript_lspCompartment.set(this, new Compartment());
        _CodeMirrorForCgScript_saveDraftTimer.set(this, null);
        const { language, keywordCompletions } = createCgScriptLanguage();
        const saveDelay = opts?.saveDelay ?? 2000;
        const startState = EditorState.create({
            doc: initialContent,
            extensions: [
                lineNumbers(),
                highlightActiveLineGutter(),
                highlightSpecialChars(),
                history(),
                foldGutter(),
                drawSelection(),
                dropCursor(),
                EditorState.allowMultipleSelections.of(true),
                indentOnInput(),
                bracketMatching(),
                closeBrackets(),
                autocompletion(),
                language.data.of({ autocomplete: completeFromList(keywordCompletions) }),
                rectangularSelection(),
                crosshairCursor(),
                highlightActiveLine(),
                highlightSelectionMatches(),
                language,
                semanticDecoField,
                __classPrivateFieldGet(this, _CodeMirrorForCgScript_themeCompartment, "f").of(resolveTheme(selectedTheme)),
                __classPrivateFieldGet(this, _CodeMirrorForCgScript_lspCompartment, "f").of([]),
                EditorState.tabSize.of(4),
                keymap.of([
                    ...closeBracketsKeymap,
                    ...defaultKeymap,
                    ...searchKeymap,
                    ...historyKeymap,
                    ...foldKeymap,
                    ...completionKeymap,
                    indentWithTab,
                    { key: "Ctrl-h", run: () => { openSearchPanel(__classPrivateFieldGet(this, _CodeMirrorForCgScript_view, "f")); return true; } },
                    { key: "F11", run: () => { this._setFullscreen(true); return true; } },
                    { key: "Escape", run: () => { this._setFullscreen(false); return true; } },
                ]),
                EditorView.updateListener.of(update => {
                    if (!update.docChanged)
                        return;
                    const text = update.state.doc.toString();
                    if (opts?.onDocChange) {
                        if (__classPrivateFieldGet(this, _CodeMirrorForCgScript_saveDraftTimer, "f"))
                            clearTimeout(__classPrivateFieldGet(this, _CodeMirrorForCgScript_saveDraftTimer, "f"));
                        __classPrivateFieldSet(this, _CodeMirrorForCgScript_saveDraftTimer, setTimeout(() => opts.onDocChange(text), saveDelay), "f");
                    }
                }),
                EditorView.lineWrapping,
                signatureHintTheme,
            ],
        });
        __classPrivateFieldSet(this, _CodeMirrorForCgScript_view, new EditorView({ state: startState, parent }), "f");
    }
    connectLsp(lspClient, transport, fileUri) {
        __classPrivateFieldGet(this, _CodeMirrorForCgScript_view, "f").dispatch({
            effects: __classPrivateFieldGet(this, _CodeMirrorForCgScript_lspCompartment, "f").reconfigure([
                languageServerSupport(lspClient, fileUri, "cgscript"),
                ...makeSemanticTokensViewPlugin(transport, fileUri),
            ]),
        });
    }
    disconnectLsp() {
        __classPrivateFieldGet(this, _CodeMirrorForCgScript_view, "f").dispatch({ effects: __classPrivateFieldGet(this, _CodeMirrorForCgScript_lspCompartment, "f").reconfigure([]) });
    }
    setTheme(name) {
        __classPrivateFieldGet(this, _CodeMirrorForCgScript_view, "f").dispatch({ effects: __classPrivateFieldGet(this, _CodeMirrorForCgScript_themeCompartment, "f").reconfigure(resolveTheme(name)) });
    }
    getSelection() {
        const sel = __classPrivateFieldGet(this, _CodeMirrorForCgScript_view, "f").state.selection.main;
        return sel.from === sel.to ? "" : __classPrivateFieldGet(this, _CodeMirrorForCgScript_view, "f").state.sliceDoc(sel.from, sel.to);
    }
    loadContent(text) {
        __classPrivateFieldGet(this, _CodeMirrorForCgScript_view, "f").dispatch({
            changes: { from: 0, to: __classPrivateFieldGet(this, _CodeMirrorForCgScript_view, "f").state.doc.length, insert: text },
        });
    }
    _setFullscreen(on) {
        document.body.classList.toggle("CodeMirror-fullscreen", on);
        __classPrivateFieldGet(this, _CodeMirrorForCgScript_view, "f").requestMeasure();
    }
    toggleFullscreen() {
        this._setFullscreen(!document.body.classList.contains("CodeMirror-fullscreen"));
    }
    get view() { return __classPrivateFieldGet(this, _CodeMirrorForCgScript_view, "f"); }
}
_CodeMirrorForCgScript_view = new WeakMap(), _CodeMirrorForCgScript_themeCompartment = new WeakMap(), _CodeMirrorForCgScript_lspCompartment = new WeakMap(), _CodeMirrorForCgScript_saveDraftTimer = new WeakMap();
// ─── LSP Connection Manager ───────────────────────────────────────────────────
export async function manageLspConnection(wsUri, cm, fileUri) {
    fileUri ?? (fileUri = `file:///cgscript${location.pathname}.cgs`);
    let delay = 2000;
    while (true) {
        try {
            const { rawTransport, lspTransport, closed } = await openTransport(wsUri);
            delay = 2000;
            const lspClient = new LSPClient({ extensions: languageServerExtensions() }).connect(lspTransport);
            cm.connectLsp(lspClient, rawTransport, fileUri);
            await closed;
        }
        catch (e) {
            console.warn(`CgScript LSP: connection failed, retrying in ${delay}ms`, e);
        }
        cm.disconnectLsp();
        await new Promise(r => setTimeout(r, delay));
        delay = Math.min(delay * 2, 30000);
    }
}
