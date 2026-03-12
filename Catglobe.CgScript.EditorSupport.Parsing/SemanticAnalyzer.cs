using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>
/// Two-pass semantic analyzer for CgScript.
/// <para>
/// <b>Pass 1</b> walks the parse tree to collect every globally-scoped variable name
/// (skipping inside <c>function</c> literal bodies) and for-each loop variables.
/// Duplicate declarations are reported as <see cref="DiagnosticSeverity.Warning"/> (CGS001).
/// </para>
/// <para>
/// <b>Pass 2</b> walks the tree again to validate identifier usages against the
/// collected globals, the current function's parameters, and the three sets of
/// known external names.
/// </para>
/// </summary>
public sealed class SemanticAnalyzer : CgScriptParserBaseVisitor<object?>
{
   // ── Known-name sets (supplied at construction time) ──────────────────────────
   private readonly HashSet<string> _knownFunctions;
   private readonly HashSet<string> _knownObjects;
   private readonly HashSet<string> _knownConstants;

   // ── Object member definitions for property/method validation ────────────────
   private readonly IReadOnlyDictionary<string, ObjectMemberInfo>? _objectDefinitions;

   // ── Pass-1 result (populated before Pass 2 begins) ──────────────────────────
   private HashSet<string>         _globalVars  = new(StringComparer.Ordinal);
   private Dictionary<string, int> _globalVarLines = new(StringComparer.Ordinal);

   // ── Variable type tracking (var name → declared type name) ──────────────────
   private Dictionary<string, string> _varTypes = new(StringComparer.Ordinal);

   // ── Pass-2 scope state ───────────────────────────────────────────────────────
   private int              _functionDepth;
   private HashSet<string>? _functionParams; // null = global scope
   private HashSet<string>  _extraLocals = new(StringComparer.Ordinal); // catch vars

   // ── Pass-2 read tracking ─────────────────────────────────────────────────────
   private readonly HashSet<string> _readVars = new(StringComparer.Ordinal);

   // ── Accumulated diagnostics ──────────────────────────────────────────────────
   private readonly List<Diagnostic> _diagnostics = new();

   // ── Constructor ──────────────────────────────────────────────────────────────

   /// <summary>
   /// Initialises a new <see cref="SemanticAnalyzer"/>.
   /// All three name collections may be empty but must not be <c>null</c>.
   /// </summary>
   public SemanticAnalyzer(
      IEnumerable<string> knownFunctions,
      IEnumerable<string> knownObjects,
      IEnumerable<string> knownConstants,
      IReadOnlyDictionary<string, ObjectMemberInfo>? objectDefinitions = null)
   {
      _knownFunctions    = new HashSet<string>(knownFunctions, StringComparer.Ordinal);
      _knownObjects      = new HashSet<string>(knownObjects,   StringComparer.Ordinal);
      _knownConstants    = new HashSet<string>(knownConstants, StringComparer.Ordinal);
      _objectDefinitions = objectDefinitions;
   }

   // ── Static entry point ───────────────────────────────────────────────────────

