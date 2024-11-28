﻿using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Catglobe.CgScript.Common;

namespace Catglobe.CgScript.Runtime;

internal partial class DevelopmentModeCgScriptApiClient(HttpClient httpClient, IScriptProvider scriptProvider) : ApiClientBase(httpClient)
{
   IReadOnlyDictionary<string, IScriptDefinition>? _scriptDefinitions;
   private BaseCgScriptMaker?                      _cgScriptMaker;
   protected override async ValueTask<string> GetPath(string scriptName, string? additionalParameters = null)
   {
      if (_scriptDefinitions == null)
      {
         _scriptDefinitions = await scriptProvider.GetAll();
         _cgScriptMaker = new CgScriptMakerForDevelopment(_scriptDefinitions);
      }
      return $"dynamicRun{additionalParameters ?? ""}";
   }

   protected override async Task<JsonContent?> GetJsonContent<TP>(string scriptName, TP? parameter, JsonTypeInfo<TP> callJsonTypeInfo) where TP : default => 
      JsonContent.Create(new DynamicCgScript<TP>(scriptName, await GetScript(scriptName), parameter, callJsonTypeInfo), mediaType: null, jsonTypeInfo: DynamicCgScriptSerializer.Default.IDynamicScript);

   [RequiresUnreferencedCode("JSON")]
   protected override async Task<JsonContent?> GetJsonContent<TP>(string scriptName, TP? parameter, JsonSerializerOptions? callJsonTypeInfo) where TP : default
   {
      var script = await GetScript(scriptName);

      throw new NotImplementedException();
   }

   private Task<string> GetScript(string scriptName) => _cgScriptMaker!.GetContent(scriptName);

   [JsonConverter(typeof(DynamicConverter))]
   internal interface IDynamicScript
   {
      string ScriptName { get; }
      string Script     { get; }
      void   WriteParameter(Utf8JsonWriter writer);
   }

   [JsonConverter(typeof(DynamicConverter))]
   internal record DynamicCgScript<T>(string ScriptName, string Script, T? Parameter, JsonTypeInfo<T> jsonTypeInfo) : IDynamicScript
   {
      public void WriteParameter(Utf8JsonWriter writer)
      {
         writer.WritePropertyName("parameter");
         if (Parameter is null)
            writer.WriteNullValue();
         else
            JsonSerializer.Serialize(writer, Parameter, jsonTypeInfo);
      }
   }

   internal class DynamicConverter : JsonConverter<IDynamicScript>
   {
      public override IDynamicScript? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotImplementedException();

      public override void Write(Utf8JsonWriter writer, IDynamicScript value, JsonSerializerOptions options)
      {
         writer.WriteStartObject();
         writer.WriteString("scriptName"u8, value.ScriptName);
         writer.WriteString("script"u8, value.Script);
         value.WriteParameter(writer);
         writer.WriteEndObject();
      }
   }


   [JsonSerializable(typeof(IDynamicScript))]
   [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
   private partial class DynamicCgScriptSerializer : JsonSerializerContext;
}


