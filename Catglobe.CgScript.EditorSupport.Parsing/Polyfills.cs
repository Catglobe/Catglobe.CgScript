// IsExternalInit is required by C# 9+ record types but is only available in .NET 5+.
// This polyfill makes records compile when targeting netstandard2.0.
#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