   /// <summary>
   /// Runs semantic analysis on a CgScript parse tree and returns all diagnostics.
   /// </summary>
   /// <param name="tree">The root of the ANTLR4 parse tree.</param>
   /// <param name="knownFunctions">Names of built-in/runtime functions.</param>
   /// <param name="knownObjects">Names of built-in/runtime object types.</param>
   /// <param name="knownConstants">Names of built-in/runtime constants.</param>
   /// <param name="objectDefinitions">
   /// Optional member definitions for object types, used to validate property/method
   /// access and detect assignments to read-only properties.
   /// </param>
   /// <param name="globalVariableTypes">
   /// Optional map of pre-declared runtime global variables to their type names
   /// (e.g. <c>"Catglobe" → "GlobalNamespace"</c>), used to validate member access
   /// on those variables.
   /// </param>
   public static IReadOnlyList<Diagnostic> Analyze(
      IParseTree          tree,
      IEnumerable<string> knownFunctions,
      IEnumerable<string> knownObjects,
      IEnumerable<string> knownConstants,
      IReadOnlyDictionary<string, ObjectMemberInfo>? objectDefinitions = null,
      IReadOnlyDictionary<string, string>?           globalVariableTypes = null)
   {
      // ── Pass 1: collect global declarations ──────────────────────────────────
      var collector = new ScopeCollector();
      collector.Visit(tree);

      // ── Pass 2: check usages ─────────────────────────────────────────────────
      var analyzer = new SemanticAnalyzer(knownFunctions, knownObjects, knownConstants, objectDefinitions);
      analyzer._globalVars     = collector.Vars;
      analyzer._globalVarLines = collector.VarLines;
      analyzer._diagnostics.AddRange(collector.Diagnostics);

      // Seed variable types from declarations and pre-declared global variables
      foreach (var kvp in collector.VarTypes)
         analyzer._varTypes[kvp.Key] = kvp.Value;
      if (globalVariableTypes != null)
         foreach (var kvp in globalVariableTypes)
            if (!analyzer._varTypes.ContainsKey(kvp.Key))
               analyzer._varTypes[kvp.Key] = kvp.Value;

      analyzer.Visit(tree);

      // ── Post-pass: unused variable check ────────────────────────────────────
      foreach (var kvp in collector.VarLines)
      {
         var name = kvp.Key;
         var line = kvp.Value;
         if (!analyzer._readVars.Contains(name))
         {
            analyzer._diagnostics.Add(new Diagnostic(
               DiagnosticSeverity.Warning,
               $"Variable '{name}' is declared but never used",
               line, 0, name.Length,
               "CGS009"));
         }
      }

      return analyzer._diagnostics;
   }

   // ── Pass 1: global scope collector ──────────────────────────────────────────

   /// <summary>
   /// Collects all globally-scoped variable names and reports duplicate declarations.
   /// Traversal skips inside <c>function</c> literal bodies.
   /// </summary>
   private sealed class ScopeCollector : CgScriptParserBaseVisitor<object?>
   {
      private int _depth; // > 0 when inside a function-literal body

