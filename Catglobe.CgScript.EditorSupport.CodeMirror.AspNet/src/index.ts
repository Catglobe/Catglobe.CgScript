import { EditorState, StateEffect, StateField, Compartment, type Extension, type Range } from "@codemirror/state";
import {
   EditorView, ViewPlugin, lineNumbers, highlightActiveLineGutter, highlightSpecialChars,
   drawSelection, dropCursor, rectangularSelection, crosshairCursor, Decoration,
   highlightActiveLine, keymap, type DecorationSet, type ViewUpdate,
} from "@codemirror/view";
import {
   foldGutter, indentOnInput, syntaxHighlighting,
   HighlightStyle, bracketMatching, foldKeymap, StreamLanguage,
} from "@codemirror/language";
import { history, defaultKeymap, historyKeymap, indentWithTab, undo, redo } from "@codemirror/commands";
import { javascript } from "@codemirror/lang-javascript";
import { html } from "@codemirror/lang-html";
import { highlightSelectionMatches, searchKeymap, openSearchPanel } from "@codemirror/search";
import {
   autocompletion, completionKeymap, completeFromList,
   closeBrackets, closeBracketsKeymap,
} from "@codemirror/autocomplete";
import { tags } from "@lezer/highlight";
import {
   LSPClient, LSPPlugin, languageServerExtensions,
   type LSPClientExtension, type Transport,
} from "@codemirror/lsp-client";

export type { Transport, Extension };
export { LSPClient, languageServerExtensions };

// ─── CgScript StreamLanguage ──────────────────────────────────────────────────

interface CgState {
   tokenize: ((stream: any, state: CgState) => string) | null;
   context:  CgContext | null;
   indented: number;
   curPunc:  string | null;
}

class CgContext {
   constructor(
      public indented: number,
      public column: number,
      public type: string,
      public align: boolean | null,
      public prev: CgContext | null,
   ) {}
}

