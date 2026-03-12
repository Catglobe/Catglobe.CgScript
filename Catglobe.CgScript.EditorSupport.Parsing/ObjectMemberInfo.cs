namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>
/// Cached member information for a single CgScript object type,
/// used by <see cref="SemanticAnalyzer"/> to validate property and method access.
/// </summary>
public sealed class ObjectMemberInfo
{
   private readonly HashSet<string> _methodNames;

   /// <summary>
   /// Property names mapped to whether they have a setter
   /// (<c>true</c> = writable, <c>false</c> = read-only).
   /// </summary>
   public IReadOnlyDictionary<string, bool> Properties { get; }

   /// <param name="properties">Property names mapped to whether they have a setter.</param>
   /// <param name="methodNames">Method names (overloads share the same name).</param>
   public ObjectMemberInfo(
      IReadOnlyDictionary<string, bool> properties,
      IEnumerable<string>               methodNames)
   {
      Properties   = properties;
      _methodNames = new HashSet<string>(methodNames, StringComparer.Ordinal);
   }

   /// <summary>Returns <c>true</c> when the type exposes a method with the given name.</summary>
   public bool HasMethod(string name) => _methodNames.Contains(name);
}
