using System.Collections.Generic;
using System.Text.Json.Serialization;
using Catglobe.CgScript;

namespace BlazorWebApp;

/// <summary>
/// AOT-safe STJ context for CgScript generated wrappers.
/// Add a [JsonSerializable(typeof(...))] entry here for every non-primitive
/// return type that any .cgs script in this project can return.
/// </summary>
[CgScriptSerializer]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(IEnumerable<object>))]
[JsonSerializable(typeof(IEnumerable<TagItem>))]
[JsonSerializable(typeof(TagSummary))]
[JsonSerializable(typeof(ProjectForEdit))]
[JsonSerializable(typeof(IEnumerable<ProjectQuotaSetupItem>))] 
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(IEnumerable<string>))] 
[JsonSerializable(typeof(IEnumerable<string>))] 
internal partial class CgScriptJsonContext : System.Text.Json.Serialization.JsonSerializerContext;

//dummy
public record ProjectQuotaSetupItem { }

//dummy
public record ProjectForEdit { }