export function createCgScriptLanguage() {
   const blockkw  = new Set("catch else for if try while function switch case".split(" "));
   const keywords = new Set([..."break continue return where new throw".split(" "), ...blockkw]);
   const types    = new Set("object bool number string array question".split(" "));
   const atoms    = new Set(["true", "false"]);
   const nulls    = new Set(["empty"]);

   const keywordCompletions = [
      ...[...keywords].map(w => ({ label: w, type: "keyword" })),
      ...[...types].map(w =>    ({ label: w, type: "keyword" })),
      ...[...atoms].map(w =>    ({ label: w, type: "keyword" })),
      ...[...nulls].map(w =>    ({ label: w, type: "keyword" })),
   ];

   const isOperatorChar = /[+\-*&%=<>!?|/]/;

   function tokenBase(stream: any, state: CgState): string {
      const ch = stream.next() as string;
      if (ch === '"') {
         state.tokenize = tokenString(ch);
         return state.tokenize(stream, state);
      }
      if (/[[\]{}()]/.test(ch)) { state.curPunc = ch; return "bracket"; }
      if (/[[\]{}(),;:.]/.test(ch)) { state.curPunc = ch; return ""; }
      if (/\d/.test(ch)) { stream.eatWhile(/[\w.]/); return "number"; }
      if (ch === "/") {
         if (stream.eat("*")) { state.tokenize = tokenComment; return tokenComment(stream, state); }
         if (stream.eat("/")) { stream.skipToEnd(); return "comment"; }
      }
      if (isOperatorChar.test(ch)) { stream.eatWhile(isOperatorChar); return "operator"; }
      stream.eatWhile(/[\w$_\xa1-\uffff]/);
      const cur = stream.current() as string;
      if (keywords.has(cur)) { if (blockkw.has(cur)) state.curPunc = "newstatement"; return "keyword"; }
      if (types.has(cur))     return "cgType";
      if (nulls.has(cur))     return "cgNull";
      if (atoms.has(cur))     return "cgAtom";
      return "variable";
   }

   function tokenString(quote: string) {
      return function (stream: any, state: CgState): string {
         let escaped = false, next: string | null;
         while ((next = stream.next()) != null) {
            if (next === quote && !escaped) { state.tokenize = null; break; }
            escaped = !escaped && next === "\\";
         }
         return "string";
      };
   }

   function tokenComment(stream: any, state: CgState): string {
      let maybeEnd = false, ch: string | null;
      while ((ch = stream.next())) {
         if (ch === "/" && maybeEnd) { state.tokenize = null; break; }
         maybeEnd = (ch === "*");
      }
      return "comment";
   }

   function pushContext(state: CgState, col: number, type: string) {
      const indent = state.context?.type === "statement" ? state.context.indented : state.indented;
      return (state.context = new CgContext(indent, col, type, null, state.context));
   }
   function popContext(state: CgState) {
      if (!state.context) return null;
      const t = state.context.type;
      if (t === ")" || t === "]" || t === "}") state.indented = state.context.indented;
      return (state.context = state.context.prev);
   }

   const cgscriptParser = {
      name: "cgscript",
      tokenTable: {
         keyword:  tags.keyword,
         cgType:   tags.typeName,
         cgAtom:   tags.bool,
         cgNull:   tags.null,
         number:   tags.number,
         string:   tags.string,
         comment:  tags.comment,
         operator: tags.operator,
         bracket:  tags.bracket,
      },

      startState(_baseIndent?: number): CgState {
         return {
            tokenize: null,
            context:  new CgContext(-4, 0, "top", false, null),
            indented: 0,
            curPunc:  null,
         };
      },

      copyState(state: CgState): CgState {
         return { tokenize: state.tokenize, context: state.context, indented: state.indented, curPunc: null };
      },

      token(stream: any, state: CgState): string | null {
         const ctx = state.context;
         if (stream.sol()) {
            if (ctx && ctx.align == null) ctx.align = false;
            state.indented = stream.indentation();
         }
         if (stream.eatSpace()) return null;
         state.curPunc = null;
         const style = (state.tokenize ?? tokenBase)(stream, state);
         if (style === "comment") return style;
         if (ctx && ctx.align == null) ctx.align = true;

         const p = state.curPunc;
         if ((p === ";" || p === ":" || p === ",") && state.context?.type === "statement") {
            popContext(state);
         } else if (p === "{") { pushContext(state, stream.column(), "}"); }
         else if (p === "[")   { pushContext(state, stream.column(), "]"); }
         else if (p === "(")   { pushContext(state, stream.column(), ")"); }
         else if (p === "}") {
            let c = state.context;
            while (c && c.type === "statement") c = popContext(state);
            if (c && c.type === "}") c = popContext(state);
            while (c && c.type === "statement") c = popContext(state);
         } else if (p === state.context?.type) {
            popContext(state);
         } else if (state.context && (state.context.type === "}" || state.context.type === "top") && p !== ";" ||
                    state.context?.type === "statement" && p === "newstatement") {
            pushContext(state, stream.column(), "statement");
         }
         return style || null;
      },

      indent(state: CgState, textAfter?: string): number | null {
         if (state.tokenize != null) return null;
         let ctx = state.context;
         if (!ctx) return 0;
         const first   = textAfter?.charAt(0);
         if (ctx.type === "statement" && first === "}") ctx = ctx.prev;
         if (!ctx) return 0;
         const closing = first === ctx.type;
         const unit = 4;
         if      (ctx.type === "statement") return ctx.indented + (first === "{" ? 0 : unit);
         else if (ctx.align)                return ctx.column + (closing ? 0 : 1);
         else if (ctx.type === ")" && !closing) return ctx.indented + unit;
         else                               return ctx.indented + (closing ? 0 : unit);
      },

      languageData: {
         commentTokens:  { line: "//", block: { open: "/*", close: "*/" } },
         indentOnInput:  /^\s*[{}]$/,
         closeBrackets:  { brackets: ["(", "[", "{", '"'] },
      },
   };

   const language = StreamLanguage.define(cgscriptParser);
   return { language, keywordCompletions };
}

// ─── QSL StreamLanguage ───────────────────────────────────────────────────────

interface QslState {
   tokenize: ((stream: any, state: QslState) => string) | null;
   inBrackets: number;
}

