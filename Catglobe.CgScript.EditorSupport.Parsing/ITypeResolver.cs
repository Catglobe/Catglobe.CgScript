namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>
/// Resolves whether an identifier token is a valid CgScript class name.
/// Implement this interface to replicate the runtime's
/// <c>CgScriptObjectTypeDescriptorFactory.Instance.IsClassName</c> check.
/// Pass <c>null</c> when constructing <see cref="CgScriptParseService"/> to
/// operate in <em>editor / allow-all</em> mode (useful for IDE tooling where
/// the full type registry may not be available).
/// </summary>
public interface ITypeResolver
{
   /// <summary>Returns <c>true</c> if <paramref name="name"/> is a known CgScript class name.</summary>
   bool IsTypeName(string name);
}
