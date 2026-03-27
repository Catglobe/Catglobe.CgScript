using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

#pragma warning disable VSEXTPREVIEW_SETTINGS

namespace Catglobe.CgScript.EditorSupport.VisualStudio;

internal static class CgScriptSettingDefinitions
{
   [VisualStudioContribution]
   internal static SettingCategory Category { get; } = new("cgscript", "%CgScript.Settings.Category.DisplayName%")
   {
      Description            = "%CgScript.Settings.Category.Description%",
      GenerateObserverClass  = true,
   };

   [VisualStudioContribution]
   internal static Setting.String SiteUrlSetting { get; } = new(
      "siteUrl",
      "%CgScript.Settings.SiteUrl.DisplayName%",
      Category,
      defaultValue: "https://voxmeter.catglobe.com")
   {
      Description = "%CgScript.Settings.SiteUrl.Description%",
   };
}
