using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Catglobe.CgScript.Common;
using Microsoft.Extensions.Logging;

namespace Catglobe.CgScript.Runtime;

internal class CgScriptApiClient(HttpClient httpClient, IScriptMapping map, ILogger<ICgScriptApiClient> logger) : ApiClientBase(httpClient, logger)
{
   protected override async ValueTask<string> GetPath(string scriptName, string? additionalParameters = null)
   {
      await map.EnsureDownloaded();
      return $"api/CgScript/run/{map.GetIdOf(scriptName)}{additionalParameters ?? ""}";
   }

   protected override Task<HttpContent?> GetJsonContent<TP>(string scriptName, TP? parameter, JsonTypeInfo<TP> callJsonTypeInfo) where TP : default
   {
      if (parameter is null) return Task.FromResult<HttpContent?>(null);
      // Use SerializeToUtf8Bytes which invokes the TypeInfo's Converter directly,
      // bypassing the Options TypeInfoResolver — avoids the "no metadata for Params type"
      // failure that happens with JsonContent.Create on Linux trimmed deployments.
      var bytes   = JsonSerializer.SerializeToUtf8Bytes(parameter, callJsonTypeInfo);
      var content = new ByteArrayContent(bytes);
      content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
      return Task.FromResult<HttpContent?>(content);
   }

   [RequiresUnreferencedCode("JSON")]
   protected override Task<JsonContent?> GetJsonContent<TP>(string scriptName, TP? parameter, JsonSerializerOptions? jsonOptions) where TP : default => 
      Task.FromResult<JsonContent?>(JsonContent.Create(parameter, mediaType: null, jsonOptions));
}