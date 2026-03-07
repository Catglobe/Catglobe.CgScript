using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using System.Collections.Generic;

namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>
/// Represents a single occurrence of a symbol — either its declaration or a usage.
/// <para>
/// <see cref="Line"/> is 1-based (ANTLR convention); <see cref="Column"/> is 0-based.
/// </para>
/// </summary>
public sealed record SymbolRef(int Line, int Column, int Length, bool IsDeclaration);

/// <summary>
/// Two-pass reference analyzer for CgScript.
/// <para>
/// <b>Pass 1</b> collects every symbol declaration, grouped by the scope in which it lives.
/// <b>Pass 2</b> walks the same tree and resolves every identifier to its scope, collecting
/// all references (declarations and usages) into per-symbol groups.
/// </para>
/// <para>
/// The public API then finds which group the cursor is inside and returns it, giving the
/// caller all the information needed for <em>go-to-definition</em>, <em>find references</em>
/// and <em>rename</em>.
/// </para>
/// </summary>
public static class ReferenceAnalyzer
{
    // ── Scope-key conventions ─────────────────────────────────────────────────────
    //   0                                → global scope
    //   funcToken.Line * 100_000 + funcToken.Column  → function-literal scope
    //   catchToken.Line * 100_000 + catchToken.Column → catch-variable scope
    // All values are guaranteed positive and unique within one parse tree.

    // ── Public API ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds all references (including the declaration) to the user-defined symbol
    /// that the cursor is positioned on.
    /// </summary>
    /// <param name="tree">Root of the ANTLR4 parse tree.</param>
    /// <param name="cursorLine">1-based line number (ANTLR convention).</param>
    /// <param name="cursorColumn">0-based column number.</param>
    /// <returns>
    /// All <see cref="SymbolRef"/> entries for the symbol under the cursor, or an empty
    /// list when the cursor is not on a user-defined symbol.
    /// </returns>
    public static IReadOnlyList<SymbolRef> FindReferences(
        IParseTree tree,
        int        cursorLine,
        int        cursorColumn)
    {
        // ── Pass 1 ───────────────────────────────────────────────────────────────
        var collector = new DeclCollector();
        collector.Visit(tree);

        // ── Pass 2 ───────────────────────────────────────────────────────────────
        var refVisitor = new RefVisitor(collector.ScopeDecls, collector.DeclTokens);
        refVisitor.Visit(tree);

        // ── Find the group that contains the cursor ───────────────────────────────
        foreach (var kvp in refVisitor.Groups)
        {
            var list     = kvp.Value;
            bool hasDecl = false;

            foreach (var r in list)
            {
                if (r.IsDeclaration)
                    hasDecl = true;

                if (r.Line   == cursorLine
                    && r.Column <= cursorColumn
                    && cursorColumn < r.Column + r.Length)
                {
                    // Only return groups that have a declaration (user-defined symbol).
                    // Built-in or unknown identifiers also produce groups but have no
                    // declaration entry, so we must check the whole list first.
                    // Because hasDecl is accumulated while scanning, do a second pass
                    // only when the cursor hit was found before a declaration was seen.
                    if (hasDecl)
                        return list;

                    // Cursor match found but declaration not seen yet — scan remainder.
                    for (int i = list.IndexOf(r) + 1; i < list.Count; i++)
                    {
                        if (list[i].IsDeclaration)
                            return list;
                    }

                    return Array.Empty<SymbolRef>();
                }
            }
        }

        return Array.Empty<SymbolRef>();
    }