export function createQslLanguage() {
   const qslKeywords = new Set([
      "questionnaire", "question", "group", "end", "page",
      "single", "multi", "number", "open", "yesno",
      "matrix", "scale", "scalegrid", "date", "time", "datetime",
      "rankno", "ranktext", "ranknumber", "netnumber",
      "branch", "goto", "if", "else", "call", "return",
      "replace", "with", "include", "and", "or", "not",
   ]);
   const boolLiterals = new Set(["true", "false"]);

   const keywordCompletions = [
      ...[...qslKeywords].map(w => ({ label: w.toUpperCase(), type: "keyword" })),
   ];

   function tokenBase(stream: any, state: QslState): string {
      const ch = stream.next() as string;
      if (ch === '"') {
         state.tokenize = tokenQslString;
         return tokenQslString(stream, state);
      }
      if (ch === "[") { state.inBrackets++; return "bracket"; }
      if (ch === "]") { state.inBrackets = Math.max(0, state.inBrackets - 1); return "bracket"; }
      if (ch === "/" && stream.eat("*")) { state.tokenize = tokenQslBlockComment; return tokenQslBlockComment(stream, state); }
      if (ch === "/" && stream.eat("/")) { stream.skipToEnd(); return "comment"; }
      if (/[<>!]/.test(ch)) { stream.eat(/[=<>]/); return "operator"; }
      if (ch === "=" || ch === ":") return "operator";
      if (/\d/.test(ch)) { stream.eatWhile(/\d/); return "number"; }
      if (/\w/.test(ch)) {
         stream.eatWhile(/\w/);
         const cur = stream.current().toLowerCase() as string;
         if (qslKeywords.has(cur)) return "keyword";
         if (boolLiterals.has(cur)) return "atom";
         return state.inBrackets > 0 ? "property" : "variable";
      }
      return "";
   }

   function tokenQslString(stream: any, state: QslState): string {
      let ch: string | null;
      while ((ch = stream.next()) != null) {
         if (ch === '"') { state.tokenize = null; break; }
      }
      return "string";
   }

   function tokenQslBlockComment(stream: any, state: QslState): string {
      let maybeEnd = false, ch: string | null;
      while ((ch = stream.next())) {
         if (ch === "/" && maybeEnd) { state.tokenize = null; break; }
         maybeEnd = (ch === "*");
      }
      return "comment";
   }

   const qslParser = {
      name: "qsl",
      tokenTable: {
         keyword:  tags.keyword,
         atom:     tags.bool,
         number:   tags.number,
         string:   tags.string,
         comment:  tags.comment,
         operator: tags.operator,
         bracket:  tags.bracket,
         property: tags.propertyName,
         variable: tags.variableName,
      },
      startState(): QslState { return { tokenize: null, inBrackets: 0 }; },
      copyState(state: QslState): QslState { return { tokenize: state.tokenize, inBrackets: state.inBrackets }; },
      token(stream: any, state: QslState): string | null {
         if (stream.eatSpace()) return null;
         return (state.tokenize ?? tokenBase)(stream, state) || null;
      },
      languageData: {
         commentTokens: { line: "//", block: { open: "/*", close: "*/" } },
      },
   };

   const language = StreamLanguage.define(qslParser);
   return { language, keywordCompletions };
}

// ─── LSP Transport ────────────────────────────────────────────────────────────

export interface SplitTransport {
   transport: Transport;
   closed: Promise<CloseEvent>;
}

export async function openTransport(uri: string): Promise<SplitTransport> {
   const sock = new WebSocket(uri);
   let handlers: Array<(m: string) => void> = [];

   sock.onmessage = e => {
      const msg = e.data as string;
      for (const h of handlers) h(msg);
   };

   const closed = new Promise<CloseEvent>(resolve => { sock.onclose = resolve; });
   const sendMsg = (message: string) => { if (sock.readyState === WebSocket.OPEN) sock.send(message); };

   return new Promise((resolve, reject) => {
      sock.onopen = () => resolve({
         transport: {
            send: sendMsg,
            subscribe(h)   { handlers.push(h); },
            unsubscribe(h) { handlers = handlers.filter(x => x !== h); },
         },
         closed,
      });
      sock.onerror = () => reject(new Error(`LSP WebSocket failed: ${uri}`));
   });
}

