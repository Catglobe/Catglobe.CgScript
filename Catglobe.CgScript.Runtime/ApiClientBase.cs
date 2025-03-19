using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;

namespace Catglobe.CgScript.Runtime;

internal abstract class ApiClientBase(HttpClient httpClient, ILogger<ICgScriptApiClient> logger) : ICgScriptApiClient
{
   public async Task<ScriptResult<TR>> Execute<TP, TR>(string scriptName, TP parameter, JsonTypeInfo<TP> callJsonTypeInfo, JsonTypeInfo<TR> resultJsonTypeInfo, CancellationToken cancellationToken) =>
      await ParseResponse(await httpClient.PostAsync(await GetPath(scriptName), await GetJsonContent(scriptName, parameter, callJsonTypeInfo), cancellationToken).ConfigureAwait(false), resultJsonTypeInfo, cancellationToken);

   public async Task<ScriptResult<TR>> ExecuteArray<TP, TR>(string scriptName, TP parameter, JsonTypeInfo<TP> callJsonTypeInfo, JsonTypeInfo<TR> resultJsonTypeInfo, CancellationToken cancellationToken) =>
      await ParseResponse(await httpClient.PostAsync(await GetPath(scriptName, "?expandParameters=true"), await GetJsonContent(scriptName, parameter, callJsonTypeInfo), cancellationToken).ConfigureAwait(false), resultJsonTypeInfo, cancellationToken);

   public async Task<ScriptResult<TR>> Execute<TR>(string scriptName, JsonTypeInfo<TR> resultJsonTypeInfo, CancellationToken cancellationToken = default) =>
      await ParseResponse(await httpClient.PostAsync(await GetPath(scriptName, "?expandParameters=true"), await GetJsonContent(scriptName, null, (JsonTypeInfo<object>)null!), cancellationToken).ConfigureAwait(false), resultJsonTypeInfo, cancellationToken);

   private async Task<ScriptResult<TR>> ParseResponse<TR>(HttpResponseMessage call, JsonTypeInfo<TR> resultJsonTypeInfo, CancellationToken cancellationToken)
   {
      var jsonTypeInfo = JsonMetadataServices.CreateValueInfo<ScriptResult<TR>>(new(){TypeInfoResolver = new DummyResolver<TR>(resultJsonTypeInfo)}, new ScriptResultConverterWithTypeInfo<TR>(resultJsonTypeInfo));
      //if not successful, log the error the server sent
      if (!call.IsSuccessStatusCode)
      {
         logger.LogInformation(await call.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
         call.EnsureSuccessStatusCode();
      }
      var result = await call.Content.ReadFromJsonAsync(jsonTypeInfo, cancellationToken).ConfigureAwait(false);
      return result ?? throw new IOException("Could not deserialize result");
   }

   [RequiresUnreferencedCode("JSON")]
   public async Task<ScriptResult<TR>> Execute<TP, TR>(string scriptName, TP parameter, JsonSerializerOptions? options, CancellationToken cancellationToken = default) =>
      await ParseResponse<TR>(await httpClient.PostAsync(await GetPath(scriptName), await GetJsonContent(scriptName, parameter, options), cancellationToken).ConfigureAwait(false), options, cancellationToken);
   [RequiresUnreferencedCode("JSON")]
   public async Task<ScriptResult<TR>> ExecuteArray<TP, TR>(string scriptName, TP parameter, JsonSerializerOptions? options, CancellationToken cancellationToken = default) =>
      await ParseResponse<TR>(await httpClient.PostAsync(await GetPath(scriptName, "?expandParameters=true"), await GetJsonContent(scriptName, parameter, options), cancellationToken).ConfigureAwait(false), options, cancellationToken);

   [RequiresUnreferencedCode("JSON")]
   public async Task<ScriptResult<TR>> Execute<TR>(string scriptName, IReadOnlyCollection<object> parameters, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default) =>
      await ParseResponse<TR>(await httpClient.PostAsync(await GetPath(scriptName, "?expandParameters=true"), await GetJsonContent(scriptName, parameters, options), cancellationToken).ConfigureAwait(false), options, cancellationToken);

   [RequiresUnreferencedCode("JSON")]
   public async Task<ScriptResult<TR>> Execute<TR>(string scriptName, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default) =>
      await ParseResponse<TR>(await httpClient.PostAsync(await GetPath(scriptName), await GetJsonContent(scriptName, null, (JsonTypeInfo<object>)null!), cancellationToken).ConfigureAwait(false), options, cancellationToken);

   [RequiresUnreferencedCode("JSON")]
   private async Task<ScriptResult<TR>> ParseResponse<TR>(HttpResponseMessage call, JsonSerializerOptions? options, CancellationToken cancellationToken)
   {
      var retOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new ScriptResultConverterFactory<TR>(options) } };
      //if not successful, log the error the server sent
      if (!call.IsSuccessStatusCode)
      {
         logger.LogInformation(await call.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
         call.EnsureSuccessStatusCode();
      }
      var result = (ScriptResult<TR>?)await call.Content.ReadFromJsonAsync(typeof(ScriptResult<TR>), retOptions, cancellationToken).ConfigureAwait(false);
      return result ?? throw new IOException("Could not deserialize result");
   }

   protected abstract ValueTask<string> GetPath(string scriptName, string? additionalParameters = null);

   protected abstract Task<JsonContent?> GetJsonContent<TP>(string scriptName, TP? parameter, JsonTypeInfo<TP> callJsonTypeInfo);

   [RequiresUnreferencedCode("JSON")]
   protected abstract Task<JsonContent?> GetJsonContent<TP>(string scriptName, TP? parameter, JsonSerializerOptions? jsonOptions);

   private class DummyResolver<T>(JsonTypeInfo info) : IJsonTypeInfoResolver
   {
      public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => type == typeof(T) ? info : null;
   }
}