      public HashSet<string>              Vars     { get; } = new HashSet<string>(StringComparer.Ordinal);
      public Dictionary<string, int>      VarLines { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
      public Dictionary<string, string>   VarTypes { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
      public List<Diagnostic>             Diagnostics { get; } = new List<Diagnostic>();

      // Skip inside function-literal bodies by tracking nesting depth
      public override object? VisitPrimaryExpr(CgScriptParser.PrimaryExprContext ctx)
      {
         if (ctx.FUNCTION() != null)
         {
            _depth++;
            VisitChildren(ctx);
            _depth--;
            return null;
         }
         return VisitChildren(ctx);
      }

      // declarationStatement adds the variable to global scope
      public override object? VisitDeclarationStatement(
         CgScriptParser.DeclarationStatementContext ctx)
      {
         if (_depth == 0)
         {
            TryAdd(ctx.declaration().IDENTIFIER());
            // Track the declared class type for member access validation
            if (ctx.declaration().typeSpec() is CgScriptParser.ClassNameTypeContext classType)
            {
               var varName = ctx.declaration().IDENTIFIER()?.Symbol.Text;
               if (varName != null)
                  VarTypes[varName] = classType.IDENTIFIER().Symbol.Text;
            }
         }
         return VisitChildren(ctx);
      }

      // for-each loop variable is loop-scoped (runtime saves/restores it) — not a global declaration
      public override object? VisitForEachControl(CgScriptParser.ForEachControlContext ctx)
         => VisitChildren(ctx);

      // classic for-loop init declaration is also global
      public override object? VisitForClassicControl(CgScriptParser.ForClassicControlContext ctx)
      {
         if (_depth == 0)
         {
            var decl = ctx.declaration();
            if (decl != null)
            {
               TryAdd(decl.IDENTIFIER());
               // Track the declared class type for member access validation
               if (decl.typeSpec() is CgScriptParser.ClassNameTypeContext classType)
               {
                  var varName = decl.IDENTIFIER()?.Symbol.Text;
                  if (varName != null)
                     VarTypes[varName] = classType.IDENTIFIER().Symbol.Text;
               }
            }
         }
         return VisitChildren(ctx);
      }

      private void TryAdd(ITerminalNode? node)
      {
         if (node is null) return;
         var token = node.Symbol;
         if (!Vars.Add(token.Text))
         {
            Diagnostics.Add(new Diagnostic(
               DiagnosticSeverity.Warning,
               $"Illegal variable re-declaration: {token.Text}",
               token.Line,
               token.Column,
               token.Text.Length,
               "CGS001"));
         }
         else
         {
            VarLines[token.Text] = token.Line;
         }
      }
   }

   // ── Pass 2: visitor overrides ────────────────────────────────────────────────

   /// <summary>
   /// Rule 2 — classNameType must name a known object type.
   /// </summary>
   public override object? VisitClassNameType(CgScriptParser.ClassNameTypeContext ctx)
   {
      var idNode = ctx.IDENTIFIER();
      if (idNode is null) return VisitChildren(ctx);
      var token = idNode.Symbol;
      if (!_knownObjects.Contains(token.Text))
      {
         _diagnostics.Add(new Diagnostic(
            DiagnosticSeverity.Error,
            $"Unknown type '{token.Text}'",
            token.Line,
            token.Column,
            token.Text.Length,
            "CGS002"));
      }
      return VisitChildren(ctx);
   }

   /// <summary>
   /// Handles three distinct primaryExpr forms:<br/>
   /// Rule 3 — <c>new ClassName(...)</c> where ClassName is not a known object type;<br/>
   /// Function scope — pushes a new parameter scope before visiting a function literal;<br/>
   /// Rule 5 — bare IDENTIFIER that is not declared or known.
   /// </summary>
   public override object? VisitPrimaryExpr(CgScriptParser.PrimaryExprContext ctx)
   {
      // ── Rule 3: new ClassName(...) ──────────────────────────────────────────
      if (ctx.NEW() != null)
      {
         var idNode2 = ctx.IDENTIFIER();
         if (idNode2 is null) return VisitChildren(ctx);
         var token = idNode2.Symbol;
         if (!_knownObjects.Contains(token.Text))
         {
            _diagnostics.Add(new Diagnostic(
               DiagnosticSeverity.Error,
               $"Unknown type '{token.Text}'",
               token.Line,
               token.Column,
               token.Text.Length,
               "CGS003"));
         }
         return VisitChildren(ctx);
      }

      // ── Function literal: push new scope ────────────────────────────────────
      if (ctx.FUNCTION() != null)
      {
         var savedParams = _functionParams;
         var savedDepth  = _functionDepth;
         var savedLocals = _extraLocals;

         // Collect declared parameter names — start from the outer scope for closure semantics
         var newScope = new HashSet<string>(StringComparer.Ordinal);
         // Inherit outer function's parameters and catch-var locals so closures can reference them
         if (_functionParams != null)
            newScope.UnionWith(_functionParams);
         newScope.UnionWith(_extraLocals);
         var fpCtx = ctx.functionParameters();
         if (fpCtx != null)
         {
            foreach (var decl in fpCtx.declaration())
            {
               var id = decl.IDENTIFIER();
               if (id is not null)
                  newScope.Add(id.Symbol.Text);
            }
         }

         // Also collect local declarations from the function body so that usages of
         // locally-declared variables inside the function don't trigger false positives.
         var blockCtx = ctx.block();
         if (blockCtx != null)
         {
            var localCollector = new ScopeCollector();
            localCollector.Visit(blockCtx);
            foreach (var name in localCollector.Vars)
               newScope.Add(name);
         }

         _functionParams = newScope;
         _functionDepth++;
         _extraLocals = new HashSet<string>(StringComparer.Ordinal);

         // Visit params (triggers Rule 2 for classNameType) and body (Rules 4/5)
         VisitChildren(ctx);

         _functionParams = savedParams;
         _functionDepth  = savedDepth;
         _extraLocals    = savedLocals;
         return null;
      }

      // ── Rule 5: bare IDENTIFIER (variable reference) ─────────────────────────
      var idNode = ctx.IDENTIFIER();
      if (idNode != null)
      {
         var token = idNode.Symbol;
         var name  = token.Text;

         // Suppress if a higher-level rule already handles this identifier:
         //   • direct function call  → Rule 4 reports "Unknown function"
         //   • LHS of member access  → spec exemption
         if (!IsKnown(name) && !ShouldSkipIdentifierCheck(ctx))
         {
            _diagnostics.Add(new Diagnostic(
               DiagnosticSeverity.Warning,
               $"Undefined variable '{name}'",
               token.Line,
               token.Column,
               token.Text.Length,
               "CGS005"));
         }
         else
         {
            // Track read usages for unused-variable analysis.
            // Only count as a global read when the identifier is not shadowed by
            // a function parameter or catch variable in the current scope.
            if (_globalVars.Contains(name)
                && (_functionParams == null || !_functionParams.Contains(name))
                && !_extraLocals.Contains(name))
               _readVars.Add(name);

            // Rule 8 — use before define (global scope only)
            if (_functionDepth == 0
                && _globalVarLines.TryGetValue(name, out var declLine)
                && token.Line < declLine
                && !ShouldSkipIdentifierCheck(ctx))
            {
               _diagnostics.Add(new Diagnostic(
                  DiagnosticSeverity.Warning,
                  $"Variable '{name}' used before its declaration on line {declLine}",
                  token.Line,
                  token.Column,
                  token.Text.Length,
                  "CGS008"));
            }
         }
         // IDENTIFIER and optional INC/DEC are terminals — nothing further to visit
         return null;
      }

      return VisitChildren(ctx);
   }

   /// <summary>
   /// For-each loop: the loop variable is loop-scoped (the runtime saves/restores it),
   /// so it is NOT in global scope. Push it as a temporary extra local for the loop body.
   /// Classic for-init declarations remain global scope.
   /// </summary>
   public override object? VisitForStatement(CgScriptParser.ForStatementContext ctx)
   {
      if (ctx.forControl() is CgScriptParser.ForClassicControlContext)
      {
         var token = ctx.Start;
         _diagnostics.Add(new Diagnostic(
            DiagnosticSeverity.Information,
            "C-style for loop can be converted to native CgScript style: for(<var> for <from>; <to>)",
            token.Line,
            token.Column,
            token.Text.Length,
            "CGS015"));
      }

      if (ctx.forControl() is CgScriptParser.ForEachControlContext feCtx)
      {
         // Visit the iteration expressions in the current (outer) scope
         Visit(feCtx.expression(0));
         Visit(feCtx.expression(1));

         var idNode = feCtx.IDENTIFIER();
         if (idNode is not null)
         {
            var varName = idNode.Symbol.Text;
            _extraLocals.Add(varName);
            Visit(ctx.statement());
            _extraLocals.Remove(varName);
         }
         else
         {
            Visit(ctx.statement());
         }
         return null;
      }
      return VisitChildren(ctx);
   }

   /// <summary>
   /// Validates function calls and member access (property and method) against known
   /// object type definitions, including chained access like <c>Catglobe.Json.Parse()</c>.
   /// </summary>
   public override object? VisitPostfixExpr(CgScriptParser.PostfixExprContext ctx)
   {
      // Pattern: postfixExpr LPAREN parameters RPAREN  (direct call, not member-method call)
      if (ctx.LPAREN() != null && ctx.DOT() == null)
      {
         var inner = ctx.postfixExpr();
         if (inner != null)
         {
            // The callee must be a bare primary identifier with no intermediate postfix ops
            var primary = inner.primaryExpr();
            if (primary                 != null
                && primary.IDENTIFIER() != null
                && primary.NEW()        == null
                && primary.FUNCTION()   == null
                && inner.LPAREN()       == null
                && inner.DOT()          == null
                && inner.LBRACKET()     == null)
            {
               var token = primary.IDENTIFIER().Symbol;
               if (!IsKnown(token.Text))
               {
                  _diagnostics.Add(new Diagnostic(
                     DiagnosticSeverity.Warning,
                     $"Unknown function '{token.Text}'",
                     token.Line,
                     token.Column,
                     token.Text.Length,
                     "CGS004"));
               }
            }
         }
      }

      // Pattern: postfixExpr DOT IDENTIFIER (LPAREN ...)?  (member access)
      if (ctx.DOT() != null && _objectDefinitions != null)
      {
         var memberIdNode = ctx.IDENTIFIER();
         if (memberIdNode != null)
         {
            var baseType = TryResolveBaseType(ctx.postfixExpr());
            if (baseType != null && _objectDefinitions.TryGetValue(baseType, out var members))
            {
               var memberName  = memberIdNode.Symbol.Text;
               bool isCallExpr = ctx.LPAREN() != null;

               if (isCallExpr)
               {
                  if (!members.HasMethod(memberName))
                  {
                     _diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        $"'{baseType}' does not have a method named '{memberName}'",
                        memberIdNode.Symbol.Line,
                        memberIdNode.Symbol.Column,
                        memberName.Length,
                        "CGS017"));
                  }
               }
               else
               {
                  if (!members.Properties.ContainsKey(memberName))
                  {
                     _diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        $"'{baseType}' does not have a property named '{memberName}'",
                        memberIdNode.Symbol.Line,
                        memberIdNode.Symbol.Column,
                        memberName.Length,
                        "CGS016"));
                  }
                  else
                  {
                     if (members.Properties.TryGetValue(memberName, out var hasSetter)
                         && !hasSetter
                         && IsLhsOfAssignment(ctx))
                     {
                        _diagnostics.Add(new Diagnostic(
                           DiagnosticSeverity.Error,
                           $"'{memberName}' is read-only",
                           memberIdNode.Symbol.Line,
                           memberIdNode.Symbol.Column,
                           memberName.Length,
                           "CGS018"));
                     }
                  }
               }
            }
         }
      }

      return VisitChildren(ctx);
   }

   /// <summary>
   /// Rule 6 — a bare semicolon (empty statement) is never intentional in CgScript.
   /// Warns whether it appears directly as an if/while/for body (<c>if (x) ;</c>)
   /// or as a stray statement after a block (<c>if (x) {};</c>) or on its own (<c>;;</c>).
   /// </summary>
   public override object? VisitEmptyStatement(CgScriptParser.EmptyStatementContext ctx)
   {
      var token = ctx.SEMI().Symbol;
      _diagnostics.Add(new Diagnostic(
         DiagnosticSeverity.Warning,
         "Empty statement has no effect",
         token.Line,
         token.Column,
         token.Text.Length,
         "CGS006"));
      return null;
   }

   /// <summary>
   /// Manually visits try/catch to scope the catch variable to the catch body only,
   /// preventing false "Undefined variable" warnings for the catch identifier.
   /// If the catch variable name matches a global declaration, that global is
   /// considered "used" (the catch assigns to it conceptually).
   /// </summary>
   public override object? VisitTryStatement(CgScriptParser.TryStatementContext ctx)
   {
      // Visit the try body with the current scope
      Visit(ctx.statement(0));

      // Add the catch variable as a temporary local for the catch body only
      var catchToken = ctx.IDENTIFIER().Symbol;
      var catchVar   = catchToken.Text;
      _extraLocals.Add(catchVar);

      // Treat catch (exx) as a "use" of any same-named global declaration so that
      // `object exx; ... catch (exx) { }` does not trigger "declared but never used".
      if (_globalVars.Contains(catchVar)
          && (_functionParams == null || !_functionParams.Contains(catchVar)))
         _readVars.Add(catchVar);

      Visit(ctx.statement(1));

      _extraLocals.Remove(catchVar);
      return null;
   }

   // ── Helpers ──────────────────────────────────────────────────────────────────

   private bool IsKnown(string name)
      => _globalVars.Contains(name)
      || (_functionParams != null && _functionParams.Contains(name))
      || _extraLocals.Contains(name)
      || _knownFunctions.Contains(name)
      || _knownObjects.Contains(name)
      || _knownConstants.Contains(name);

   /// <summary>
   /// Resolves the declared type of the base expression in a member-access chain.
   /// Returns the type name when the base is a simple, undecorated global variable
   /// whose type was recorded at declaration time, or <c>null</c> when the type
   /// cannot be determined (chained access, function parameter, etc.).
   /// </summary>
   private string? TryResolveBaseType(CgScriptParser.PostfixExprContext? baseCtx)
   {
      if (baseCtx == null) return null;

      var primary = baseCtx.primaryExpr();
      if (primary != null)
      {
         var idNode = primary.IDENTIFIER();
         if (idNode == null) return null; // Not a bare identifier

         var varName = idNode.Symbol.Text;

         // If the variable is shadowed by a function parameter or catch variable,
         // its type is unknown in this scope.
         if (_functionParams != null && _functionParams.Contains(varName)) return null;
         if (_extraLocals.Contains(varName)) return null;

         return _varTypes.TryGetValue(varName, out var typeName) ? typeName : null;
      }

      // Multi-level: baseCtx is itself a property access (e.g. Catglobe.Json)
      if (baseCtx.DOT() != null && baseCtx.LPAREN() == null && _objectDefinitions != null)
      {
         var propName = baseCtx.IDENTIFIER()?.Symbol.Text;
         if (propName != null)
         {
            var innerType = TryResolveBaseType(baseCtx.postfixExpr());
            if (innerType != null
                && _objectDefinitions.TryGetValue(innerType, out var innerMembers)
                && innerMembers.PropertyReturnTypes.TryGetValue(propName, out var returnType)
                && !string.IsNullOrEmpty(returnType))
            {
               return returnType;
            }
         }
      }

      return null;
   }

   /// <summary>
   /// Returns <c>true</c> when <paramref name="ctx"/> is the direct left-hand side
   /// of an assignment expression (<c>exprOrAssign: expression ASSIGN expression</c>).
   /// </summary>
   private static bool IsLhsOfAssignment(CgScriptParser.PostfixExprContext ctx)
   {
      Antlr4.Runtime.Tree.IParseTree? node = ctx;
      while (node?.Parent != null)
      {
         var parent = node.Parent;
         if (parent is CgScriptParser.ExprOrAssignContext assignCtx)
         {
            if (assignCtx.ASSIGN() == null) return false;
            // node is the direct child of exprOrAssign; it is the LHS when it matches expression(0)
            return node == assignCtx.expression(0);
         }
         // Stop searching beyond a statement boundary
         if (parent is CgScriptParser.StatementContext) return false;
         node = parent;
      }
      return false;
   }

   /// <summary>
   /// Returns <see langword="true"/> when the identifier in <paramref name="ctx"/>
   /// should not trigger a Rule 5 "Undefined variable" warning because either:
   /// <list type="bullet">
   ///   <item>It is the callee of a direct function call (Rule 4 handles that case).</item>
   ///   <item>It is the LHS of a member-access expression (spec exemption).</item>
   /// </list>
   /// The parent of a <c>primaryExpr</c> is always the base-case
   /// <c>postfixExpr</c> wrapper; the grandparent is the outer postfix operator.
   /// </summary>
   private static bool ShouldSkipIdentifierCheck(CgScriptParser.PrimaryExprContext ctx)
   {
      if (ctx.Parent is not CgScriptParser.PostfixExprContext wrapper) return false;

      if (wrapper.Parent is CgScriptParser.PostfixExprContext outer)
      {
         // Direct call: outer has LPAREN but no DOT
         if (outer.LPAREN() != null && outer.DOT() == null) return true;
         // Member access: outer has DOT
         if (outer.DOT() != null) return true;
      }

      return false;
   }

   /// <summary>
   /// Rule 7 — unreachable code after an unconditional jump in a block.
   /// </summary>
   public override object? VisitBlock(CgScriptParser.BlockContext ctx)
   {
      var statements = ctx.statement();
      bool seenJump = false;
      foreach (var stmt in statements)
      {
         if (seenJump)
         {
            var token = stmt.Start;
            _diagnostics.Add(new Diagnostic(
               DiagnosticSeverity.Warning,
               "Unreachable code",
               token.Line,
               token.Column,
               token.Text.Length,
               "CGS007"));
            // Report only the first unreachable statement to avoid cascading noise
            break;
         }

         Visit(stmt);

         seenJump = stmt is CgScriptParser.ReturnStatementContext
                         or CgScriptParser.BreakStatementContext
                         or CgScriptParser.ContinueStatementContext
                         or CgScriptParser.ThrowStatementContext;
      }
      return null;
   }
}
