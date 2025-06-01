using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Catglobe.CgScript.Common;
using Microsoft.Extensions.Options;

namespace Catglobe.CgScript.Runtime;

internal partial class ScriptMapping(HttpClient httpClient, IOptions<CgScriptOptions> options) : IScriptMapping
{
   private DeploymentMap? _map;

   public int GetIdOf(string scriptName) => _map![scriptName].ScriptResourceId;

   public async ValueTask EnsureDownloaded()
   {
      if (_map is not null) return;
      using var activity = CgScriptTelemetry.Source.StartActivity("DownloadMap");
      var req = await httpClient.GetAsync($"GetMap/{options.Value.FolderResourceId}");
      if (!req.IsSuccessStatusCode)
         activity?.SetStatus(ActivityStatusCode.Error);
      req.EnsureSuccessStatusCode();
      _map = await req.Content.ReadFromJsonAsync(MapSerializer.Default.DeploymentMap) ?? new();
   }

   public void Reset() => _map = null;

   [JsonSerializable(typeof(DeploymentMap))]
   [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
   private partial class MapSerializer : JsonSerializerContext;

   private class DeploymentMap : Dictionary<string, DeploymentMapItem>;

   private record DeploymentMapItem
   {
      /// <summary>
      /// If true, the script can be run without a user being logged in
      /// </summary>
      public bool AllowExecuteWithoutLogin { get; set; }
      /// <summary>
      /// The id to use in the api
      /// </summary>
      public int ScriptResourceId { get; set; }
      /// <summary>
      /// The hash of the script content and the impersonation id
      /// </summary>
      public string Sha256 { get; set; } = null!;
   }
}