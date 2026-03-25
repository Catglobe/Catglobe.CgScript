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

   /// <summary>
   /// Constructor overloads, where each overload is a list of canonical parameter type names.
   /// <c>null</c> when no constructor information is available.
   /// </summary>
   public IReadOnlyList<IReadOnlyList<string>>? ConstructorOverloads { get; }

   /// <summary>
   /// Method overloads keyed by method name.  Each value is a list of overloads,
   /// where every overload is a list of canonical parameter type names.
   /// <c>null</c> when no method-parameter information is available.
   /// </summary>
   public IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<string>>>? MethodOverloads { get; }

   /// <summary>
   /// Obsolete property names mapped to their optional deprecation message (<c>null</c> = no message).
   /// </summary>
   public IReadOnlyDictionary<string, string?> ObsoletePropertyNames { get; }

   /// <summary>
   /// Obsolete method names mapped to their optional deprecation message (<c>null</c> = no message).
   /// </summary>
   public IReadOnlyDictionary<string, string?> ObsoleteMethodNames { get; }

   /// <param name="properties">Property names mapped to whether they have a setter.</param>
   /// <param name="methodNames">Method names (overloads share the same name).</param>
   /// <param name="propertyReturnTypes">Optional property return type map.</param>
   /// <param name="constructorOverloads">Optional list of constructor overloads (each overload is a list of param types).</param>
   /// <param name="methodOverloads">Optional method name → overloads map (each overload is a list of param types).</param>
   /// <param name="obsoletePropertyNames">Optional map of obsolete property names to their deprecation message (null value = no message).</param>
   /// <param name="obsoleteMethodNames">Optional map of obsolete method names to their deprecation message (null value = no message).</param>
   public ObjectMemberInfo(
      IReadOnlyDictionary<string, bool>   properties,
      IEnumerable<string>                 methodNames,
      IReadOnlyDictionary<string, string>? propertyReturnTypes = null,
      IReadOnlyList<IReadOnlyList<string>>? constructorOverloads = null,
      IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<string>>>? methodOverloads = null,
      IReadOnlyDictionary<string, string?>? obsoletePropertyNames = null,
      IReadOnlyDictionary<string, string?>? obsoleteMethodNames   = null)
   {
      Properties            = properties;
      PropertyReturnTypes   = propertyReturnTypes ?? new Dictionary<string, string>();
      _methodNames          = new HashSet<string>(methodNames, StringComparer.Ordinal);
      ConstructorOverloads  = constructorOverloads;
      MethodOverloads       = methodOverloads;
      ObsoletePropertyNames = obsoletePropertyNames
         ?? (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>();
      ObsoleteMethodNames   = obsoleteMethodNames
         ?? (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>();
   }

   /// <summary>Returns <c>true</c> when the type exposes a method with the given name.</summary>
   public bool HasMethod(string name) => _methodNames.Contains(name);
}
