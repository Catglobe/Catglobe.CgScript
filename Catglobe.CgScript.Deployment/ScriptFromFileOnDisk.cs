using Catglobe.CgScript.Common;

namespace Catglobe.CgScript.Deployment;

/// <summary>
/// Script file from disk.
/// Impersonation is inferred from the file name. If file is named MyFolder\MyScript@123.cgs, impersonation will be 123.
/// If script is named MyScript@123.public.cgs, it will be possible to run the script without a user being logged in.
/// If script is named MyScript@123.pii.cgs, the script declares that it may read personal data, which is only honored
/// together with an impersonated user that is PII-eligible on the site.
/// </summary>
public class ScriptFromFileOnDisk : IScriptDefinition
{
   private readonly string _fullPath;

   /// <summary>
   /// Script file from disk.
   /// </summary>
   /// <param name="fullPath">Full path on disk to load file</param>
   /// <param name="relativePath">Path that is interpreted as scriptName</param>
   /// <exception cref="InvalidScriptFileNameException">The file name does not follow the script naming grammar.</exception>
   public ScriptFromFileOnDisk(string fullPath, string relativePath)
   {
      _fullPath = fullPath;
      var parsed = ScriptFileNameParser.Parse(relativePath);
      ScriptName               = parsed.ScriptName.Replace('\\', '/');
      Impersonation            = parsed.Impersonation;
      AllowExecuteWithoutLogin = parsed.AllowExecuteWithoutLogin;
      CanAccessPII             = parsed.CanAccessPII;
   }

   ///<inheritdoc/>
   public string ScriptName { get; }
   ///<inheritdoc/>
   public uint?  Impersonation { get; }
   ///<inheritdoc/>
   public bool AllowExecuteWithoutLogin { get; }
   ///<inheritdoc/>
   public bool CanAccessPII { get; }
   ///<inheritdoc/>
   public Task<Stream> Content => Task.FromResult<Stream>(File.OpenRead(_fullPath));
}
