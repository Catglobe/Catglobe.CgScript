using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Catglobe.CgScript.Common;
using Microsoft.Extensions.Logging;

namespace Catglobe.CgScript.Runtime;

internal abstract class ApiClientBase(HttpClient httpClient, ILogger<ICgScriptApiClient> logger) : ICgScriptApiClient, ILongRunningCgScriptApiClient
{
   public Task<ScriptResult<TR>> Execute<TP, TR>(string scriptName, TP parameter, JsonTypeInfo<TP> callJsonTypeInfo, JsonTypeInfo<TR> resultJsonTypeInfo, CancellationToken cancellationToken) =>
      ExecuteCustomOptions(scriptName, parameter, callJsonTypeInfo, resultJsonTypeInfo, null, cancellationToken);

   public async Task<ScriptResult<TR>> ExecuteCustomOptions<TP, TR>(string scriptName, TP parameter, JsonTypeInfo<TP> callJsonTypeInfo, JsonTypeInfo<TR> resultJsonTypeInfo,
                                                                   Action<HttpRequestOptions>? applyOptions, CancellationToken cancellationToken)
   {
      using var activity = CgScriptTelemetry.Source.StartActivity(scriptName);
      var       path     = await GetPath(scriptName);
      var       jsonContent = await GetJsonContent(scriptName, parameter, callJsonTypeInfo);
      var       request     = new HttpRequestMessage(HttpMethod.Post, path) {Content = jsonContent,};
      applyOptions?.Invoke(request.Options);
      var httpResponseMessage = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
      return await ParseResponse(httpResponseMessage, resultJsonTypeInfo, cancellationToken);
   }

   public Task<ScriptResult<TR>> ExecuteArray<TP, TR>(string scriptName, TP parameter, JsonTypeInfo<TP> callJsonTypeInfo, JsonTypeInfo<TR> resultJsonTypeInfo, CancellationToken cancellationToken) =>
      ExecuteArrayCustomOptions(scriptName, parameter, callJsonTypeInfo, resultJsonTypeInfo, null, cancellationToken);

   public async Task<ScriptResult<TR>> ExecuteArrayCustomOptions<TP, TR>(string scriptName, TP parameter, JsonTypeInfo<TP> callJsonTypeInfo, JsonTypeInfo<TR> resultJsonTypeInfo,
                                                                        Action<HttpRequestOptions>? applyOptions, CancellationToken cancellationToken)
   {
      using var activity = CgScriptTelemetry.Source.StartActivity(scriptName);
      var       path     = await GetPath(scriptName, "?expandParameters=true");
      var       jsonContent = await GetJsonContent(scriptName, parameter, callJsonTypeInfo);
      var       request     = new HttpRequestMessage(HttpMethod.Post, path) {Content = jsonContent,};
      applyOptions?.Invoke(request.Options);
      var httpResponseMessage = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
      return await ParseResponse(httpResponseMessage, resultJsonTypeInfo, cancellationToken);
   }

   public Task<ScriptResult<TR>> Execute<TR>(string scriptName, JsonTypeInfo<TR> resultJsonTypeInfo, CancellationToken cancellationToken = default) =>
      ExecuteCustomOptions(scriptName, resultJsonTypeInfo, null, cancellationToken);

   public async Task<ScriptResult<TR>> ExecuteCustomOptions<TR>(string scriptName, JsonTypeInfo<TR> resultJsonTypeInfo, Action<HttpRequestOptions>? applyOptions,
                                                                CancellationToken cancellationToken = default)
   {
      using var activity = CgScriptTelemetry.Source.StartActivity(scriptName);
      var       path     = await GetPath(scriptName, "?expandParameters=true");
      var       jsonContent = await GetJsonContent(scriptName, null, (JsonTypeInfo<object>)null!);
      var       request     = new HttpRequestMessage(HttpMethod.Post, path) {Content = jsonContent,};
      applyOptions?.Invoke(request.Options);
      var httpResponseMessage = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
      return await ParseResponse(httpResponseMessage, resultJsonTypeInfo, cancellationToken);
   }

