namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>
/// Function signature information for CgScript built-in functions,
/// used by <see cref="SemanticAnalyzer"/> to validate call argument types.
/// Each entry in <see cref="Variants"/> is one overload;
/// each string is the raw parameter type (e.g. "int", "string", "bool").
/// </summary>
public sealed class FunctionInfo
{
   /// <summary>
   /// Overload variants.  Each entry is one overload;
   /// each string is the raw parameter type (e.g. "int", "string", "bool").
   /// </summary>
   public IReadOnlyList<IReadOnlyList<string>> Variants { get; }

   /// <summary>
   /// <c>true</c> when this function (or all of its variants) is marked as obsolete/deprecated.
   /// </summary>
   public bool IsObsolete { get; }

   /// <summary>
   /// Optional deprecation message.
   /// <c>null</c> when no message was provided.
   /// </summary>
   public string? ObsoleteDoc { get; }

   /// <summary>
   /// Initialises a <see cref="FunctionInfo"/> with the given overload variants.
   /// </summary>
   /// <param name="variants">
   /// The list of overloads, where each overload is an ordered list of raw parameter type strings.
   /// </param>
   /// <param name="isObsolete">Whether the function is obsolete.</param>
   /// <param name="obsoleteDoc">Optional deprecation message.</param>
   public FunctionInfo(IReadOnlyList<IReadOnlyList<string>> variants, bool isObsolete = false, string? obsoleteDoc = null)
   {
      Variants    = variants;
      IsObsolete  = isObsolete;
      ObsoleteDoc = obsoleteDoc;
   }
}
