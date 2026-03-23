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

## Release 2.6.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CGS014 | CgScript | Info     | CgScript parameter has dynamic object type

## Release 2.16.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CGS015 | CgScript | Info     | C-style for loop
CGS016 | CgScript | Error    | Unknown property name
CGS017 | CgScript | Error    | Unknown method name
CGS018 | CgScript | Error    | Assignment to read-only property
CGS019 | CgScript | Error    | Syntax error

## Release 2.17.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CGS020 | CgScript | Error    | Invalid data type in assignment
CGS021 | CgScript | Error    | Ternary branches must return the same type
CGS022 | CgScript | Error    | No matching function overload

## Release 2.18.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CGS023 | CgScript | Error    | No matching constructor overload
CGS024 | CgScript | Error    | No matching method overload

## Release 2.20.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CGS025 | CgScript | Error    | No matching indexer overload
