using Microsoft.VisualStudio.Extensibility.UI;

namespace Catglobe.CgScript.EditorSupport.VisualStudio;

/// <summary>Remote UI control that renders the CgScript settings panel inside Visual Studio.</summary>
internal sealed class CgScriptSettingsControl : RemoteUserControl
{
   internal CgScriptSettingsControl() : base(dataContext: new CgScriptSettingsViewModel()) { }
}
