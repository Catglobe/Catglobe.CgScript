namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>
/// A foldable code region in a QSL source document.
/// <see cref="StartLine"/> and <see cref="EndLine"/> are both 0-based (LSP convention).
/// </summary>
public sealed record QslFoldingRange(int StartLine, int EndLine, string Kind);

