using Catglobe.CgScript.Common;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Catglobe.CgScript.Deployment;

/// <summary>
/// Deployer
/// </summary>
public interface IDeployer
{
   /// <summary>
   /// Sync the current scripts
   /// </summary>
   Task Sync(string environmentName, CancellationToken token);
}

internal partial class Deployer(HttpClient httpClient, IScriptProvider provider, IScriptMapping map, IOptions<DeploymentOptions> options) : IDeployer
{
   private readonly int _parentId = options.Value.FolderResourceId;

   public async Task Sync(string environmentName, CancellationToken token)
   {
      using var activity = CgScriptTelemetry.Source.StartActivity();
      var current           = await provider.GetAll();
      var mapAfterFirstSync = await SyncMap(current.Keys.Select(x => new CgScriptReference { ScriptName = x }).ToList(), token);
      var maker             = new CgScriptMaker(environmentName, current, mapAfterFirstSync);

      var cgScriptDefinitions = new List<CgScriptDefinition>();
      foreach (var (scriptName, scriptDefinition) in current)
         cgScriptDefinitions.Add(new() {
            ScriptName = scriptName,
            Content = await maker.GetContent(scriptName),
            Impersonation = scriptDefinition.Impersonation,
            AllowExecuteWithoutLogin = scriptDefinition.AllowExecuteWithoutLogin,
         });
      await UpdateScripts(cgScriptDefinitions, token);
      map.Reset();
   }

   private async Task<CgDeploymentMap> SyncMap(List<CgScriptReference> scripts, CancellationToken token)
   {
      using var activity = CgScriptTelemetry.Source.StartActivity();
      var       req      = await httpClient.PostAsJsonAsync($"SyncMap/{_parentId}", scripts, MapSerializer.Default.ListCgScriptReference, token);
      if (!req.IsSuccessStatusCode)
         activity?.SetStatus(ActivityStatusCode.Error);
      req.EnsureSuccessStatusCode();
      return await req.Content.ReadFromJsonAsync(MapSerializer.Default.CgDeploymentMap, token) ?? new();
   }

   private async Task<CgDeploymentMap> UpdateScripts(List<CgScriptDefinition> scripts, CancellationToken token)
   {
      using var activity = CgScriptTelemetry.Source.StartActivity();
      var       req      = await httpClient.PostAsJsonAsync($"UpdateScripts/{_parentId}", scripts, MapSerializer.Default.ListCgScriptDefinition, token);
      if (!req.IsSuccessStatusCode)
         activity?.SetStatus(ActivityStatusCode.Error);
      req.EnsureSuccessStatusCode();
      return await req.Content.ReadFromJsonAsync(MapSerializer.Default.CgDeploymentMap, token) ?? new();
   }

   [JsonSerializable(typeof(CgDeploymentMap))]
   [JsonSerializable(typeof(List<CgScriptReference>))]
   [JsonSerializable(typeof(List<CgScriptDefinition>))]
   [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
   private partial class MapSerializer : JsonSerializerContext;

   private class CgDeploymentMap : Dictionary<string, CgDeploymentMapItem>, IScriptMapping
   {
      public int GetIdOf(string scriptName) => this[scriptName].ScriptResourceId;

      public ValueTask EnsureDownloaded() => ValueTask.CompletedTask;

      public void Reset() { }
   }

   private record CgDeploymentMapItem
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

   private record CgScriptReference
   {
      /// <summary>
      /// The name of the script as it is known in CgScript
      /// </summary>
      public string ScriptName { get; set; } = null!;
   }

   private record CgScriptDefinition
   {
      /// <summary>
      /// The name of the script as it is known in CgScript
      /// </summary>
      public string ScriptName { get; set; } = null!;
      /// <summary>
      /// The actual script
      /// </summary>
      public string Content { get; set; } = null!;
      /// <summary>
      /// The resource id of the user that the script run as
      /// </summary>
      public uint? Impersonation { get; set; }
      /// <summary>
      /// If true, the script can be run without a user being logged in. For obvious reasons this must be used with impersonation.
      /// </summary>
      public bool AllowExecuteWithoutLogin { get; set; }
   }
}