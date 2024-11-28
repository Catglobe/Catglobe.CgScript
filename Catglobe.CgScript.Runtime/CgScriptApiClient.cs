﻿using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Catglobe.CgScript.Common;

namespace Catglobe.CgScript.Runtime;

internal class CgScriptApiClient(HttpClient httpClient, IScriptMapping map) : ApiClientBase(httpClient)
{
   protected override async ValueTask<string> GetPath(string scriptName, string? additionalParameters = null)
   {
      await map.EnsureDownloaded();
      return $"run/{map.GetIdOf(scriptName)}{additionalParameters ?? ""}";
   }

   protected override Task<JsonContent?> GetJsonContent<TP>(string scriptName, TP? parameter, JsonTypeInfo<TP> callJsonTypeInfo) where TP : default => 
      Task.FromResult(parameter is null ? default : JsonContent.Create(parameter, mediaType: null, jsonTypeInfo: callJsonTypeInfo));

   [RequiresUnreferencedCode("JSON")]
   protected override Task<JsonContent?> GetJsonContent<TP>(string scriptName, TP? parameter, JsonSerializerOptions? options) where TP : default => 
      Task.FromResult<JsonContent?>(JsonContent.Create(parameter, mediaType: null, options));
}