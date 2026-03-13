namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>
/// Minimal function signature information for old-style CgScript built-in functions,
/// used by <see cref="SemanticAnalyzer"/> to validate call argument types.
/// </summary>
public sealed class FunctionInfo
{
   /// <summary>
   /// The function's return type (e.g. "Number", "String", "Array").
   /// <c>null</c> or "Empty" when the function does not return a value.
   /// </summary>
   public string? ReturnType { get; }

   /// <summary>Minimum number of arguments required at the call site.</summary>
   public int NumberOfRequiredArguments { get; }

   /// <summary>Declared parameter types, in order.</summary>
   public IReadOnlyList<FunctionParamInfo> Parameters { get; }

   /// <param name="returnType">Function return type name.</param>
   /// <param name="numberOfRequiredArguments">Minimum required argument count.</param>
   /// <param name="parameters">Ordered list of parameter type info.</param>
   public FunctionInfo(
      string?                          returnType,
      int                              numberOfRequiredArguments,
      IReadOnlyList<FunctionParamInfo> parameters)
   {
      ReturnType                = returnType;
      NumberOfRequiredArguments = numberOfRequiredArguments;
      Parameters                = parameters;
   }
}

/// <summary>Type information for a single function parameter.</summary>
public sealed class FunctionParamInfo
{
   /// <summary>
   /// The constant/primitive type (e.g. "Number", "String", "Boolean", "Array", "Function").
   /// </summary>
   public string ConstantType { get; }

   /// <summary>
   /// When <see cref="ConstantType"/> is "Array", the expected CgScript object sub-type
   /// (e.g. "DATETIME").  "NONE" means any array is accepted.
   /// </summary>
   public string ObjectType { get; }

   /// <param name="constantType">Primitive/constant type of the parameter.</param>
   /// <param name="objectType">Object sub-type when ConstantType is "Array".</param>
   public FunctionParamInfo(string constantType, string objectType)
   {
      ConstantType = constantType;
      ObjectType   = objectType;
   }
}
