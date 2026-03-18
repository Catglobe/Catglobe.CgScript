namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>
/// Function signature information for CgScript built-in functions,
/// used by <see cref="SemanticAnalyzer"/> to validate call argument types.
/// <para>
/// Old-style functions carry <see cref="Parameters"/> and
/// <see cref="NumberOfRequiredArguments"/>; <see cref="Variants"/> is <c>null</c>.
/// New-style functions (those with runtime overloads) carry <see cref="Variants"/>
/// (a list of overloads where each overload is an ordered list of raw parameter
/// type strings); <see cref="Parameters"/> is empty.
/// </para>
/// </summary>
public sealed class FunctionInfo
{
   /// <summary>
   /// The function's return type (e.g. "Number", "String", "Array").
   /// <c>null</c> or "Empty" when the function does not return a value.
   /// </summary>
   public string? ReturnType { get; }

   /// <summary>Minimum number of arguments required at the call site (old-style only).</summary>
   public int NumberOfRequiredArguments { get; }

   /// <summary>Declared parameter types, in order (old-style only; empty for new-style).</summary>
   public IReadOnlyList<FunctionParamInfo> Parameters { get; }

   /// <summary>
   /// Overload variants for new-style functions.  Each entry is one overload;
   /// each string is the raw parameter type (e.g. "int", "string", "bool").
   /// <c>null</c> for old-style functions.
   /// </summary>
   public IReadOnlyList<IReadOnlyList<string>>? Variants { get; }

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
      Variants                  = null;
   }

   /// <summary>Creates a <see cref="FunctionInfo"/> for a new-style function with overload variants.</summary>
   /// <param name="variants">
   /// The list of overloads, where each overload is an ordered list of raw parameter type strings.
   /// </param>
   public FunctionInfo(IReadOnlyList<IReadOnlyList<string>> variants)
   {
      ReturnType                = null;
      NumberOfRequiredArguments = 0;
      Parameters                = System.Array.Empty<FunctionParamInfo>();
      Variants                  = variants;
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
