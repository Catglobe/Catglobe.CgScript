using System;
using System.IO;
using System.Text.Json;

namespace Catglobe.CgScript.EditorSupport.VisualStudio;

/// <summary>
/// User-level settings persisted to %APPDATA%\Catglobe\CgScript\settings.json.
/// </summary>
internal class CgScriptSettings
{
   private static readonly string SettingsPath = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "Catglobe", "CgScript", "settings.json");

   /// <summary>
   /// Base URL of the Catglobe site (e.g. https://localhost:5001).
   /// Empty means use bundled definitions.
   /// </summary>
   public string SiteUrl { get; set; } = "https://voxmeter.catglobe.com";

   public static CgScriptSettings Load()
   {
      try
      {
         if (File.Exists(SettingsPath))
         {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<CgScriptSettings>(json,
               new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
         }
      }
      catch { /* ignore — return defaults */ }
      return new();
   }

   public void Save()
   {
      var dir = Path.GetDirectoryName(SettingsPath)!;
      Directory.CreateDirectory(dir);
      File.WriteAllText(SettingsPath,
         JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
   }
}
