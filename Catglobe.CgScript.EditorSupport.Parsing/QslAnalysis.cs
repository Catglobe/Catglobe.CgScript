namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>
/// Result of QSL semantic analysis: symbol table, label-reference list, diagnostics,
/// folding ranges, and ON_PAGE cross-reference map.
/// </summary>
public sealed class QslAnalysis
{
   /// <summary>An empty analysis with no symbols, references, or diagnostics.</summary>
   public static readonly QslAnalysis Empty =
      new(new Dictionary<string, QslSymbol>(), [], [], [], new Dictionary<string, string>());

   /// <summary>All question/group definitions keyed by label name (case-insensitive).</summary>
   public IReadOnlyDictionary<string, QslSymbol>  Symbols      { get; }

   /// <summary>Every source position where a label name appears (definitions and references).</summary>
   public IReadOnlyList<QslTokenRef>              LabelRefs    { get; }

   /// <summary>Semantic diagnostics (property name warnings, undefined-label warnings, etc.).</summary>
   public IReadOnlyList<Diagnostic>               Diagnostics  { get; }

   /// <summary>
   /// Foldable regions identified during semantic analysis (questions, groups, property blocks,
   /// and multi-line string literals).  All line numbers are 0-based.
   /// </summary>
   public IReadOnlyList<QslFoldingRange>          FoldingRanges { get; }

   /// <summary>
   /// Maps a question label that appears in an <c>ON_PAGE</c> value to the label of the PAGE
   /// question whose <c>ON_PAGE</c> property lists it.  Keys are case-insensitive.
   /// </summary>
   public IReadOnlyDictionary<string, string>     OnPageMap    { get; }

   internal QslAnalysis(
      IReadOnlyDictionary<string, QslSymbol>  symbols,
      IReadOnlyList<QslTokenRef>              labelRefs,
      IReadOnlyList<Diagnostic>               diagnostics,
      IReadOnlyList<QslFoldingRange>          foldingRanges,
      IReadOnlyDictionary<string, string>     onPageMap)
   {
      Symbols       = symbols;
      LabelRefs     = labelRefs;
      Diagnostics   = diagnostics;
      FoldingRanges = foldingRanges;
      OnPageMap     = onPageMap;
   }
}
