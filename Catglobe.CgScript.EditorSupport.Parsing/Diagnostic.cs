namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>A parse error or warning produced by <see cref="CgScriptParseService"/>.</summary>
/// <param name="Severity">How severe the issue is.</param>
/// <param name="Message">Human-readable description.</param>
/// <param name="Line">1-based line number of the offending token (0 if unknown).</param>
/// <param name="Column">0-based column of the offending token (0 if unknown).</param>
/// <param name="Length">Character length of the offending token (0 if unknown).</param>
public sealed record Diagnostic(
   DiagnosticSeverity Severity,
   string             Message,
   int                Line,
   int                Column,
   int                Length = 0);
