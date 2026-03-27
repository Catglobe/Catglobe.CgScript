using System.Threading;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.UI;

namespace Catglobe.CgScript.EditorSupport.VisualStudio;

internal sealed class CgScriptSettingsViewModel : NotifyPropertyChangedObject
{
   private string _siteUrl;

   public string SiteUrl
   {
      get => _siteUrl;
      set => SetProperty(ref _siteUrl, value);
   }

   public AsyncCommand SaveCommand { get; }

   public CgScriptSettingsViewModel()
   {
      _siteUrl    = CgScriptSettings.Load().SiteUrl;
      SaveCommand = new AsyncCommand((_, ct) =>
      {
         new CgScriptSettings { SiteUrl = SiteUrl.Trim() }.Save();
         return System.Threading.Tasks.Task.CompletedTask;
      });
   }
}
