// CgScript Parser Grammar (ANTLR4)
// Ported from CatGlobe.g3 (ANTLR3) — the authoritative runtime grammar.
//
// Key design notes:
//  - Tree rewriting (-> rules) is replaced by the visitor pattern.
//  - k=2 lookahead is handled automatically by ANTLR4's ALL(*) algorithm.
//  - Block vs array/dict disambiguation: ANTLR4's ALL(*) naturally resolves
//    the { block } vs { ARRAYDECL } ambiguity via the grammar structure.
//    The constantValue rule (LCURLY dictOrArrayBody? RCURLY) defines exactly
//    what an array/dict literal looks like; any { ... } that cannot match
//    dictOrArrayBody is unambiguously a block.  This mirrors the runtime's
//    scan_for_block syntactic predicate, which also relies on the grammar
//    (it speculatively parses an expression then checks LA(1)).
//    The only remaining ambiguity is bare {} (empty braces), which is resolved
//    in favour of block by ANTLR4's first-alternative rule — matching the
//    runtime, which also treats {} as an empty block at statement level
//    (only {} at top-level program scope is an empty ARRAYDECL).
//  - ITypeResolver is set via SetTypeResolver() to validate class-name tokens
//    in declarations; pass null to allow all identifiers as types (editor mode).
//  - All alternatives within a rule either all have '#' labels or none do
//    (ANTLR4 requirement).  Left-recursive chains use direct left recursion.
parser grammar CgScriptParser;

options { tokenVocab = CgScriptLexer; }

@parser::members {
    // ── Type resolver ──────────────────────────────────────────────────────────
    private Catglobe.CgScript.EditorSupport.Parsing.ITypeResolver? _typeResolver;

    /// <summary>Inject a type resolver used to validate class-name identifiers.</summary>
    public void SetTypeResolver(Catglobe.CgScript.EditorSupport.Parsing.ITypeResolver? resolver)
        => _typeResolver = resolver;

    private bool IsTypeName(string name)
        => _typeResolver?.IsTypeName(name) ?? true;   // null = allow-all (editor mode)
}

// ── Top-level program ─────────────────────────────────────────────────────────
// A script that is exactly `{}` is treated as an empty array (matches the
// runtime's special-case at the top level).
program
    : LCURLY RCURLY SEMI? EOF
    | statement* EOF
    ;

// ── Statements ────────────────────────────────────────────────────────────────
statement
    : declaration SEMI                                                                          # declarationStatement
    | block                                                                                         # blockStatement
    | exprOrAssign (SEMI | EOF)                                                                 # exprStatement
    | IF LPAREN expression RPAREN statement (ELSE statement)?                                   # ifStatement
    | WHILE LPAREN expression RPAREN statement                                                  # whileStatement
    | FOR LPAREN forControl RPAREN statement                                                    # forStatement
    | TRY statement CATCH LPAREN IDENTIFIER RPAREN statement                                    # tryStatement
    | SWITCH LPAREN expression RPAREN LCURLY caseExpression+ defaultExpression? RCURLY         # switchStatement
    | BREAK SEMI                                                                                # breakStatement
    | CONTINUE SEMI                                                                             # continueStatement
    | THROW expression? (SEMI | EOF)                                                            # throwStatement
    | RETURN expression? (SEMI | EOF)                                                           # returnStatement
    | SEMI                                                                                      # emptyStatement
    ;

// CgScript has two kinds of for-loop:
//   for (item for collection; limit)     foreach-style
//   for (init; condition; update)        C-style
forControl
    : IDENTIFIER FOR expression SEMI expression                         # forEachControl
    | (declaration | assignment) SEMI expression SEMI exprOrAssign      # forClassicControl
    ;

block : LCURLY statement* RCURLY;

// ── Declarations ─────────────────────────────────────────────────────────────
// Typed variable with an optional initialiser.
// A bare IDENTIFIER that is a known class name is also a valid type.
declaration
    : typeSpec IDENTIFIER declarationInitializer?
    ;

