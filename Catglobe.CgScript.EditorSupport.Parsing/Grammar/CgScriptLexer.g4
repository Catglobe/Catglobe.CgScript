// CgScript Lexer Grammar (ANTLR4)
// Ported from CatGlobe.g3 (ANTLR3) — the authoritative runtime grammar.
lexer grammar CgScriptLexer;

// ── Keywords (must appear before IDENTIFIER so they match first) ─────────────
BOOL:     'bool';
NUMBER:   'number';
STRING:   'string';
ARRAY:    'array';
QUESTION: 'question';
IF:       'if';
ELSE:     'else';
WHILE:    'while';
FOR:      'for';
BREAK:    'break';
CONTINUE: 'continue';
RETURN:   'return';
TRUE:     'true';
FALSE:    'false';
EMPTY:    'empty';
WHERE:    'where';
OBJECT:   'object';
NEW:      'new';
SWITCH:   'switch';
DEFAULT:  'default';
CASE:     'case';
TRY:      'try';
CATCH:    'catch';
THROW:    'throw';
FUNCTION: 'function';

// ── Operators (longer multi-char tokens before single-char ones) ──────────────
INC:        '++';
DEC:        '--';
EQUALS:     '==';
NOT_EQUALS: '!=';
LTEQ:       '<=';
GTEQ:       '>=';
AND:        '&&';
OR:         '||';

ASSIGN:   '=';
COLON:    ':';
SEMI:     ';';
COMMA:    ',';
LBRACKET: '[';
RBRACKET: ']';
LPAREN:   '(';
RPAREN:   ')';
LCURLY:   '{';
RCURLY:   '}';
PLUS:     '+';
MINUS:    '-';
STAR:     '*';
DIV:      '/';
MOD:      '%';
POW:      '^';
LTHAN:    '<';
GTHAN:    '>';
NOT:      '!';
QMARK:    '?';
DOT:      '.';

// ── Whitespace & Comments ─────────────────────────────────────────────────────
WS:        [ \t\f\r\n]+ -> channel(HIDDEN);
SL_COMMENT: '//' ~[\r\n]*  -> channel(HIDDEN);
ML_COMMENT: '/*' .*? '*/'  -> channel(HIDDEN);

// ── Literals ──────────────────────────────────────────────────────────────────
// Date literals must come before NUM_INT / NUM_DOUBLE to avoid partial matches.
DATE_LITERAL: '#' DIGIT+ '-' DIGIT+ '-' DIGIT+ (' ' DIGIT+ ':' DIGIT+ (':' DIGIT+)?)? '#';

// Floating-point before integer so '1.0' is not split into '1' and '.0'.
NUM_DOUBLE: (DIGIT+ '.' DIGIT* | '.' DIGIT+) EXPONENT?;
NUM_INT:     DIGIT+;

CHAR_LITERAL:   '\'' (~['\\\r\n] | ESC) '\'';
STRING_LITERAL: '"'  (~["\\\r\n] | ESC)* '"';

IDENTIFIER: [a-zA-Z_][a-zA-Z0-9_]*;

// ── Fragments ─────────────────────────────────────────────────────────────────
fragment DIGIT:     [0-9];
fragment HEX_DIGIT: [0-9A-Fa-f];
fragment EXPONENT:  [eE] [+\-]? DIGIT+;
fragment ESC
    : '\\' (
        [nrtbf"'\\/]                                   // simple escapes
      | 'u' HEX_DIGIT HEX_DIGIT HEX_DIGIT HEX_DIGIT   // unicode
      | [0-3] [0-7]? [0-7]?                            // octal 0-377
      | [4-9] [0-7]?                                   // octal 40-97
    )
    ;
