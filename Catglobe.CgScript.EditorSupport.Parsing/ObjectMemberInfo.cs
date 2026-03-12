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

   /// <summary>
   /// Property names mapped to their declared return type name (empty string = unknown).
   /// </summary>
   public IReadOnlyDictionary<string, string> PropertyReturnTypes { get; }

   /// <param name="properties">Property names mapped to whether they have a setter.</param>
   /// <param name="methodNames">Method names (overloads share the same name).</param>
   /// <param name="propertyReturnTypes">Optional property return type map.</param>
   public ObjectMemberInfo(
      IReadOnlyDictionary<string, bool>   properties,
      IEnumerable<string>                 methodNames,
      IReadOnlyDictionary<string, string>? propertyReturnTypes = null)
   {
      Properties          = properties;
      PropertyReturnTypes = propertyReturnTypes ?? new Dictionary<string, string>();
      _methodNames        = new HashSet<string>(methodNames, StringComparer.Ordinal);
   }

   /// <summary>Returns <c>true</c> when the type exposes a method with the given name.</summary>
   public bool HasMethod(string name) => _methodNames.Contains(name);
}
