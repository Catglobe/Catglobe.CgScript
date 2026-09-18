namespace Catglobe.CgScript.Common;

/// <summary>
/// The content and metadata of a script
/// </summary>
public interface IScriptDefinition
{
   /// <summary>
   /// The name of the script as it is known in CgScript
   /// </summary>
   public string ScriptName { get; }
   /// <summary>
   /// The actual script
   /// </summary>
   public Task<Stream> Content { get; }
   /// <summary>
   /// The resource id of the user that the script run as
   /// </summary>
   public uint? Impersonation { get; }
   /// <summary>
   /// If true, the script can be run without a user being logged in
   /// </summary>
   bool AllowExecuteWithoutLogin { get; }
   /// <summary>
   /// Maps to the workflow PII flag of the site, declaring that the script may read personal data.
   /// It is only honored together with <see cref="Impersonation"/>: a PII script needs an impersonated
   /// user that is PII-eligible on the site. The default is false, so implementations must override
   /// this member when their scripts may read PII.
   /// </summary>
   bool CanAccessPII => false;
}
