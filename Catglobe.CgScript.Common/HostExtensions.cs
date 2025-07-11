using OpenTelemetry.Trace;

namespace Catglobe.CgScript.Common;

/// <summary>
/// Setup methods
/// </summary>
public static class HostExtensions
{
   /// <summary>
   /// Register the CgScript telemetry source
   /// </summary>
   public static TracerProviderBuilder AddCgScriptInstrumentation(this TracerProviderBuilder builder) => builder.AddSource(CgScriptTelemetry.TelemetrySourceName);

}

