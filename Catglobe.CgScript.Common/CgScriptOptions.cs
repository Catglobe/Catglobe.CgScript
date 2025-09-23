namespace Catglobe.CgScript.Common;

/// <summary>
/// Options for the CgScript execution
/// </summary>
public class CgScriptOptions
{
   /// <summary>
   /// Which root folder are we running from
   /// </summary>
   public uint FolderResourceId { get; set; }

   /// <summary>
   /// For development, map these impersonations to these users instead. Use 0 to map to developer account
   /// </summary>
   public Dictionary<uint, uint>? ImpersonationMapping { get; set; }
}