// ─── Semantic Tokens ──────────────────────────────────────────────────────────

const setSemanticDecos = StateEffect.define<DecorationSet>();

export const semanticDecoField = StateField.define<DecorationSet>({
   create: () => Decoration.none,
   update(decos, tr) {
      decos = decos.map(tr.changes);
      for (const e of tr.effects)
         if (e.is(setSemanticDecos)) decos = e.value;
      return decos;
   },
   provide: f => EditorView.decorations.from(f),
});

// CSS class per server token type index (nulls use StreamLanguage / default styling)
export const SEMANTIC_CLASSES: Array<string | null> = [
   null,               // 0: keyword
   null,               // 1: string
   null,               // 2: number
   null,               // 3: comment
   null,               // 4: operator
   null,               // 5: variable
   "cm-cgs-function",  // 6: function
   "cm-cgs-type",      // 7: class (object type like Dictionary)
   "cm-cgs-constant",  // 8: enum (constant)
   null,               // 9: parameter
   "cm-cgs-function",  // 10: method (same style as function)
   "cm-cgs-property",  // 11: property
   "cm-cgs-macro",     // 12: macro (preprocessor directive)
];

/// An LSPClientExtension that requests semantic tokens from the language
/// server and applies syntax highlighting decorations. Pass to the
/// LSPClient extensions option alongside languageServerExtensions().
export function makeSemanticTokensViewPlugin(): LSPClientExtension {
   const plugin = ViewPlugin.fromClass(class {
      private _view: EditorView;
      private _reqCounter = 0;
      private _debounce: ReturnType<typeof setTimeout> | null = null;
      private _started = false;

      constructor(view: EditorView) {
         this._view = view;
      }

      update(upd: ViewUpdate) {
         const isFirst = !this._started;
         this._started = true;
         if (isFirst || upd.docChanged) {
            if (this._debounce) clearTimeout(this._debounce);
            // First call: 1.5s to let lsp-client complete initialize→initialized→didOpen.
            // Subsequent docChanged: 400ms debounce.
            this._debounce = setTimeout(() => this._request(), isFirst ? 1500 : 400);
         }
      }

      destroy() {
         if (this._debounce) clearTimeout(this._debounce);
      }

      private _request() {
         const lsp = LSPPlugin.get(this._view);
         if (!lsp) return;
         lsp.client.sync();
         const myCount = ++this._reqCounter;
         lsp.client.request<{ textDocument: { uri: string } }, { data?: number[] } | null>(
            "textDocument/semanticTokens/full",
            { textDocument: { uri: lsp.uri } }
         ).then(result => {
            if (myCount !== this._reqCounter) return; // stale — a newer request is in flight
            this._applyTokens(result?.data ?? []);
         }).catch(() => { /* disconnected or server error — ignore */ });
      }

      private _applyTokens(data: number[]) {
         if (!data.length) {
            this._view.dispatch({ effects: setSemanticDecos.of(Decoration.none) });
            return;
         }

         const doc   = this._view.state.doc;
         const decos: Range<Decoration>[] = [];
         let line = 0, col = 0;

         for (let i = 0; i + 4 < data.length; i += 5) {
            const dl = data[i], dc = data[i + 1], len = data[i + 2], type = data[i + 3];
            if (dl > 0) { line += dl; col = dc; } else { col += dc; }

            const cls = type < SEMANTIC_CLASSES.length ? SEMANTIC_CLASSES[type] : null;
            if (!cls || line + 1 > doc.lines) continue;

            const lineObj = doc.line(line + 1);
            const from    = lineObj.from + col;
            const to      = from + len;
            if (to > doc.length) continue;
            decos.push(Decoration.mark({ class: cls }).range(from, to));
         }

         this._view.dispatch({
            effects: setSemanticDecos.of(
               decos.length ? Decoration.set(decos, true) : Decoration.none
            ),
         });
      }
   });

   return {
      clientCapabilities: {
         textDocument: {
            semanticTokens: {
               requests: { full: true },
               tokenTypes: [],
               tokenModifiers: [],
               formats: ["relative"],
            },
         },
      },
      editorExtension: [semanticDecoField, plugin],
   };
}

