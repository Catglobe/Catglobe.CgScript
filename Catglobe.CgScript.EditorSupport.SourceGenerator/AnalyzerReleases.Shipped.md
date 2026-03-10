; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 2.2.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CGS001 | CgScript | Warning  | Duplicate variable declaration
CGS002 | CgScript | Error    | Unknown type
CGS003 | CgScript | Error    | Unknown type in new expression
CGS004 | CgScript | Warning  | Unknown function
CGS005 | CgScript | Warning  | Undefined variable
CGS006 | CgScript | Warning  | Empty statement has no effect
CGS007 | CgScript | Warning  | Unreachable code
CGS008 | CgScript | Warning  | Variable used before its declaration
CGS009 | CgScript | Warning  | Declared variable is never used

## Release 2.3.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CGS010 | CgScript | Error    | Missing or duplicate [CgScriptSerializer] attribute on a JsonSerializerContext
CGS011 | CgScript | Warning  | Missing [JsonSerializable] on the [CgScriptSerializer]-marked context for a script return type
CGS012 | CgScript | Error    | CgScript parameter requires @param annotation
CGS013 | CgScript | Error    | Invalid type annotation syntax
