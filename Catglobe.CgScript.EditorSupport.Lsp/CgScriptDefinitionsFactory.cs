using Catglobe.CgScript.EditorSupport.Parsing;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Catglobe.CgScript.EditorSupport.Lsp;

/// <summary>
/// Factory for creating <see cref="CgScriptDefinitions"/> instances that fetch live definitions
/// from a running Catglobe server over HTTP.
/// </summary>
public static class CgScriptDefinitionsFactory
{
   /// <summary>
   /// Fetches definitions from <c>{siteUrl}/api/cgscript/definitions</c>.
   /// Falls back to the bundled definitions if the request fails.
   /// On parse failure, returns a loader with <see cref="CgScriptDefinitions.LoadError"/> set.
   /// </summary>
   public static async Task<CgScriptDefinitions> CreateFromUrlAsync(string siteUrl, CancellationToken ct = default)
   {
      var url = $"{siteUrl.TrimEnd('/')}/api/cgscript/definitions";
      CgScriptDefinitions.TraceSource.TraceInformation("Loading CgScript definitions from {0}", url);
      try
      {
         using var http = new HttpClient();
         await using var stream = await http.GetStreamAsync(url, ct);
         var result = CgScriptDefinitions.FromJsonStream(stream);
         CgScriptDefinitions.TraceSource.TraceInformation(
            "Loaded definitions from {0}: {1} functions, {2} objects, {3} constants",
            url, result.Functions.Count, result.Objects.Count, result.Constants.Count);
         return result;
      }
      catch (JsonException ex) when (ex is not null)
      {
         CgScriptDefinitions.TraceSource.TraceEvent(TraceEventType.Error, 0,
            "Failed to parse definitions from {0} — plugin may be out of date: {1}", url, ex.Message);
         return new CgScriptDefinitions(loadError:
            $"CgScript plugin is out of date: the definition response from '{url}' could not be parsed " +
            $"({ex.Message}). Please upgrade the CgScript plugin.");
      }
      catch (Exception ex)
      {
         CgScriptDefinitions.TraceSource.TraceEvent(TraceEventType.Warning, 0,
            "Failed to fetch definitions from {0} — falling back to bundled definitions: {1}", url, ex.Message);
         return new CgScriptDefinitions();
      }
   }
}
