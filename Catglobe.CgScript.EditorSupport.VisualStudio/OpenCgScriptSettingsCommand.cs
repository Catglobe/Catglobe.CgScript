using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace Catglobe.CgScript.EditorSupport.VisualStudio;

/// <summary>Opens the CgScript Settings tool window from the Tools menu.</summary>
[VisualStudioContribution]
internal sealed class OpenCgScriptSettingsCommand : Command
{
   public override CommandConfiguration CommandConfiguration =>
      new("%CgScript.Commands.OpenSettings.DisplayName%")
      {
         Placements = [CommandPlacement.KnownPlacements.ToolsMenu],
      };

   public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken ct)
      => await Extensibility.Shell().ShowToolWindowAsync<CgScriptSettingsWindow>(activate: true, ct);
}