    /// <summary>
    /// Convenience overload: returns just the declaration <see cref="SymbolRef"/> for
    /// the symbol at the cursor, or <see langword="null"/> when not applicable.
    /// </summary>
    public static SymbolRef? FindDeclaration(
        IParseTree tree,
        int        cursorLine,
        int        cursorColumn)
    {
        foreach (var r in FindReferences(tree, cursorLine, cursorColumn))
        {
            if (r.IsDeclaration)
                return r;
        }

        return null;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Pass 1 — DeclCollector
    // Walks the tree and records every declared symbol together with the scope key
    // of the scope in which it was declared.  Function and catch scopes get their
    // own scope keys so that local declarations never clash with globals.
    // ═════════════════════════════════════════════════════════════════════════════

    private sealed class DeclCollector : CgScriptParserBaseVisitor<object?>
    {
        // Scope stack — top of stack is the "current" scope.
        private readonly Stack<int> _scopeStack = new Stack<int>();

        /// <summary>Maps scope key → set of names declared in that scope.</summary>
        public Dictionary<int, HashSet<string>> ScopeDecls { get; }
            = new Dictionary<int, HashSet<string>>();

        /// <summary>Maps (scopeKey, name) → the token of the first declaration.</summary>
        public Dictionary<(int scope, string name), IToken> DeclTokens { get; }
            = new Dictionary<(int scope, string name), IToken>();

        public DeclCollector()
        {
            _scopeStack.Push(0);
            ScopeDecls[0] = new HashSet<string>(StringComparer.Ordinal);
        }

        private int CurrentScope => _scopeStack.Peek();

        // ── Record helpers ────────────────────────────────────────────────────────

        /// <summary>Records <paramref name="token"/> in the given <paramref name="scopeKey"/>.</summary>
        private void RecordInScope(int scopeKey, IToken token)
        {
            if (!ScopeDecls.TryGetValue(scopeKey, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                ScopeDecls[scopeKey] = set;
            }

            var name = token.Text;
            set.Add(name);                         // HashSet: first-wins semantics for the set itself

            var key = (scopeKey, name);
            if (!DeclTokens.ContainsKey(key))
                DeclTokens[key] = token;           // first declaration wins
        }

        /// <summary>Records <paramref name="token"/> in the current (top-of-stack) scope.</summary>
        private void Record(IToken token) => RecordInScope(CurrentScope, token);

        // ── Visitor overrides ─────────────────────────────────────────────────────

        /// <summary>
        /// Function literal — pushes a new scope for the function's parameters and
        /// local declarations.  Does <b>not</b> call the default VisitChildren
        /// delegate (we call it manually after pushing the scope so that nested
        /// declarations are attributed to the correct scope).
        /// </summary>
        public override object? VisitPrimaryExpr(CgScriptParser.PrimaryExprContext ctx)
        {
            if (ctx.FUNCTION() != null)
            {
                var funcToken = ctx.FUNCTION().Symbol;
                var scopeKey  = funcToken.Line * 100_000 + funcToken.Column;

                _scopeStack.Push(scopeKey);

                if (!ScopeDecls.ContainsKey(scopeKey))
                    ScopeDecls[scopeKey] = new HashSet<string>(StringComparer.Ordinal);

                // Record each declared parameter in the function scope.
                var fpCtx = ctx.functionParameters();
                if (fpCtx != null)
                {
                    foreach (var decl in fpCtx.declaration())
                        Record(decl.IDENTIFIER().Symbol);
                }

                // Visit the function body — VisitDeclarationStatement inside it will
                // record local declarations in this function's scope (current scope).
                VisitChildren(ctx);

                _scopeStack.Pop();
                return null;
            }

            return VisitChildren(ctx);
        }

        /// <summary>
        /// Declaration statement — records the declared name in the current scope
        /// (global scope or the enclosing function scope).
        /// </summary>
        public override object? VisitDeclarationStatement(
            CgScriptParser.DeclarationStatementContext ctx)
        {
            Record(ctx.declaration().IDENTIFIER().Symbol);
            return VisitChildren(ctx);
        }

        /// <summary>For-each loop variable — recorded in current scope.</summary>
        public override object? VisitForEachControl(
            CgScriptParser.ForEachControlContext ctx)
        {
            Record(ctx.IDENTIFIER().Symbol);
            return VisitChildren(ctx);
        }

        /// <summary>
        /// Classic for-loop — the optional init declaration is recorded in the
        /// current scope (if an <c>assignment</c> was used instead of a
        /// <c>declaration</c>, there is nothing to record here).
        /// </summary>
        public override object? VisitForClassicControl(
            CgScriptParser.ForClassicControlContext ctx)
        {
            var decl = ctx.declaration();
            if (decl != null)
                Record(decl.IDENTIFIER().Symbol);

            return VisitChildren(ctx);
        }

        /// <summary>
        /// Try/catch — the catch variable lives exclusively in its own scope key
        /// (keyed on the CATCH token position).  We push that scope around the
        /// catch-variable recording <em>and</em> the catch-body visit so that any
        /// further declarations inside the catch body are also attributed to the
        /// catch scope rather than the global scope.  The try body is visited
        /// before the push so it sees the outer scope.
        /// </summary>
        public override object? VisitTryStatement(
            CgScriptParser.TryStatementContext ctx)
        {
            // Visit the try body in the current (outer) scope.
            Visit(ctx.statement(0));

            // Compute a unique scope key for this catch clause.
            var catchKeyToken = ctx.CATCH().Symbol;
            var scopeKey      = catchKeyToken.Line * 100_000 + catchKeyToken.Column;

            _scopeStack.Push(scopeKey);

            if (!ScopeDecls.ContainsKey(scopeKey))
                ScopeDecls[scopeKey] = new HashSet<string>(StringComparer.Ordinal);

            // Record the catch variable in the catch scope.
            Record(ctx.IDENTIFIER().Symbol);

            // Visit the catch body — still with the catch scope on the stack.
            Visit(ctx.statement(1));

            _scopeStack.Pop();
            return null;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Pass 2 — RefVisitor
    // Walks the same tree and emits a SymbolRef for every occurrence of every
    // identifier, grouped by (scopeKey, name).
    // ═════════════════════════════════════════════════════════════════════════════

    private sealed class RefVisitor : CgScriptParserBaseVisitor<object?>
    {
        private readonly Dictionary<int, HashSet<string>>                 _scopeDecls;
        private readonly Dictionary<(int scope, string name), IToken>     _declTokens;

        private readonly Stack<int> _scopeStack = new Stack<int>();

        /// <summary>All symbol references grouped by (scopeKey, name).</summary>
        public Dictionary<(int scope, string name), List<SymbolRef>> Groups { get; }
            = new Dictionary<(int scope, string name), List<SymbolRef>>();

        public RefVisitor(
            Dictionary<int, HashSet<string>>             scopeDecls,
            Dictionary<(int scope, string name), IToken> declTokens)
        {
            _scopeDecls = scopeDecls;
            _declTokens = declTokens;
            _scopeStack.Push(0);
        }

        // ── Scope resolution ──────────────────────────────────────────────────────

        /// <summary>
        /// Resolves a name to the innermost scope that declares it by walking the
        /// scope stack from top (innermost) to bottom (global).
        /// Defaults to 0 (global) when no declaring scope is found.
        /// </summary>
        private int ResolveScope(string name)
        {
            foreach (var scope in _scopeStack)   // Stack<T> enumerates top→bottom
            {
                if (_scopeDecls.TryGetValue(scope, out var set) && set.Contains(name))
                    return scope;
            }

            return 0;
        }

        // ── Reference recording ───────────────────────────────────────────────────

        private void AddRef(IToken token, bool isDeclaration)
        {
            var name  = token.Text;
            var scope = ResolveScope(name);
            var key   = (scope, name);

            if (!Groups.TryGetValue(key, out var list))
            {
                list = new List<SymbolRef>();
                Groups[key] = list;
            }

            list.Add(new SymbolRef(token.Line, token.Column, token.Text.Length, isDeclaration));
        }

        // ── Visitor overrides ─────────────────────────────────────────────────────

        /// <summary>
        /// Function literal — pushes a new scope so that parameter references inside
        /// the body resolve to the function scope.  Parameters are emitted as
        /// declaration refs before visiting the body.
        /// </summary>
        public override object? VisitPrimaryExpr(CgScriptParser.PrimaryExprContext ctx)
        {
            if (ctx.FUNCTION() != null)
            {
                var funcToken = ctx.FUNCTION().Symbol;
                var scopeKey  = funcToken.Line * 100_000 + funcToken.Column;

                _scopeStack.Push(scopeKey);

                // Emit declaration refs for every parameter.
                var fpCtx = ctx.functionParameters();
                if (fpCtx != null)
                {
                    foreach (var decl in fpCtx.declaration())
                        AddRef(decl.IDENTIFIER().Symbol, isDeclaration: true);
                }

                VisitChildren(ctx);

                _scopeStack.Pop();
                return null;
            }

            // new ClassName(...) — the class-name IDENTIFIER is a type, not a variable.
            // We still visit children so that constructor arguments are analysed.
            if (ctx.NEW() != null)
                return VisitChildren(ctx);

            // Bare IDENTIFIER (variable reference, optionally followed by ++ / --).
            var idNode = ctx.IDENTIFIER();
            if (idNode != null)
            {
                AddRef(idNode.Symbol, isDeclaration: false);
                return null;   // IDENTIFIER and optional INC/DEC are terminals — nothing else to visit
            }

            return VisitChildren(ctx);
        }

        /// <summary>Declaration statement — the declared name is a definition site.</summary>
        public override object? VisitDeclarationStatement(
            CgScriptParser.DeclarationStatementContext ctx)
        {
            AddRef(ctx.declaration().IDENTIFIER().Symbol, isDeclaration: true);
            return VisitChildren(ctx);
        }

        /// <summary>For-each loop variable — declaration site.</summary>
        public override object? VisitForEachControl(
            CgScriptParser.ForEachControlContext ctx)
        {
            AddRef(ctx.IDENTIFIER().Symbol, isDeclaration: true);
            return VisitChildren(ctx);
        }

        /// <summary>Classic for-loop init declaration — declaration site (when present).</summary>
        public override object? VisitForClassicControl(
            CgScriptParser.ForClassicControlContext ctx)
        {
            var decl = ctx.declaration();
            if (decl != null)
                AddRef(decl.IDENTIFIER().Symbol, isDeclaration: true);

            return VisitChildren(ctx);
        }

        /// <summary>
        /// Try/catch — mirrors the DeclCollector strategy: push the catch scope
        /// around both the catch-variable declaration and the catch body so that
        /// references to the catch variable inside the body resolve correctly.
        /// </summary>
        public override object? VisitTryStatement(
            CgScriptParser.TryStatementContext ctx)
        {
            // Visit try body with the outer scope.
            Visit(ctx.statement(0));

            var catchKeyToken = ctx.CATCH().Symbol;
            var scopeKey      = catchKeyToken.Line * 100_000 + catchKeyToken.Column;

            _scopeStack.Push(scopeKey);

            // Catch variable is a declaration site.
            AddRef(ctx.IDENTIFIER().Symbol, isDeclaration: true);

            // Visit catch body with the catch scope on the stack.
            Visit(ctx.statement(1));

            _scopeStack.Pop();
            return null;
        }
    }
}
