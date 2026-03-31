using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;

namespace Catglobe.CgScript.EditorSupport.VisualStudio;

/// <summary>Extension entry point — metadata is read here by the VS Extensibility SDK to generate the VSIX manifest.</summary>
[VisualStudioContribution]
public class ExtensionEntrypoint : Extension
{
   /// <inheritdoc/>
   public override ExtensionConfiguration ExtensionConfiguration => new()
   {
      Metadata = new(
         id: "CgScriptLanguageSupport.e841c4eb-8913-4ff2-bb7c-91487ee8fa5c",
         version: this.ExtensionAssemblyVersion,
         publisherName: "Voxmeter",
         displayName: "CgScript Language Support",
         description: "IntelliSense, diagnostics, completions and semantic highlighting for CgScript (.cgs) files.") {
      },
   };

   /// <inheritdoc/>
   protected override void InitializeServices(IServiceCollection serviceCollection)
   {
      base.InitializeServices(serviceCollection);
      serviceCollection.AddSettingsObservers();
   }
}
