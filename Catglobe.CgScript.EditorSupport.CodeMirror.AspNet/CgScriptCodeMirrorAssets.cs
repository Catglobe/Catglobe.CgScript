namespace Catglobe.CgScript.EditorSupport.CodeMirror.AspNet;

/// <summary>
/// Path constants for the CgScript + QSL CodeMirror 6 static web asset bundle.
/// The bundle is served at <see cref="BundlePath"/> and exports both
/// <c>CodeMirrorForCgScript</c> and <c>CodeMirrorForQsl</c> editor classes,
/// together with <c>manageLspConnection</c> for connecting either editor to an
/// LSP server over a WebSocket transport.
/// </summary>
public static class CgScriptCodeMirrorAssets
{
    /// <summary>
    /// Path of the JS bundle within the static web assets content root.
    /// Add a <c>&lt;script type="module" src="@CgScriptCodeMirrorAssets.BundlePath"&gt;&lt;/script&gt;</c>
    /// tag (or import it from your own JS entry point) to make the editor classes available.
    /// The same bundle serves both CgScript (.cgs) and QSL (.qsl) editors.
    /// </summary>
    public const string BundlePath = "/_content/Catglobe.CgScript.EditorSupport.CodeMirror.AspNet/cgscript-cm6.js";
}
