namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>Severity level of a parse diagnostic.</summary>
public enum DiagnosticSeverity
{
   /// <summary>A hard parse error — the input is syntactically invalid.</summary>
   Error,

   /// <summary>A warning that does not prevent parsing from continuing.</summary>
   Warning,
}