// ─── Themes ───────────────────────────────────────────────────────────────────

const defaultHighlight = HighlightStyle.define([
   { tag: tags.keyword,   color: "#00447c" },
   { tag: tags.typeName,  color: "#F60" },
   { tag: tags.bool,      color: "red" },
   { tag: tags.null,      color: "blue" },
   { tag: tags.number,    color: "#164" },
   { tag: tags.string,    color: "#a11" },
   { tag: tags.comment,   color: "#A0A0A0" },
   { tag: tags.operator,  color: "black" },
   { tag: tags.bracket,   color: "#cc7" },
]);

const midnightHighlight = HighlightStyle.define([
   { tag: tags.keyword,   color: "#8ccbff" },
   { tag: tags.typeName,  color: "#F60" },
   { tag: tags.bool,      color: "red" },
   { tag: tags.null,      color: "#67f9e4" },
   { tag: tags.number,    color: "#36ffaf" },
   { tag: tags.string,    color: "#ff8e8e" },
   { tag: tags.comment,   color: "#A0A0A0" },
   { tag: tags.operator,  color: "yellow" },
   { tag: tags.bracket,   color: "#cc7" },
]);

// Single base theme — responds to &light / &dark set by the compartment flag below.
// baseTheme has lower specificity than theme(), so per-editor overrides still win.
const cgscriptBaseTheme = EditorView.baseTheme({
   "&light":                         { backgroundColor: "white" },
   "&dark":                          { backgroundColor: "black", color: "white" },
   "&light .cm-gutters":             { backgroundColor: "#f8f8f8", borderRight: "1px solid #ddd", color: "#999" },
   "&dark  .cm-gutters":             { backgroundColor: "#111", color: "#888", borderColor: "#333" },
   "&light .cm-activeLine":          { backgroundColor: "#e8f2ff33" },
   "&dark  .cm-activeLine":          { backgroundColor: "#1a1a1a33" },
   "&light .cm-activeLineGutter":    { backgroundColor: "#e8f2ff" },
   "&dark  .cm-activeLineGutter":    { backgroundColor: "#1a1a1a" },
   "&light.cm-focused > .cm-scroller > .cm-selectionLayer .cm-selectionBackground, &light .cm-selectionBackground, &light .cm-content ::selection": { backgroundColor: "#b3d4fd" },
   "&dark.cm-focused > .cm-scroller > .cm-selectionLayer .cm-selectionBackground, &dark .cm-selectionBackground, &dark .cm-content ::selection":   { backgroundColor: "#334" },
   "&dark  .cm-cursor":              { borderLeftColor: "white" },
   "&light .cm-matchingBracket":     { color: "inherit", backgroundColor: "#c0e0c0", outline: "1px solid #888" },
   "&dark  .cm-matchingBracket":     { color: "#fff", backgroundColor: "#3a3a3a", outline: "1px solid #888" },
   "&dark  .cm-searchMatch":         { backgroundColor: "#555" },
   "&dark  .cm-foldPlaceholder":     { backgroundColor: "#333", color: "#aaa" },
   // semantic highlights
   "&light .cm-cgs-function":        { color: "#170 !important", fontStyle: "italic" },
   "&dark  .cm-cgs-function":        { color: "#81ff6c !important", fontStyle: "italic" },
   ".cm-cgs-type":                   { color: "#F60 !important" },
   "&light .cm-cgs-constant":        { color: "#30a !important" },
   "&dark  .cm-cgs-constant":        { color: "#ac8afd !important" },
   "&light .cm-cgs-property":        { color: "#555 !important" },
   "&dark  .cm-cgs-property":        { color: "#aaa !important" },
   "&light .cm-cgs-macro":           { color: "#660 !important" },
   "&dark  .cm-cgs-macro":           { color: "#cc9900 !important" },
});