   private async Task<ScriptResult<TR>> ParseResponse<TR>(HttpResponseMessage call, JsonTypeInfo<TR> resultJsonTypeInfo, CancellationToken cancellationToken)
   {
      var jsonTypeInfo = JsonMetadataServices.CreateValueInfo<ScriptResult<TR>>(new(){TypeInfoResolver = new DummyResolver<TR>(resultJsonTypeInfo)}, new ScriptResultConverterWithTypeInfo<TR>(resultJsonTypeInfo));
      //if not successful, log the error the server sent
      if (!call.IsSuccessStatusCode)
      {
         Activity.Current?.SetStatus(ActivityStatusCode.Error);
         logger.LogInformation(await call.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
         call.EnsureSuccessStatusCode();
      }
      try
      {
         var result = await call.Content.ReadFromJsonAsync(jsonTypeInfo, cancellationToken).ConfigureAwait(false);
         if (result?.Error is not null)
         {
            Activity.Current?.SetStatus(ActivityStatusCode.Error);
         }
         return result ?? throw new IOException("Could not deserialize result");
      } catch (OperationCanceledException)
      {
         throw;
      } catch (Exception e)
      {
         Activity.Current?.SetStatus(ActivityStatusCode.Error);
         Activity.Current?.AddException(e);
         throw;
      }
   }

   [RequiresUnreferencedCode("JSON")]
   public Task<ScriptResult<TR>> Execute<TP, TR>(string scriptName, TP parameter, JsonSerializerOptions? options, CancellationToken cancellationToken = default) =>
      ExecuteCustomOptions<TP, TR>(scriptName, parameter, options, null, cancellationToken);

   [RequiresUnreferencedCode("JSON")]
   public async Task<ScriptResult<TR>> ExecuteCustomOptions<TP, TR>(string scriptName, TP parameter, JsonSerializerOptions? options, Action<HttpRequestOptions>? applyOptions,
                                                                    CancellationToken cancellationToken = default)
   {
      using var activity = CgScriptTelemetry.Source.StartActivity(scriptName);
      var       path     = await GetPath(scriptName);
      var       jsonContent = await GetJsonContent(scriptName, parameter, options);
      var       request     = new HttpRequestMessage(HttpMethod.Post, path) {Content = jsonContent,};
      applyOptions?.Invoke(request.Options);
      var httpResponseMessage = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
      return await ParseResponse<TR>(httpResponseMessage, options, cancellationToken);
   }

   [RequiresUnreferencedCode("JSON")]
   public async Task<ScriptResult<TR>> ExecuteArrayCustomOptions<TP, TR>(string scriptName, TP parameter, JsonSerializerOptions? options, Action<HttpRequestOptions>? applyOptions, CancellationToken cancellationToken = default)
   {
      using var activity    = CgScriptTelemetry.Source.StartActivity(scriptName);
      var       path        = await GetPath(scriptName, "?expandParameters=true");
      var       jsonContent = await GetJsonContent(scriptName, parameter, options);
      var       request     = new HttpRequestMessage(HttpMethod.Post, path) {Content = jsonContent,};
      applyOptions?.Invoke(request.Options);
      var httpResponseMessage = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
      return await ParseResponse<TR>(httpResponseMessage, options, cancellationToken);
   }

   [RequiresUnreferencedCode("JSON")]
   public Task<ScriptResult<TR>> ExecuteArray<TP, TR>(string scriptName, TP parameter, JsonSerializerOptions? options, CancellationToken cancellationToken = default) =>
      ExecuteArrayCustomOptions<TP, TR>(scriptName, parameter, options, null, cancellationToken);

   [RequiresUnreferencedCode("JSON")]
   public Task<ScriptResult<TR>> Execute<TR>(string scriptName, IReadOnlyCollection<object> parameters, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default) =>
      ExecuteCustomOptions<TR>(scriptName, parameters, options, null, cancellationToken);

   [RequiresUnreferencedCode("JSON")]
   public async Task<ScriptResult<TR>> ExecuteCustomOptions<TR>(string scriptName, IReadOnlyCollection<object> parameters, JsonSerializerOptions? options,
                                                                Action<HttpRequestOptions>? applyOptions, CancellationToken cancellationToken = default)
   {
      using var activity = CgScriptTelemetry.Source.StartActivity(scriptName);
      var       path     = await GetPath(scriptName, "?expandParameters=true");
      var       jsonContent = await GetJsonContent(scriptName, parameters, options);
      var       request     = new HttpRequestMessage(HttpMethod.Post, path) {Content = jsonContent,};
      applyOptions?.Invoke(request.Options);
      var httpResponseMessage = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
      return await ParseResponse<TR>(httpResponseMessage, options, cancellationToken);
   }

   [RequiresUnreferencedCode("JSON")]
   public Task<ScriptResult<TR>> Execute<TR>(string scriptName, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default) =>
      ExecuteCustomOptions<TR>(scriptName, options, null, cancellationToken);

   [RequiresUnreferencedCode("JSON")]
   public async Task<ScriptResult<TR>> ExecuteCustomOptions<TR>(string scriptName, JsonSerializerOptions? options, Action<HttpRequestOptions>? applyOptions,
                                                                CancellationToken cancellationToken = default)
   {
      using var activity = CgScriptTelemetry.Source.StartActivity(scriptName);
      var       path     = await GetPath(scriptName);
      var       jsonContent = await GetJsonContent(scriptName, null, (JsonTypeInfo<object>)null!);
      var       request     = new HttpRequestMessage(HttpMethod.Post, path) {Content = jsonContent,};
      applyOptions?.Invoke(request.Options);
      var httpResponseMessage = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
      return await ParseResponse<TR>(httpResponseMessage, options, cancellationToken);
   }

   [RequiresUnreferencedCode("JSON")]
   private async Task<ScriptResult<TR>> ParseResponse<TR>(HttpResponseMessage call, JsonSerializerOptions? options, CancellationToken cancellationToken)
   {
      var retOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new ScriptResultConverterFactory<TR>(options) } };
      //if not successful, log the error the server sent
      if (!call.IsSuccessStatusCode)
      {
         Activity.Current?.SetStatus(ActivityStatusCode.Error);
         logger.LogInformation(await call.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
         call.EnsureSuccessStatusCode();
      }
      try
      {
         var result = (ScriptResult<TR>?)await call.Content.ReadFromJsonAsync(typeof(ScriptResult<TR>), retOptions, cancellationToken).ConfigureAwait(false);
         if (result?.Error is not null)
         {
            Activity.Current?.SetStatus(ActivityStatusCode.Error);
         }
         return result ?? throw new IOException("Could not deserialize result");
      } catch (OperationCanceledException)
      {
         throw;
      } catch (Exception e)
      {
         Activity.Current?.SetStatus(ActivityStatusCode.Error);
         Activity.Current?.AddException(e);
         throw;
      }
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
