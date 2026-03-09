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
         id: "CgScriptLanguageSupport.7c3f2e1d-b8a4-4d9e-9f0a-1b2c3d4e5f6a",
         version: this.ExtensionAssemblyVersion,
         publisherName: "Voxmeter A/S",
         displayName: "CgScript Language Support",
         description: "IntelliSense, diagnostics, completions and semantic highlighting for CgScript (.cgs) files.") {
      },
   };

   /// <inheritdoc/>
   protected override void InitializeServices(IServiceCollection serviceCollection)
   {
      base.InitializeServices(serviceCollection);
   }
}