typeSpec
    : BOOL      # boolType
    | NUMBER    # numberType
    | STRING    # stringType
    | ARRAY     # arrayType
    | OBJECT    # objectType
    | QUESTION  # questionType
    | FUNCTION  # functionType
    | IDENTIFIER  {IsTypeName(TokenStream.LT(1).Text)}?   # classNameType
    ;

declarationInitializer : ASSIGN expression;

assignment : expression ASSIGN expression;

caseExpression    : CASE (NUM_INT | STRING_LITERAL | IDENTIFIER) COLON statement+;
defaultExpression : DEFAULT COLON statement+;

// ── Expression or assignment ──────────────────────────────────────────────────
exprOrAssign
    : expression ASSIGN expression
    | expression
    ;

// ── Expressions (precedence encoded in separate rules, top -> lowest) ────────
expression
    : whereAggregation WHERE subExpression
    | subExpression QMARK subExpression COLON subExpression
    | subExpression
    ;

// ── Where-expression aggregation (LHS of a 'where' expression) ───────────────
// Always a direct function call: funcName(arg1, arg2, …)
// The function name must be one of the known where-expression aggregators.
// Arguments are DCS column names or CgScript expressions, depending on the function.
whereAggregation
    : IDENTIFIER LPAREN parameters RPAREN
    ;

subExpression : orExpression;

orExpression  : andExpression  (OR  andExpression)*;
andExpression : relExpression  (AND relExpression)*;
relExpression : addExpression  ((EQUALS | NOT_EQUALS | LTHAN | LTEQ | GTHAN | GTEQ) addExpression)*;
addExpression : multExpression ((PLUS  | MINUS) multExpression)*;
multExpression: powExpression  ((STAR  | DIV | MOD) powExpression)*;
powExpression : negExpression  (POW negExpression)*;

negExpression
    : NOT negExpression
    | unaryExpr
    ;

unaryExpr
    : PLUS  unaryExpr
    | MINUS unaryExpr
    | postfixExpr
    ;

// Left-recursive postfix chain: indexing, member access, and calls.
postfixExpr
    : postfixExpr LBRACKET expression RBRACKET
    | postfixExpr DOT IDENTIFIER (LPAREN parameters RPAREN)?
    | postfixExpr LPAREN parameters RPAREN
    | primaryExpr
    ;

primaryExpr
    : constantValue
    | INC primaryExpr
    | DEC primaryExpr
    | NEW IDENTIFIER LPAREN parameters RPAREN
    | FUNCTION LPAREN functionParameters RPAREN block
    | IDENTIFIER (INC | DEC)?
    | LPAREN expression RPAREN
    ;

// ── Function / lambda helpers ─────────────────────────────────────────────────
functionParameters
    : (declaration (COMMA declaration)*)?
    ;

parameters
    : (expression (COMMA expression)*)?
    ;

// ── Literals ──────────────────────────────────────────────────────────────────
constantValue
    : TRUE
    | FALSE
    | EMPTY
    | NUM_INT
    | NUM_DOUBLE
    | CHAR_LITERAL
    | STRING_LITERAL
    | DATE_LITERAL
    | LBRACKET intervals RBRACKET
    | LCURLY dictOrArrayBody? RCURLY
    ;

// The body can be an array or a dictionary.
//   Array:  expr , expr , ...
//   Dict:   expr : expr , expr : expr , ...
// The visitor must distinguish the two forms by checking whether the first
// separator after the first expression is COLON or COMMA.
dictOrArrayBody
    : expression COLON expression (COMMA expression COLON expression)* COMMA?
    | expression (COMMA expression)* COMMA?
    ;

// ── Intervals (used in [ ] range literals) ────────────────────────────────────
intervals : interval (COMMA interval)*;
interval  : NUM_INT (MINUS NUM_INT)?;

