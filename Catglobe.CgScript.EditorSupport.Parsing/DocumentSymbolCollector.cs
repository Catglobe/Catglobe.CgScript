using Antlr4.Runtime.Tree;

namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>
/// Lightweight record that carries the information needed to produce either a
/// <c>DocumentSymbol</c> or a <c>SymbolInformation</c> LSP response.
/// All line numbers are 1-based (ANTLR convention); columns are 0-based.
/// </summary>
public sealed record DocumentSymbolInfo(
   string Name,
   string Kind,         // "function" or "variable"
   string TypeName,     // CgScript type keyword or class name (e.g. "number", "Tenant")
   int    StartLine,
   int    StartColumn,
   int    EndLine,
   int    EndColumn,
   int    NameLine,
   int    NameColumn,
   int    NameLength);

/// <summary>
/// Single-pass parse-tree visitor that collects all top-level variable and
/// function declarations from a CgScript program.
/// "Top-level" means declarations that are not nested inside a
/// <c>function</c> literal body (i.e. global scope declarations only).
/// </summary>
public static class DocumentSymbolCollector
{
   /// <summary>
   /// Walks <paramref name="tree"/> and returns one <see cref="DocumentSymbolInfo"/>
   /// for every top-level declaration found.
   /// </summary>
   public static IReadOnlyList<DocumentSymbolInfo> Collect(IParseTree tree)
      => CollectCore(tree, globalOnly: true);

   /// <summary>
   /// Collects declarations at all nesting depths — used for variable type resolution.
   /// </summary>
   public static IReadOnlyList<DocumentSymbolInfo> CollectAll(IParseTree tree)
      => CollectCore(tree, globalOnly: false);

   /// <summary>
   /// Collects variable declarations that are visible at the given cursor position.
   /// Global-scope variables are always included.  Function parameters are included
   /// only when the cursor falls within the body of their enclosing function literal,
   /// and they shadow any global variable with the same name.
   /// </summary>
   /// <param name="tree">The parse tree to walk.</param>
   /// <param name="cursorLine">1-based line number (ANTLR convention).</param>
   /// <param name="cursorColumn">0-based column number (ANTLR convention).</param>
   public static IReadOnlyList<DocumentSymbolInfo> CollectAtPosition(
      IParseTree tree, int cursorLine, int cursorColumn)
   {
      // Pass 1: global-scope variables (always visible)
      var globals = CollectCore(tree, globalOnly: true);

      // Pass 2: parameters from every function literal that encloses the cursor
      var paramVisitor = new ScopedParamCollector(cursorLine, cursorColumn);
      paramVisitor.Visit(tree);
      var parameters = paramVisitor.Symbols;

      if (parameters.Count == 0)
         return globals;

      // Merge: parameters shadow globals that share the same name
      var result = new Dictionary<string, DocumentSymbolInfo>(StringComparer.Ordinal);
      foreach (var sym in globals)
         result[sym.Name] = sym;
      foreach (var sym in parameters)
         result[sym.Name] = sym;   // inner-scope parameter wins

      return [.. result.Values];
   }

   private static IReadOnlyList<DocumentSymbolInfo> CollectCore(IParseTree tree, bool globalOnly)
   {
      var visitor = new Collector(globalOnly);
      visitor.Visit(tree);
      return visitor.Symbols;
   }

   // ── Internal visitor ─────────────────────────────────────────────────────────

   private sealed class Collector : CgScriptParserBaseVisitor<object?>
   {
      private readonly bool _globalOnly;
      /// <summary>Nesting depth inside function-literal bodies; 0 == global scope.</summary>
      private int _depth;

      public List<DocumentSymbolInfo> Symbols { get; } = [];

      public Collector(bool globalOnly) => _globalOnly = globalOnly;

      // ── Track function-literal nesting ────────────────────────────────────────

      /// <summary>
      /// Increments <see cref="_depth"/> when entering a function literal so that
      /// declarations inside the body are not included in the document symbols.
      /// </summary>
      public override object? VisitPrimaryExpr(CgScriptParser.PrimaryExprContext ctx)
      {
         if (ctx.FUNCTION() is not null)
         {
            _depth++;
            VisitChildren(ctx);
            _depth--;
            return null;
         }

         return VisitChildren(ctx);
      }

      // ── Collect top-level declarations ────────────────────────────────────────

      /// <summary>
      /// Records global-scope declaration statements as document symbols.
      /// Declarations typed as <c>function</c> are classified as functions;
      /// all others are classified as variables.
      /// </summary>
      public override object? VisitDeclarationStatement(
         CgScriptParser.DeclarationStatementContext ctx)
      {
         if (_depth == 0 || !_globalOnly)
         {
            var decl    = ctx.declaration();
            var idToken = decl.IDENTIFIER().Symbol;

            // A declaration whose typeSpec is the FUNCTION keyword represents a
            // function-valued variable — display it as a function symbol.
            bool isFunction = decl.typeSpec() is CgScriptParser.FunctionTypeContext;

            var stopToken = ctx.Stop;
            int endLine   = stopToken?.Line   ?? ctx.Start.Line;
            int endCol    = stopToken is not null
                               ? stopToken.Column + stopToken.Text.Length
                               : ctx.Start.Column;

            Symbols.Add(new DocumentSymbolInfo(
               Name:        idToken.Text,
               Kind:        isFunction ? "function" : "variable",
               TypeName:    decl.typeSpec().GetText(),
               StartLine:   ctx.Start.Line,
               StartColumn: ctx.Start.Column,
               EndLine:     endLine,
               EndColumn:   endCol,
               NameLine:    idToken.Line,
               NameColumn:  idToken.Column,
               NameLength:  idToken.Text.Length));
         }

         return VisitChildren(ctx);
      }

