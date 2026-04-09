// QSL (Questionnaire Script Language) Parser Grammar (ANTLR4)
// Derived from Questionnaire.g4 (ANTLR3) - editor/tooling variant without semantic predicates,
// return types, or embedded C# actions.  Property names/values are accepted liberally
// (no property-name validation) so that editor diagnostics focus on structural errors only.
parser grammar QslParser;
options { tokenVocab = QslLexer; }

root : questionnaire? (question | group)+ EOF;

questionnaire : QUESTIONNAIRE (LBRACK qnaireProperty (SEMI qnaireProperty | SEMI)* RBRACK)?;

qnaireProperty : Label EQUALS propValue;

question
   : QUESTION Label PAGE        location? condition* qproperties? StringLiteral branch*
   | QUESTION Label SINGLE      location? condition* qproperties? StringLiteral answeroption+ branch*
   | QUESTION Label MULTI       location? condition* qproperties? StringLiteral answeroption+ branch*
   | QUESTION Label SCALE       location? condition* qproperties? StringLiteral branch*
   | QUESTION Label NUMBER      location? condition* qproperties? StringLiteral branch*
   | QUESTION Label TEXT        location? condition* qproperties? StringLiteral branch*
   | QUESTION Label OPEN        location? condition* qproperties? StringLiteral answeroption* branch*
   | QUESTION Label MULTIMEDIA  location? condition* qproperties? StringLiteral branch*
   | QUESTION Label SINGLEGRID  location? condition* qproperties? StringLiteral subquestion+ answeroption+ branch*
   | QUESTION Label MULTIGRID   location? condition* qproperties? StringLiteral subquestion+ answeroption+ branch*
   | QUESTION Label SCALEGRID   location? condition* qproperties? StringLiteral subquestion+ branch*
   | QUESTION Label TEXTGRID    location? condition* qproperties? StringLiteral subquestion+ branch*
   ;

location
   : FIRST
   | LAST
   | AFTER  Label
   | BEFORE Label
   ;

condition
   : IF StringLiteral ORCLEARCURRENT?
   | IF StringLiteral INC_AO    ranges
   | IF StringLiteral EXC_AO    ranges
   | IF StringLiteral INC_SQ    ranges
   | IF StringLiteral EXC_SQ    ranges
   | IF StringLiteral INC_AO_FROM Label IN ranges
   | IF StringLiteral EXC_AO_FROM Label IN ranges
   | IF StringLiteral textReplacement+
   ;

ranges : LBRACK range (COMMA range | COMMA)* RBRACK;
range  : Int (DASH Int)?;

textReplacement : REPLACE StringLiteral WITH StringLiteral;

qproperties : LBRACK qproperty (SEMI qproperty | SEMI)* RBRACK;
qproperty   : Label EQUALS propValue;

subquestion : SQ COLON (LBRACK sqproperty (SEMI sqproperty | SEMI)* RBRACK)? StringLiteral;
sqproperty  : Label EQUALS propValue;

answeroption : Int COLON (LBRACK aoproperty (SEMI aoproperty | SEMI)* RBRACK)? StringLiteral;
aoproperty   : Label EQUALS propValue;

propValue : TRUE | FALSE | StringLiteral | Int | Float | ranges;

branch      : GOTO Label (IF StringLiteral)? (ANDCLEAR clearTarget)?;
clearTarget : LBRACK Label (COMMA Label | COMMA)* RBRACK;

group : GROUP Label? grouptype (AS group+ | QUESTIONS possiblyhiddenquestion+) (END | EOF);

possiblyhiddenquestion : HIDE? question;

grouptype
   : RANDOM SHOW Int
   | RANDOM
   | ROTATE SHOW Int
   | ROTATE
   | SEQUENCE
   | GROUP
   ;
