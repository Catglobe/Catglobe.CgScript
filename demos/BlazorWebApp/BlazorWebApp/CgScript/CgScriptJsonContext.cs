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
internal partial class CgScriptJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
