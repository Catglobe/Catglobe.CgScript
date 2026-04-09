namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>Every source position where a label name appears (definition or reference).</summary>
public sealed record QslTokenRef(
   string Label,
   bool   IsDefinition,
   int    Line,    // 1-based
   int    Column,  // 0-based
   int    Length);
