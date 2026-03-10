; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CGS010 | CgScript | Error    | Missing or duplicate [CgScriptSerializer] attribute on a JsonSerializerContext
CGS011 | CgScript | Error    | Missing [JsonSerializable] on the [CgScriptSerializer]-marked context for a script return type
CGS013 | CgScript | Error    | Invalid type annotation syntax — brackets must appear as '[]' pairs
