namespace Catglobe.CgScript.Common;

/// <summary>A script identity and the security metadata declared in its file name.</summary>
public sealed class ScriptFileName
{
   internal ScriptFileName(string scriptName, uint? impersonation, bool allowExecuteWithoutLogin, bool canAccessPII)
   {
      ScriptName = scriptName;
      Impersonation = impersonation;
      AllowExecuteWithoutLogin = allowExecuteWithoutLogin;
      CanAccessPII = canAccessPII;
   }

   /// <summary>The relative script name without metadata or extension, with forward-slash separators.</summary>
   public string ScriptName { get; }

   /// <summary>The declared user id; null means absent, while zero explicitly means no impersonation user.</summary>
   public uint? Impersonation { get; }

   /// <summary>Whether the script may execute without login; requires a nonzero impersonation user.</summary>
   public bool AllowExecuteWithoutLogin { get; }

   /// <summary>Whether the script requests PII access; requires a nonzero, site-eligible impersonation user.</summary>
   public bool CanAccessPII { get; }
}