      /// <summary>
      /// Collects function literal parameters as typed symbols so that
      /// type-resolution and completion can find their declared types.
      /// Only collected when <c>CollectAll</c> is used (not for the document outline).
      /// </summary>
      public override object? VisitFunctionParameters(
         CgScriptParser.FunctionParametersContext ctx)
      {
         if (!_globalOnly)
         {
            foreach (var decl in ctx.declaration())
            {
               var idToken = decl.IDENTIFIER()?.Symbol;
               if (idToken is null) continue;

               var typeText = decl.typeSpec()?.GetText() ?? "";
               if (string.IsNullOrEmpty(typeText)) continue;

               var stopToken = decl.Stop;
               Symbols.Add(new DocumentSymbolInfo(
                  Name:        idToken.Text,
                  Kind:        "parameter",
                  TypeName:    typeText,
                  StartLine:   decl.Start.Line,
                  StartColumn: decl.Start.Column,
                  EndLine:     stopToken?.Line   ?? decl.Start.Line,
                  EndColumn:   stopToken is not null
                                  ? stopToken.Column + stopToken.Text.Length
                                  : decl.Start.Column,
                  NameLine:    idToken.Line,
                  NameColumn:  idToken.Column,
                  NameLength:  idToken.Text.Length));
            }
         }

         // Do not recurse into children: parameter declarations are not statements
         // and contain nothing else the collector needs to visit.
         return null;
      }
   }

   // ── Scope-aware parameter visitor ────────────────────────────────────────────

   /// <summary>
   /// Collects function parameters only for function literals whose range contains
   /// the cursor position.  Used by <see cref="CollectAtPosition"/>.
   /// </summary>
   private sealed class ScopedParamCollector : CgScriptParserBaseVisitor<object?>
   {
      private readonly int _cursorLine;   // 1-based
      private readonly int _cursorColumn; // 0-based
      private bool _currentFunctionContainsCursor;

      public List<DocumentSymbolInfo> Symbols { get; } = [];

      public ScopedParamCollector(int cursorLine, int cursorColumn)
      {
         _cursorLine   = cursorLine;
         _cursorColumn = cursorColumn;
      }

      public override object? VisitPrimaryExpr(CgScriptParser.PrimaryExprContext ctx)
      {
         if (ctx.FUNCTION() is not null)
         {
            bool saved = _currentFunctionContainsCursor;
            _currentFunctionContainsCursor = IsPositionInRange(ctx);
            VisitChildren(ctx);
            _currentFunctionContainsCursor = saved;
            return null;
         }
         return VisitChildren(ctx);
      }

      public override object? VisitFunctionParameters(CgScriptParser.FunctionParametersContext ctx)
      {
         if (_currentFunctionContainsCursor)
         {
            foreach (var decl in ctx.declaration())
            {
               var idToken  = decl.IDENTIFIER()?.Symbol;
               if (idToken is null) continue;
               var typeText = decl.typeSpec()?.GetText() ?? "";
               if (string.IsNullOrEmpty(typeText)) continue;

               var stopToken = decl.Stop;
               Symbols.Add(new DocumentSymbolInfo(
                  Name:        idToken.Text,
                  Kind:        "parameter",
                  TypeName:    typeText,
                  StartLine:   decl.Start.Line,
                  StartColumn: decl.Start.Column,
                  EndLine:     stopToken?.Line   ?? decl.Start.Line,
                  EndColumn:   stopToken is not null
                                  ? stopToken.Column + stopToken.Text.Length
                                  : decl.Start.Column,
                  NameLine:    idToken.Line,
                  NameColumn:  idToken.Column,
                  NameLength:  idToken.Text.Length));
            }
         }
         // Do not recurse — parameter declarations contain nothing else to visit.
         return null;
      }

      /// <summary>Returns true when the cursor falls within the span of <paramref name="ctx"/>.</summary>
      private bool IsPositionInRange(Antlr4.Runtime.ParserRuleContext ctx)
      {
         int startLine = ctx.Start.Line;
         int stopLine  = ctx.Stop?.Line ?? startLine;

         if (_cursorLine < startLine || _cursorLine > stopLine) return false;
         if (_cursorLine == startLine && _cursorColumn < ctx.Start.Column) return false;
         if (_cursorLine == stopLine)
         {
            int stopEndCol = ctx.Stop is not null
                                ? ctx.Stop.Column + ctx.Stop.Text.Length
                                : ctx.Start.Column;
            if (_cursorColumn > stopEndCol) return false;
         }

         return true;
      }
   }
}
