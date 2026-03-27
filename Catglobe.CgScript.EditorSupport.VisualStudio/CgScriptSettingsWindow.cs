using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace Catglobe.CgScript.EditorSupport.VisualStudio;

/// <summary>Tool window that hosts the CgScript settings panel.</summary>
[VisualStudioContribution]
internal sealed class CgScriptSettingsWindow : ToolWindow
{
   public CgScriptSettingsWindow()
   {
      Title = "%CgScript.Windows.Settings.Title%";
   }

   public override ToolWindowConfiguration ToolWindowConfiguration => new()
   {
      Placement = ToolWindowPlacement.Floating,
   };

   public override Task<IRemoteUserControl> GetContentAsync(CancellationToken ct)
      => Task.FromResult<IRemoteUserControl>(new CgScriptSettingsControl());
}
