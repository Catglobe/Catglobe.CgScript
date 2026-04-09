// QSL (Questionnaire Script Language) Lexer Grammar (ANTLR4)
// Derived from Questionnaire.g4 (ANTLR3) - editor/tooling variant without semantic predicates or C# actions.
lexer grammar QslLexer;

// ── Structure keywords ─────────────────────────────────────────────────────
QUESTIONNAIRE : 'QUESTIONNAIRE';
QUESTION      : 'QUESTION';
GROUP         : 'GROUP';
END           : 'END';
SQ            : 'SQ';
GOTO          : 'GOTO';
IF            : 'IF';
REPLACE       : 'REPLACE';
WITH          : 'WITH';
ORCLEARCURRENT: 'ORCLEARCURRENT';
ANDCLEAR      : 'ANDCLEAR';
FIRST         : 'FIRST';
LAST          : 'LAST';
AFTER         : 'AFTER';
BEFORE        : 'BEFORE';
AS            : 'AS';
QUESTIONS     : 'QUESTIONS';
HIDE          : 'HIDDEN';
IN            : 'IN';

// ── Question type keywords ─────────────────────────────────────────────────
PAGE          : 'PAGE';
SINGLE        : 'SINGLE';
MULTI         : 'MULTI';
SCALE         : 'SCALE';
NUMBER        : 'NUMBER';
TEXT          : 'TEXT';
OPEN          : 'OPEN';
MULTIMEDIA    : 'MULTIMEDIA';
SINGLEGRID    : 'SINGLEGRID';
MULTIGRID     : 'MULTIGRID';
SCALEGRID     : 'SCALEGRID';
TEXTGRID      : 'TEXTGRID';

// ── Condition / range filter type keywords (underscore variants before Label) ─
INC_AO_FROM   : 'INC_AO_FROM';
EXC_AO_FROM   : 'EXC_AO_FROM';
INC_AO        : 'INC_AO';
EXC_AO        : 'EXC_AO';
INC_SQ        : 'INC_SQ';
EXC_SQ        : 'EXC_SQ';

// ── Group / sequence type keywords ─────────────────────────────────────────
RANDOM        : 'RANDOM';
ROTATE        : 'ROTATE';
SEQUENCE      : 'SEQUENCE';
SHOW          : 'SHOW';

// ── Boolean literals ───────────────────────────────────────────────────────
TRUE          : 'true';
FALSE         : 'false';

// ── Punctuation ────────────────────────────────────────────────────────────
LBRACK : '[';
RBRACK : ']';
COMMA  : ',';
SEMI   : ';';
EQUALS : '=';
COLON  : ':';
DASH   : '-';

// ── Literals ───────────────────────────────────────────────────────────────
// Strings may be multi-line (JAVA_SCRIPT and CG_SCRIPT values span many lines).
StringLiteral : '"' ('\\' . | ~["\\])* '"';
Int           : [0-9]+;
Float         : [0-9]* '.' [0-9]+;
Label         : [a-zA-Z_][a-zA-Z_0-9]*;

// ── Whitespace (skip) ──────────────────────────────────────────────────────
WS            : [ \t\r\n]+ -> skip;

// ── Error token ────────────────────────────────────────────────────────────
ERROR         : . ;
