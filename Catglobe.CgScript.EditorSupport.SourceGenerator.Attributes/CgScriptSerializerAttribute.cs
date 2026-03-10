namespace Catglobe.CgScript
{
   /// <summary>
   /// Marks a <c>JsonSerializerContext</c> subclass
   /// as the AOT-safe JSON context for CgScript generated wrappers.
   /// Exactly one class in the assembly must carry this attribute.
   /// The class must also declare <c>[JsonSerializable(typeof(ReturnType))]</c> for every
   /// non-primitive return type used by scripts in this project.
   /// </summary>
   [global::System.AttributeUsage(global::System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
   public sealed class CgScriptSerializerAttribute : global::System.Attribute { }
}
