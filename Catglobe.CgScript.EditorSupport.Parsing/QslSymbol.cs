namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>A question or group definition discovered during QSL semantic analysis.</summary>
public sealed record QslSymbol(
   string Name,
   string Kind,         // "question" | "group"
   string QuestionType, // "PAGE","SINGLE","MULTI",… for questions; "" for groups
   string DisplayText,  // first StringLiteral content, outer quotes stripped and escape sequences resolved
   int    Line,         // 1-based (ANTLR line)
   int    Column,       // 0-based
   int    Length);      // length of the Name token