// Signature help overload cycling hint
export const signatureHintTheme = EditorView.baseTheme({
   ".cm-lsp-signature-num": {
      position: "static !important" as "static",
   },
   ".cm-lsp-signature-num::after": {
      content: '" ctrl-shift-↑↓"',
      fontSize: "80%",
      opacity: "0.6",
      fontFamily: "sans-serif",
   },
});

const THEMES: Record<string, Extension[]> = {
   default:  [EditorView.theme({}, { dark: false }), syntaxHighlighting(defaultHighlight)],
   midnight: [EditorView.theme({}, { dark: true  }), syntaxHighlighting(midnightHighlight)],
};

export function resolveTheme(name: string): Extension[] {
   return THEMES[name] ?? THEMES["default"];
}

// ─── Base editor ─────────────────────────────────────────────────────────────

/**
 * Foundation for all CM6 editors in this package.
 * Holds the shared extensions and exposes all common public methods.
 * Pass language-specific and LSP extensions via {@link extraExtensions}.
 */
class BaseEditor {
   protected readonly _view: EditorView;
   readonly #themeCompartment    = new Compartment();
   readonly #editableCompartment = new Compartment();

   constructor(parent: Element, initialContent: string, selectedTheme: string, extraExtensions: Extension[] = []) {
      const state = EditorState.create({
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
            rectangularSelection(),
            crosshairCursor(),
            highlightActiveLine(),
            highlightSelectionMatches(),
            cgscriptBaseTheme,
            this.#themeCompartment.of(resolveTheme(selectedTheme)),
            this.#editableCompartment.of(EditorView.editable.of(true)),
            EditorState.tabSize.of(4),
            keymap.of([
               ...closeBracketsKeymap,
               ...defaultKeymap,
               ...searchKeymap,
               ...historyKeymap,
               ...foldKeymap,
               ...completionKeymap,
               indentWithTab,
               { key: "Ctrl-h", run: () => { openSearchPanel(this._view); return true; } },
            ]),
            EditorView.lineWrapping,
            ...extraExtensions,
         ],
      });
      this._view = new EditorView({ state, parent });
   }

   static _fullscreen(view: EditorView, on: boolean) {
      document.body.classList.toggle("CodeMirror-fullscreen", on);
      view.requestMeasure();
   }
   _setFullscreen(on: boolean)  { BaseEditor._fullscreen(this._view, on); }
   toggleFullscreen()           { this._setFullscreen(!document.body.classList.contains("CodeMirror-fullscreen")); }

   setTheme(name: string)         { this._view.dispatch({ effects: this.#themeCompartment.reconfigure(resolveTheme(name)) }); }
   setEditable(editable: boolean) { this._view.dispatch({ effects: this.#editableCompartment.reconfigure(EditorView.editable.of(editable)) }); }
   loadContent(text: string)      { this._view.dispatch({ changes: { from: 0, to: this._view.state.doc.length, insert: text } }); }
   getContent(): string           { return this._view.state.doc.toString(); }
   getSelection(): string {
      const sel = this._view.state.selection.main;
      return sel.from === sel.to ? "" : this._view.state.sliceDoc(sel.from, sel.to);
   }
   setSize(w: string | number, h: string | number): void {
      this._view.dom.style.width  = typeof w === "number" ? `${w}px` : (w as string);
      this._view.dom.style.height = typeof h === "number" ? `${h}px` : (h as string);
   }
   focus():                 void    { this._view.focus(); }
   onBlur(fn: () => void):  void    { this._view.contentDOM.addEventListener("blur", fn); }
   onScroll(fn: () => void): void   { this._view.scrollDOM.addEventListener("scroll", fn); }
   scrollTop():             number  { return this._view.scrollDOM.scrollTop; }
   get scrollerElement():   Element { return this._view.scrollDOM; }
   undo():                  void    { undo(this._view); }
   redo():                  void    { redo(this._view); }
   openSearch():            void    { openSearchPanel(this._view); }
   get view():              EditorView { return this._view; }
}

// ─── Shared interface for editors that support LSP ───────────────────────────

export interface LspEditor {
   connectLsp(client: LSPClient, uri: string): void;
   disconnectLsp(): void;
}

// ─── CodeMirrorForCgScript ────────────────────────────────────────────────────

export interface CgScriptEditorOptions {
   /** Called (debounced) whenever the document changes. */
   onDocChange?: (text: string) => void;
   /** Debounce delay in ms for onDocChange. Default: 2000. */
   saveDelay?: number;
}

export class CodeMirrorForCgScript extends BaseEditor implements LspEditor {
   readonly #lspCompartment: Compartment;
   #lspClient: LSPClient | null = null;
   #pendingQuestionVars: { label: string; typeName: string }[] | null = null;

   constructor(parent: Element, initialContent: string, selectedTheme: string, opts?: CgScriptEditorOptions) {
      const lspComp = new Compartment();
      const { language, keywordCompletions } = createCgScriptLanguage();
      const saveDelay = opts?.saveDelay ?? 2000;
      let saveDraftTimer: ReturnType<typeof setTimeout> | null = null;

      super(parent, initialContent, selectedTheme, [
         language.data.of({ autocomplete: completeFromList(keywordCompletions) }),
         language,
         semanticDecoField,
         lspComp.of([]),
         signatureHintTheme,
         keymap.of([
            { key: "F11",    run: (view) => { BaseEditor._fullscreen(view, true);  return true; } },
            { key: "Escape", run: (view) => { BaseEditor._fullscreen(view, false); return true; } },
         ]),
         EditorView.updateListener.of(update => {
            if (!update.docChanged || !opts?.onDocChange) return;
            if (saveDraftTimer) clearTimeout(saveDraftTimer);
            saveDraftTimer = setTimeout(() => opts.onDocChange!(update.state.doc.toString()), saveDelay);
         }),
      ]);
      this.#lspCompartment = lspComp;
   }

   connectLsp(lspClient: LSPClient, fileUri: string) {
      this.#lspClient = lspClient;
      this._view.dispatch({
         effects: this.#lspCompartment.reconfigure(lspClient.plugin(fileUri, "cgscript")),
      });
      if (this.#pendingQuestionVars) {
         const vars = this.#pendingQuestionVars;
         lspClient.initializing.then(() => lspClient.notification("workspace/didChangeConfiguration", {
            settings: { cgscript: { questionVariables: vars } },
         })).catch(() => { /* connection already closed */ });
      }
   }

   disconnectLsp() {
      this.#lspClient = null;
      this._view.dispatch({ effects: this.#lspCompartment.reconfigure([]) });
   }

   /**
    * Push the current questionnaire question variables to the LSP server so that
    * question labels are available for completion/hover even before saving.
    * Re-sends automatically on every reconnect.
    */
   sendQuestionVariables(vars: { label: string; typeName: string }[]) {
      this.#pendingQuestionVars = vars;
      if (this.#lspClient) {
         const client = this.#lspClient;
         client.initializing.then(() => client.notification("workspace/didChangeConfiguration", {
            settings: { cgscript: { questionVariables: vars } },
         })).catch(() => { /* connection already closed */ });
      }
   }
}

// ─── CodeMirrorForQsl─────────────────────────────────────────────────────────

export interface QslEditorOptions {
   /** Called (debounced) whenever the document changes. */
   onDocChange?: (text: string) => void;
   /** Debounce delay in ms for onDocChange. Default: 2000. */
   saveDelay?: number;
}

export class CodeMirrorForQsl extends BaseEditor implements LspEditor {
   readonly #lspCompartment: Compartment;

   constructor(parent: Element, initialContent: string, selectedTheme: string, opts?: QslEditorOptions) {
      const lspComp = new Compartment();
      const { language, keywordCompletions } = createQslLanguage();
      const saveDelay = opts?.saveDelay ?? 2000;
      let saveDraftTimer: ReturnType<typeof setTimeout> | null = null;

      super(parent, initialContent, selectedTheme, [
         language.data.of({ autocomplete: completeFromList(keywordCompletions) }),
         language,
         semanticDecoField,
         lspComp.of([]),
         signatureHintTheme,
         keymap.of([
            { key: "F11",    run: (view) => { BaseEditor._fullscreen(view, true);  return true; } },
            { key: "Escape", run: (view) => { BaseEditor._fullscreen(view, false); return true; } },
         ]),
         EditorView.updateListener.of(update => {
            if (!update.docChanged || !opts?.onDocChange) return;
            if (saveDraftTimer) clearTimeout(saveDraftTimer);
            saveDraftTimer = setTimeout(() => opts.onDocChange!(update.state.doc.toString()), saveDelay);
         }),
      ]);
      this.#lspCompartment = lspComp;
   }

   connectLsp(lspClient: LSPClient, fileUri: string) {
      this._view.dispatch({
         effects: this.#lspCompartment.reconfigure(lspClient.plugin(fileUri, "qsl")),
      });
   }

   disconnectLsp() {
      this._view.dispatch({ effects: this.#lspCompartment.reconfigure([]) });
   }
}

// ─── LSP Connection Manager───────────────────────────────────────────────────

export async function manageLspConnection(wsUri: string, cm: LspEditor, fileUri: string): Promise<never> {
   let delay = 2000;
   while (true) {
      try {
         const { transport, closed } = await openTransport(wsUri);
         delay = 2000;
         const lspClient = new LSPClient({ extensions: [...languageServerExtensions(), makeSemanticTokensViewPlugin()] }).connect(transport);
         cm.connectLsp(lspClient, fileUri);
         await closed;
      } catch (e) {
         console.warn(`LSP: connection failed, retrying in ${delay}ms`, e);
      }
      cm.disconnectLsp();
      await new Promise(r => setTimeout(r, delay));
      delay = Math.min(delay * 2, 30_000);
   }
}

export { html, javascript };

// ─── Simple generic editor (no LSP) ──────────────────────────────────────────

export interface SimpleEditor {
   setTheme(name: string): void;
   setEditable(editable: boolean): void;
   loadContent(text: string): void;
   getContent(): string;
   getSelection(): string;
   setSize(w: string | number, h: string | number): void;
   focus(): void;
   onBlur(fn: () => void): void;
   onScroll(fn: () => void): void;
   scrollTop(): number;
   readonly scrollerElement: Element;
   undo(): void;
   redo(): void;
   openSearch(): void;
}

/**
 * Creates a generic CodeMirror 6 editor using the shared {@link BaseEditor} foundation.
 * Pass an optional language extension (e.g. `javascript()` or `html()`) for syntax
 * highlighting and completion. Omit for plain-text / CSS editors.
 */
export function makeSimpleEditor(
   parent: Element,
   initialContent: string,
   selectedTheme: string,
   languageExtension?: Extension,
): SimpleEditor {
   return new BaseEditor(parent, initialContent, selectedTheme,
      languageExtension ? [languageExtension] : []);
}

// ─── LSP diagnostic WebSocket intercept ───────────────────────────────────────

export interface LspDiagnosticInfo {
   severity:  number;
   message:   string;
   line:      number;
   character: number;
}

/**
 * Patches window.WebSocket once so that any LSP connection created after this
 * call will forward textDocument/publishDiagnostics messages to the callback.
 */
export function patchWebSocketForDiagnostics(onDiagnostics: (diags: LspDiagnosticInfo[]) => void): void {
   const OriginalWS = window.WebSocket as typeof WebSocket;
   let intercepted = false;

   (window as Window & typeof globalThis).WebSocket = class extends OriginalWS {
      constructor(url: string | URL, protocols?: string | string[]) {
         super(url, protocols);
         if (!intercepted) {
            intercepted = true;
            this.addEventListener("message", (evt: MessageEvent) => {
               try {
                  const msg = JSON.parse(evt.data as string);
                  if (msg.method === "textDocument/publishDiagnostics" && Array.isArray(msg.params?.diagnostics)) {
                     const diags: LspDiagnosticInfo[] = msg.params.diagnostics.map((d: { severity: number; message: string; range?: { start?: { line?: number; character?: number } } }) => ({
                        severity:  d.severity,
                        message:   d.message,
                        line:      d.range?.start?.line      ?? 0,
                        character: d.range?.start?.character ?? 0,
                     }));
                     onDiagnostics(diags);
                  }
               } catch { /* ignore parse errors */ }
            });
         }
      }
   } as typeof WebSocket;
}
