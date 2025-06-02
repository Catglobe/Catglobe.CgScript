using System.Diagnostics;

namespace Catglobe.CgScript.Common;

/// <summary>
/// Telemetry for CgScript
/// </summary>
public static class CgScriptTelemetry
{
   /// <summary>
   /// Name of source
   /// </summary>
   public const string TelemetrySourceName = "Catglobe.CgScript";
   /// <summary>
   /// ActivitySource for CgScript
   /// </summary>
   public static ActivitySource Source { get; internal set; } = new(TelemetrySourceName);

}
