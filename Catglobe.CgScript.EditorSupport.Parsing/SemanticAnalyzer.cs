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
   private readonly HashSet<string> _knownGlobalVariables;

   // ── Object member definitions for property/method validation ────────────────
   private readonly IReadOnlyDictionary<string, ObjectMemberInfo>? _objectDefinitions;

   // ── Function definitions for argument type/arity validation ─────────────────
   private readonly IReadOnlyDictionary<string, FunctionInfo>? _functionDefinitions;

   // ── Pass-1 result (populated before Pass 2 begins) ──────────────────────────
   private HashSet<string>         _globalVars  = new(StringComparer.Ordinal);
   private Dictionary<string, (int Line, int Column)> _globalVarLines = new(StringComparer.Ordinal);

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
   /// All name collections may be empty but must not be <c>null</c>.
   /// </summary>
   public SemanticAnalyzer(
      IEnumerable<string> knownFunctions,
      IEnumerable<string> knownObjects,
      IEnumerable<string> knownConstants,
      IReadOnlyDictionary<string, ObjectMemberInfo>? objectDefinitions  = null,
      IEnumerable<string>?                           knownGlobalVariables = null,
      IReadOnlyDictionary<string, FunctionInfo>?     functionDefinitions  = null)
   {
      _knownFunctions    = new HashSet<string>(knownFunctions, StringComparer.Ordinal);
      _knownObjects      = new HashSet<string>(knownObjects,   StringComparer.Ordinal);
      _knownConstants    = new HashSet<string>(knownConstants, StringComparer.Ordinal);
      _knownGlobalVariables = knownGlobalVariables is null
         ? new HashSet<string>(StringComparer.Ordinal)
         : new HashSet<string>(knownGlobalVariables, StringComparer.Ordinal);
      _objectDefinitions   = objectDefinitions;
      _functionDefinitions = functionDefinitions;
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
   /// <param name="functionDefinitions">
   /// Optional map of old-style function names to their signature info, used to
   /// validate call argument types and arity.
   /// </param>
   public static IReadOnlyList<Diagnostic> Analyze(
      IParseTree          tree,
      IEnumerable<string> knownFunctions,
      IEnumerable<string> knownObjects,
      IEnumerable<string> knownConstants,
      IReadOnlyDictionary<string, ObjectMemberInfo>? objectDefinitions   = null,
      IReadOnlyDictionary<string, string>?           globalVariableTypes = null,
      IReadOnlyDictionary<string, FunctionInfo>?     functionDefinitions = null)
   {
      // ── Pass 1: collect global declarations ──────────────────────────────────
      var collector = new ScopeCollector();
      collector.Visit(tree);

      // ── Pass 2: check usages ─────────────────────────────────────────────────
      var analyzer = new SemanticAnalyzer(knownFunctions, knownObjects, knownConstants, objectDefinitions, globalVariableTypes?.Keys, functionDefinitions);
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
         var (line, col) = kvp.Value;
         if (!analyzer._readVars.Contains(name))
         {
            analyzer._diagnostics.Add(new Diagnostic(
               DiagnosticSeverity.Warning,
               $"Variable '{name}' is declared but never used",
               line, col, name.Length,
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
      public Dictionary<string, (int Line, int Column)> VarLines { get; } = new Dictionary<string, (int Line, int Column)>(StringComparer.Ordinal);
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
            VarLines[token.Text] = (token.Line, token.Column);
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
   /// CGS020 — checks that a declaration initializer's inferred type is compatible
   /// with the declared type (e.g. <c>number a = "asdf"</c> is flagged).
   /// </summary>
   public override object? VisitDeclarationStatement(CgScriptParser.DeclarationStatementContext ctx)
   {
      var decl     = ctx.declaration();
      var typeSpec = decl?.typeSpec();
      var init     = decl?.declarationInitializer();

      if (typeSpec != null && init != null)
      {
         var declaredName = GetDeclaredTypeName(typeSpec);
         var inferredType = TryInferType(init.expression());

         if (declaredName != null && inferredType != null)
         {
            var declaredCanon = MapToCanonical(declaredName);
            if (declaredCanon != null && !IsTypeCompatible(declaredCanon, inferredType))
            {
               var expr       = init.expression();
               var startToken = expr.Start;
               _diagnostics.Add(new Diagnostic(
                  DiagnosticSeverity.Error,
                  $"Invalid data type '{inferredType}', expect '{declaredName}'",
                  startToken.Line,
                  startToken.Column,
                  TokenSpanLength(startToken, expr.Stop),
                  "CGS020"));
            }
         }
      }

      return VisitChildren(ctx);
   }

   /// <summary>
   /// CGS021 — checks that both branches of a ternary expression (<c>? :</c>)
   /// produce compatible types (e.g. <c>true ? new Dictionary() : 1</c> is flagged).
   /// </summary>
   public override object? VisitExpression(CgScriptParser.ExpressionContext ctx)
   {
      if (ctx.QMARK() != null)
      {
         var thenType = TryInferType(ctx.subExpression(1));
         var elseType = TryInferType(ctx.subExpression(2));

         if (thenType != null && elseType != null && !IsTypeCompatible(thenType, elseType))
         {
            var thenExpr = ctx.subExpression(1);
            var elseExpr = ctx.subExpression(2);
            var startTok = thenExpr.Start;
            _diagnostics.Add(new Diagnostic(
               DiagnosticSeverity.Error,
               "Expression should return same data type",
               startTok.Line,
               startTok.Column,
               TokenSpanLength(startTok, elseExpr.Stop ?? elseExpr.Start),
               "CGS021"));
         }
      }

      return VisitChildren(ctx);
   }

   /// <summary>
   /// Returns the number of characters spanned from <paramref name="start"/> to
   /// <paramref name="stop"/> (inclusive) when both tokens are on the same line.
   /// Falls back to the text length of <paramref name="start"/> when <paramref name="stop"/>
   /// is <c>null</c> or on a different line.
   /// </summary>
   private static int TokenSpanLength(IToken start, IToken? stop)
   {
      if (stop != null && stop.Line == start.Line)
         return stop.StopIndex - start.StartIndex + 1;
      return System.Math.Max(1, start.Text?.Length ?? 1);
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
         else if (_objectDefinitions != null
                  && _objectDefinitions.TryGetValue(token.Text, out var objMembers)
                  && objMembers.ConstructorOverloads != null)
         {
            // CGS023: validate constructor argument types and arity
            var argTypes = InferArgumentTypes(ctx.parameters());

            if (!IsAnyOverloadValid(objMembers.ConstructorOverloads, argTypes))
            {
               var formatStr = "(" + string.Join(", ", System.Array.ConvertAll(argTypes, t => t ?? "?")) + ")";
               _diagnostics.Add(new Diagnostic(
                  DiagnosticSeverity.Error,
                  $"No constructor for '{token.Text}' matches {formatStr}",
                  token.Line,
                  token.Column,
                  token.Text.Length,
                  "CGS023"));
            }
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
         // For typed locals (ClassNameTypeContext declarations), also seed _varTypes so
         // that property/method access on them is validated rather than silently skipped.
         var addedTypes      = new HashSet<string>(StringComparer.Ordinal);
         var overriddenTypes = new Dictionary<string, string>(StringComparer.Ordinal);
         var blockCtx = ctx.block();
         if (blockCtx != null)
         {
            var localCollector = new ScopeCollector();
            localCollector.Visit(blockCtx);
            foreach (var name in localCollector.Vars)
            {
               newScope.Add(name);
               if (localCollector.VarTypes.TryGetValue(name, out var localTypeName))
               {
                  if (_varTypes.TryGetValue(name, out var prev))
                     overriddenTypes[name] = prev;
                  else
                     addedTypes.Add(name);
                  _varTypes[name] = localTypeName;
               }
            }
         }

         _functionParams = newScope;
         _functionDepth++;
         _extraLocals = new HashSet<string>(StringComparer.Ordinal);

         // Visit params (triggers Rule 2 for classNameType) and body (Rules 4/5)
         VisitChildren(ctx);

         _functionParams = savedParams;
         _functionDepth  = savedDepth;
         _extraLocals    = savedLocals;

         // Restore _varTypes: undo any overrides and removals made for this scope
         foreach (var kvp in overriddenTypes)
            _varTypes[kvp.Key] = kvp.Value;
         foreach (var name in addedTypes)
            _varTypes.Remove(name);

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
                && _globalVarLines.TryGetValue(name, out var decl)
                && token.Line < decl.Line
                && !ShouldSkipIdentifierCheck(ctx))
            {
               _diagnostics.Add(new Diagnostic(
                  DiagnosticSeverity.Warning,
                  $"Variable '{name}' used before its declaration on line {decl.Line}",
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
   /// CGS022 — validates argument types and arity for known old-style functions.
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
               else if (_functionDefinitions != null
                        && _functionDefinitions.TryGetValue(token.Text, out var funcInfo))
               {
                  // CGS022: validate argument types and arity
                  var argTypes = InferArgumentTypes(ctx.parameters());

                  if (!IsCallValid(funcInfo, argTypes))
                  {
                     var formatStr = "(" + string.Join(", ", Array.ConvertAll(argTypes, t => t ?? "?")) + ")";
                     _diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        $"No overload of '{token.Text}' matches {formatStr}",
                        token.Line,
                        token.Column,
                        token.Text.Length,
                        "CGS022"));
                  }
               }
            }
         }
      }

      // Pattern: postfixExpr LBRACKET expression RBRACKET  (indexer access)
      if (ctx.LBRACKET() != null && _objectDefinitions != null)
      {
         var rawType      = TryResolveBaseType(ctx.postfixExpr());
         var baseType     = rawType != null ? (MapToCanonical(rawType) ?? rawType) : null;
         if (baseType != null
             && _objectDefinitions.TryGetValue(baseType, out var indexedMembers)
             && indexedMembers.MethodOverloads != null
             && indexedMembers.MethodOverloads.TryGetValue("[]", out var indexerOverloads))
         {
            var lbToken  = ctx.LBRACKET().Symbol;
            var keyType  = TryInferType(ctx.expression());

            if (IsLhsOfAssignment(ctx))
            {
               // CGS025: validate setter — check [key, value] types against 2-param "[]" overloads
               var setterOverloads = indexerOverloads
                  .Where(o => o.Count == 2)
                  .ToList<IReadOnlyList<string>>();
               if (setterOverloads.Count > 0)
               {
                  var valueExpr  = TryGetAssignmentRhsExpression(ctx);
                  var valueType  = TryInferType(valueExpr);
                  var argTypes   = new[] { keyType, valueType };
                  if (!IsAnyVariantValid(setterOverloads, argTypes))
                  {
                     var formatStr = "[" + string.Join(", ", System.Array.ConvertAll(argTypes, t => t ?? "?")) + "]";
                     _diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        $"No indexer setter on '{baseType}' matches {formatStr}",
                        lbToken.Line,
                        lbToken.Column,
                        1,
                        "CGS025"));
                  }
               }
            }
            else
            {
               // CGS025: validate getter — check key type against 1-param "[]" overloads
               var getterOverloads = indexerOverloads
                  .Where(o => o.Count == 1)
                  .ToList<IReadOnlyList<string>>();
               if (getterOverloads.Count > 0)
               {
                  var argTypes = new[] { keyType };
                  if (!IsAnyVariantValid(getterOverloads, argTypes))
                  {
                     var formatStr = "[" + (keyType ?? "?") + "]";
                     _diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        $"No indexer on '{baseType}' matches {formatStr}",
                        lbToken.Line,
                        lbToken.Column,
                        1,
                        "CGS025"));
                  }
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
                  else if (members.MethodOverloads != null
                           && members.MethodOverloads.TryGetValue(memberName, out var methodOverloads))
                  {
                     // CGS024: validate method argument types and arity
                     var argTypes = InferArgumentTypes(ctx.parameters());

                     if (!IsAnyOverloadValid(methodOverloads, argTypes))
                     {
                        var formatStr = "(" + string.Join(", ", System.Array.ConvertAll(argTypes, t => t ?? "?")) + ")";
                        _diagnostics.Add(new Diagnostic(
                           DiagnosticSeverity.Error,
                           $"No overload of '{baseType}.{memberName}' matches {formatStr}",
                           memberIdNode.Symbol.Line,
                           memberIdNode.Symbol.Column,
                           memberName.Length,
                           "CGS024"));
                     }
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
      || _knownConstants.Contains(name)
      || _knownGlobalVariables.Contains(name);

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

         // Typed locals in function bodies have their type recorded in _varTypes and
         // must be resolved before checking _functionParams (which would suppress them).
         if (_varTypes.TryGetValue(varName, out var typeName))
            return typeName;

         // Untyped function parameter or catch variable — type is unknown in this scope.
         if (_functionParams != null && _functionParams.Contains(varName)) return null;
         if (_extraLocals.Contains(varName)) return null;

         return null;
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
         // If the parent is another postfix expression, ctx is either the base of a
         // further member-access chain (e.g. a.B in `a.B.C = v`) or inside an index
         // expression (e.g. u.ResourceId in `d[u.ResourceId] = v`).  In both cases
         // ctx is being *read*, not assigned, so it is not the LHS.
         if (parent is CgScriptParser.PostfixExprContext) return false;
         // Stop searching beyond a statement boundary
         if (parent is CgScriptParser.StatementContext) return false;
         node = parent;
      }
      return false;
   }

   /// <summary>
   /// When <paramref name="ctx"/> is the LHS of an assignment, returns the RHS
   /// expression (<c>expression(1)</c> in the enclosing <c>exprOrAssign</c>).
   /// Returns <c>null</c> when no enclosing assignment is found.
   /// </summary>
   private static CgScriptParser.ExpressionContext? TryGetAssignmentRhsExpression(
      CgScriptParser.PostfixExprContext ctx)
   {
      Antlr4.Runtime.Tree.IParseTree? node = ctx;
      while (node?.Parent != null)
      {
         var parent = node.Parent;
         if (parent is CgScriptParser.ExprOrAssignContext assignCtx)
         {
            if (assignCtx.ASSIGN() != null && node == assignCtx.expression(0))
               return assignCtx.expression(1);
            return null;
         }
         if (parent is CgScriptParser.PostfixExprContext) return null;
         if (parent is CgScriptParser.StatementContext) return null;
         node = parent;
      }
      return null;
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

   // ── Type inference helpers ────────────────────────────────────────────────────

   /// <summary>
   /// Attempts to infer the CgScript type of an expression node.
   /// Returns the canonical type name (e.g. "Number", "String", "Boolean", "Array",
   /// or a class name like "DateTime") or <c>null</c> when the type cannot be
   /// determined statically.
   /// </summary>
   private string? TryInferType(Antlr4.Runtime.Tree.IParseTree? tree)
   {
      switch (tree)
      {
         case CgScriptParser.ExpressionContext expr:
            // Ternary: subExpression QMARK subExpression COLON subExpression
            if (expr.QMARK() != null)
            {
               var thenT = TryInferType(expr.subExpression(1));
               var elseT = TryInferType(expr.subExpression(2));
               // Return the "then" type only when both branches are compatible
               return (thenT != null && elseT != null && IsTypeCompatible(thenT, elseT)) ? thenT : null;
            }
            return TryInferType(expr.subExpression(0));

         case CgScriptParser.SubExpressionContext sub:
            return TryInferType(sub.orExpression());

         case CgScriptParser.OrExpressionContext or:
            return or.OR().Length > 0 ? "Boolean" : TryInferType(or.andExpression(0));

         case CgScriptParser.AndExpressionContext and:
            return and.AND().Length > 0 ? "Boolean" : TryInferType(and.relExpression(0));

         case CgScriptParser.RelExpressionContext rel:
            // Comparison operators produce a Boolean result
            if (rel.addExpression().Length > 1) return "Boolean";
            return TryInferType(rel.addExpression(0));

         case CgScriptParser.AddExpressionContext add:
            // Arithmetic/string-concat: infer from the first operand
            return TryInferType(add.multExpression(0));

         case CgScriptParser.MultExpressionContext mult:
            if (mult.STAR().Length > 0 || mult.DIV().Length > 0 || mult.MOD().Length > 0)
               return "Number";
            return TryInferType(mult.powExpression(0));

         case CgScriptParser.PowExpressionContext pow:
            if (pow.negExpression().Length > 1) return "Number";
            return TryInferType(pow.negExpression(0));

         case CgScriptParser.NegExpressionContext neg:
            if (neg.NOT() != null) return "Boolean";
            return TryInferType((Antlr4.Runtime.Tree.IParseTree?)neg.unaryExpr() ?? neg.negExpression());

         case CgScriptParser.UnaryExprContext unary:
            if (unary.PLUS() != null || unary.MINUS() != null) return "Number";
            return TryInferType(unary.postfixExpr());

         case CgScriptParser.PostfixExprContext postfix:
            return TryInferPostfixType(postfix);

         case CgScriptParser.PrimaryExprContext primary:
            return TryInferPrimaryType(primary);

         case CgScriptParser.ConstantValueContext cv:
            return TryInferConstantType(cv);

         default:
            return null;
      }
   }

   private string? TryInferPostfixType(CgScriptParser.PostfixExprContext ctx)
   {
      // Bare primaryExpr (no postfix operator)
      if (ctx.LBRACKET() == null && ctx.DOT() == null && ctx.LPAREN() == null)
         return TryInferPrimaryType(ctx.primaryExpr());

      // Array indexing: element type unknown
      if (ctx.LBRACKET() != null)
         return null;

      // Direct function call: postfixExpr LPAREN parameters RPAREN (no DOT)
      if (ctx.LPAREN() != null && ctx.DOT() == null)
      {
         var inner   = ctx.postfixExpr();
         var primary = inner?.primaryExpr();
         if (primary != null && primary.IDENTIFIER() != null
             && primary.NEW() == null && primary.FUNCTION() == null
             && inner!.LPAREN() == null && inner.DOT() == null && inner.LBRACKET() == null)
         {
            var funcName = primary.IDENTIFIER().Symbol.Text;
            if (_functionDefinitions != null && _functionDefinitions.TryGetValue(funcName, out var info))
            {
               var rt = info.ReturnType;
               return string.IsNullOrEmpty(rt) || rt == "Empty" ? null : rt;
            }
         }
         return null;
      }

      // Member property access: postfixExpr DOT IDENTIFIER (no LPAREN)
      if (ctx.DOT() != null && ctx.LPAREN() == null && _objectDefinitions != null)
      {
         var propName = ctx.IDENTIFIER()?.Symbol.Text;
         if (propName != null)
         {
            var baseType = TryResolveBaseType(ctx.postfixExpr());
            if (baseType != null
                && _objectDefinitions.TryGetValue(baseType, out var members)
                && members.PropertyReturnTypes.TryGetValue(propName, out var retType)
                && !string.IsNullOrEmpty(retType))
               return MapToCanonical(retType);
         }
      }

      return null;
   }

   private string? TryInferPrimaryType(CgScriptParser.PrimaryExprContext? ctx)
   {
      if (ctx == null) return null;

      // new ClassName(...) → the class name
      if (ctx.NEW() != null)
         return ctx.IDENTIFIER()?.Symbol.Text;

      // Function literal → "Function"
      if (ctx.FUNCTION() != null)
         return "Function";

      // Bare identifier → look up declared type
      if (ctx.IDENTIFIER() != null && ctx.NEW() == null)
      {
         var name = ctx.IDENTIFIER().Symbol.Text;
         if (_varTypes.TryGetValue(name, out var varType))
            return varType;
         return null;
      }

      // Constant literal
      if (ctx.constantValue() != null)
         return TryInferConstantType(ctx.constantValue());

      // Parenthesised expression
      if (ctx.expression() != null)
         return TryInferType(ctx.expression());

      return null;
   }

   private static string? TryInferConstantType(CgScriptParser.ConstantValueContext? cv)
   {
      if (cv == null) return null;
      if (cv.STRING_LITERAL() != null || cv.CHAR_LITERAL() != null) return "String";
      if (cv.NUM_INT() != null || cv.NUM_DOUBLE() != null)           return "Number";
      if (cv.TRUE() != null || cv.FALSE() != null)                   return "Boolean";
      // Array/dict literal, date literal, interval → Array
      if (cv.LBRACKET() != null || cv.LCURLY() != null || cv.DATE_LITERAL() != null)
         return "Array";
      return null;
   }

   // ── Type compatibility helpers ────────────────────────────────────────────────

   /// <summary>
   /// Returns the display name used in error messages for a typeSpec keyword
   /// (e.g. "number", "string", "bool", or a class name like "Dictionary").
   /// Returns <c>null</c> for types that accept any value (<c>object</c>, <c>?</c>).
   /// </summary>
   private static string? GetDeclaredTypeName(CgScriptParser.TypeSpecContext typeSpec)
      => typeSpec switch
      {
         CgScriptParser.NumberTypeContext   => "number",
         CgScriptParser.StringTypeContext   => "string",
         CgScriptParser.BoolTypeContext     => "bool",
         CgScriptParser.ArrayTypeContext    => "array",
         CgScriptParser.FunctionTypeContext => "function",
         CgScriptParser.ClassNameTypeContext cls => cls.IDENTIFIER()?.Symbol.Text,
         _ => null, // object / ? → skip type checking
      };

   /// <summary>
   /// Maps a type name to its canonical form used for compatibility comparison
   /// (e.g. "number" → "Number", "string" → "String").
   /// Also normalises C# primitive aliases that bleed through from API documentation
   /// (e.g. "int" → "Number") and strips the nullable suffix (e.g. "int?" → "Number").
   /// Returns <c>null</c> for "object" / "Object" (the any-type) so that callers can
   /// skip type checking rather than generate false-positive errors.
   /// Class names pass through unchanged.
   /// </summary>
   private static string? MapToCanonical(string declaredName)
   {
      // Strip nullable suffix and re-map the base type.
      // Substring is used instead of the [..^1] range operator because this
      // assembly targets netstandard2.0 which does not support System.Range.
      if (declaredName.EndsWith("?") && declaredName.Length > 1)
         return MapToCanonical(declaredName.Substring(0, declaredName.Length - 1));

      return declaredName switch
      {
         // CgScript keyword types (declared by users in scripts)
         "number"                                                             => "Number",
         "string"                                                             => "String",
         "bool"                                                               => "Boolean",
         "array"                                                              => "Array",
         "function"                                                           => "Function",
         // C# numeric aliases that bleed through from API documentation
         "int" or "long" or "short" or "byte"
            or "double" or "float" or "decimal"                              => "Number",
         // "boolean" also appears in some API docs (lowercase variant of bool)
         "boolean"                                                            => "Boolean",
         // "string-guid" is used in some API docs for GUID-valued strings
         "string-guid"                                                        => "String",
         // "object" / "Object" means any type — suppress type checking
         "object" or "Object"                                                 => null,
         _ => declaredName, // class name (e.g. "DateTime") unchanged
      };
   }

   /// <summary>
   /// Returns <c>true</c> when <paramref name="a"/> and <paramref name="b"/> are
   /// assignment-compatible or ternary-branch-compatible types.
   /// <list type="bullet">
   ///   <item>Same type → compatible.</item>
   ///   <item>"Array" and any class name (non-primitive) → compatible, because
   ///         CgScript objects are represented as arrays internally.</item>
   ///   <item>Two different class names → conservatively treated as compatible.</item>
   /// </list>
   /// </summary>
   private static bool IsTypeCompatible(string a, string b)
   {
      if (a == b) return true;
      // Array ↔ class name: compatible (objects are arrays)
      if (a == "Array" && !IsPrimitive(b)) return true;
      if (!IsPrimitive(a) && b == "Array") return true;
      // Two different class names: be conservative
      if (!IsPrimitive(a) && !IsPrimitive(b)) return true;
      return false;
   }

   private static bool IsPrimitive(string type)
      => type is "Number" or "String" or "Boolean" or "Function";

   /// <summary>
   /// Returns <c>true</c> when the supplied argument types are valid for the given
   /// function definition (correct arity and compatible parameter types).
   /// For new-style functions with variants, checks whether any variant accepts the call.
   /// </summary>
   private static bool IsCallValid(FunctionInfo funcInfo, string?[] argTypes)
   {
      // New-style functions: check if any variant (overload) accepts the arguments.
      // Uses exact arg-count matching, mirroring the interpreter's FindRightOverLoad.
      if (funcInfo.Variants != null)
         return IsAnyVariantValid(funcInfo.Variants, argTypes);

      if (argTypes.Length < funcInfo.NumberOfRequiredArguments)
         return false;
      // No parameter definitions available — can only enforce minimum arity (above).
      // Treating as valid avoids false-positive CGS022 on functions whose signatures
      // are unknown (e.g. old-style built-ins with empty parameter lists).
      if (funcInfo.Parameters.Count == 0)
         return true;
      if (argTypes.Length > funcInfo.Parameters.Count)
         return false;

      for (var i = 0; i < argTypes.Length; i++)
      {
         if (i >= funcInfo.Parameters.Count) break;
         if (!IsArgCompatible(argTypes[i], funcInfo.Parameters[i]))
            return false;
      }
      return true;
   }

   private static bool IsArgCompatible(string? argType, FunctionParamInfo param)
   {
      if (argType == null) return true; // can't infer → don't report false positive
      return param.ConstantType switch
      {
         "Number"  => argType == "Number",
         "String"  => argType == "String",
         "Boolean" => argType == "Boolean",
         "Function"=> argType == "Function",
         "Array"   => IsArrayArgCompatible(argType, param.ObjectType),
         _         => true, // unknown param type → allow
      };
   }

   private static bool IsArrayArgCompatible(string argType, string objectType)
   {
      // Generic array or any class name is acceptable for an Array parameter
      if (argType == "Array") return true;
      if (IsPrimitive(argType)) return false;
      // Specific class name: check case-insensitively against the expected object sub-type
      if (objectType == "NONE") return true;
      return string.Equals(argType, objectType, StringComparison.OrdinalIgnoreCase);
   }

   /// <summary>
   /// Returns <c>true</c> when at least one overload accepts the given argument types.
   /// All parameters are treated as optional: a call with fewer args than an overload's
   /// parameter count is valid as long as each supplied arg type matches.
   /// An overload whose last parameter type is <c>"Params object"</c> is variadic and
   /// accepts any number of arguments beyond the preceding fixed parameters.
   /// </summary>
   private static bool IsAnyOverloadValid(
      IReadOnlyList<IReadOnlyList<string>> overloads,
      string?[] argTypes)
   {
      foreach (var overload in overloads)
      {
         // A "Params object" last parameter marks a variadic overload: it accepts any
         // number of additional arguments of any type (e.g. Function.Call, WorkflowScript.Call).
         if (overload.Count > 0 && overload[overload.Count - 1] == "Params object")
         {
            var fixedCount = overload.Count - 1;
            if (argTypes.Length < fixedCount) continue; // not enough args for fixed params
            // Check only the fixed parameters; variadic args can be anything
            bool fixedOk = true;
            for (var i = 0; i < fixedCount; i++)
            {
               if (!IsMethodArgCompatible(argTypes[i], overload[i]))
               {
                  fixedOk = false;
                  break;
               }
            }
            if (fixedOk) return true;
            continue;
         }

         if (argTypes.Length > overload.Count) continue; // too many args for this overload
         if (AllArgsCompatible(overload, argTypes)) return true;
      }
      return false;
   }

   /// <summary>
   /// Returns <c>true</c> when at least one variant of a new-style function accepts the
   /// given argument types.  Mirrors the interpreter's <c>FindRightOverLoad</c>: a variant
   /// is only a candidate when the argument count exactly equals its parameter count.
   /// </summary>
   private static bool IsAnyVariantValid(
      IReadOnlyList<IReadOnlyList<string>> variants,
      string?[] argTypes)
   {
      foreach (var variant in variants)
      {
         if (argTypes.Length != variant.Count) continue; // exact count required
         if (AllArgsCompatible(variant, argTypes)) return true;
      }
      return false;
   }

   /// <summary>
   /// Returns <c>true</c> when every supplied argument type is compatible with the
   /// corresponding parameter type in <paramref name="paramTypes"/>.
   /// </summary>
   private static bool AllArgsCompatible(IReadOnlyList<string> paramTypes, string?[] argTypes)
   {
      for (var i = 0; i < argTypes.Length; i++)
         if (!IsMethodArgCompatible(argTypes[i], paramTypes[i]))
            return false;
      return true;
   }

   private static bool IsMethodArgCompatible(string? argType, string paramType)
   {
      if (argType == null) return true; // unknown arg type → no false positive
      var canonical = MapToCanonical(paramType);
      if (canonical == null) return true; // "object" param type → any arg is accepted
      return IsTypeCompatible(argType, canonical);
   }

   /// <summary>
   /// Infers the argument types for a call expression by evaluating each argument expression.
   /// </summary>
   private string?[] InferArgumentTypes(CgScriptParser.ParametersContext? parameters)
   {
      var argExprs = parameters?.expression()
                     ?? System.Array.Empty<CgScriptParser.ExpressionContext>();
      var argTypes = new string?[argExprs.Length];
      for (var i = 0; i < argExprs.Length; i++)
         argTypes[i] = TryInferType(argExprs[i]);
      return argTypes;
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
