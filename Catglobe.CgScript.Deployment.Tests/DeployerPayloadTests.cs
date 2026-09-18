using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Catglobe.CgScript.Common;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Catglobe.CgScript.Deployment.Tests;

/// <summary>
/// Drives the real <see cref="Deployer.Sync"/> against a stub HTTP handler that captures every request body,
/// so the assertions run against the actual bytes that would go on the wire.
/// </summary>
public class DeployerPayloadTests(ITestOutputHelper output)
{
   private const string PiiScript = "PiiScript";
   private const string PlainScript = "Plain";

   [Fact]
   public async Task SyncPayloadMarksThePiiScriptAndOnlyThatScript()
   {
      using var folder = new TempScriptFolder();
      folder.Write("PiiScript@7.pii.cgs", "return 1;");
      folder.Write("Plain.cgs", "return 1;");

      var capture = await RunSync(folder.Root);

      var body = capture.BodyOf("UpdateScripts");
      output.WriteLine($"UpdateScripts body: {body}");

      var entries = JsonNode.Parse(body)!.AsArray();
      Assert.Equal(2, entries.Count);
      var pii   = FindEntry(entries, PiiScript);
      var plain = FindEntry(entries, PlainScript);

      Assert.True(pii["canAccessPII"]?.GetValue<bool>() ?? false,
         $"{PiiScript} (from PiiScript@7.pii.cgs) must declare canAccessPII: true. Raw body: {body}");
      Assert.False(plain["canAccessPII"]?.GetValue<bool>() ?? true,
         $"{PlainScript} (from Plain.cgs) must declare canAccessPII: false. Raw body: {body}");
      Assert.Contains("\"canAccessPII\":true", body);
      Assert.Contains("\"canAccessPII\":false", body);
   }

   [Fact]
   public async Task SyncCallsSyncMapBeforeUpdateScriptsAndAsksNoPiiScope()
   {
      using var folder = new TempScriptFolder();
      folder.Write("PiiScript@7.pii.cgs", "return 1;");
      folder.Write("Plain.cgs", "return 1;");

      var capture = await RunSync(folder.Root);
      output.WriteLine($"recorded requests: {capture.Describe()}");

      var syncMapIndex       = capture.IndexOf("SyncMap");
      var updateScriptsIndex = capture.IndexOf("UpdateScripts");
      Assert.True(syncMapIndex >= 0 && updateScriptsIndex >= 0, $"both calls must happen. Captured: {capture.Describe()}");
      Assert.True(syncMapIndex < updateScriptsIndex, $"SyncMap must be called before UpdateScripts. Captured: {capture.Describe()}");
      Assert.Equal("POST", capture.Requests[updateScriptsIndex].Method);

      var tokenBody = capture.BodyOf("connect/token");
      output.WriteLine($"token body: {tokenBody}");

      Assert.DoesNotContain("pii", tokenBody, StringComparison.OrdinalIgnoreCase);
      Assert.Equal("scriptdeployment:w", CaptureHandler.FormValue(tokenBody, "scope"));
   }

   [Fact]
   public async Task SyncFailsBeforeAnyHttpRequestForAMalformedFileName()
   {
      using var folder = new TempScriptFolder();
      folder.Write("Broken@1.public.pii.cgs", "return 1;");

      var capture = new CaptureHandler();
      var exception = await Assert.ThrowsAsync<InvalidScriptFileNameException>(
         () => CreateDeployer(folder.Root, capture).Sync("Deployment", CancellationToken.None));

      Assert.Contains("Broken@1.public.pii.cgs", exception.Message);
      output.WriteLine($"thrown: {exception.Message}");
      Assert.Empty(capture.Requests);
   }

   private static JsonNode FindEntry(JsonArray entries, string scriptName)
   {
      var matches = entries.Where(entry => entry!["scriptName"]?.GetValue<string>() == scriptName).ToList();
      var entry = Assert.Single(matches);
      return entry!;
   }

   private static async Task<CaptureHandler> RunSync(string scriptFolder)
   {
      var capture = new CaptureHandler();
      await CreateDeployer(scriptFolder, capture).Sync("Deployment", CancellationToken.None);
      return capture;
   }

   private static Deployer CreateDeployer(string scriptFolder, HttpMessageHandler handler)
   {
      var options = Options.Create(new DeploymentOptions {
         Authority = new Uri("https://example.test"),
         ClientId = "test-client",
         ClientSecret = "test-secret",
         FolderResourceId = 4242,
         ScriptFolder = scriptFolder,
      });

      var authenticator = new DeploymentAuthenticator(
         new HttpClient(handler, disposeHandler: false) { BaseAddress = options.Value.Authority },
         options);
      var authHandler = new DeploymentAuthHandler(authenticator) { InnerHandler = handler };
      var httpClient = new HttpClient(authHandler, disposeHandler: false) {
         BaseAddress = new Uri(options.Value.Authority, "api/CgScriptDeployment/"),
      };

      return new Deployer(httpClient, new FilesFromDirectoryScriptProvider(options), new StubMapping(), options);
   }

   private sealed class StubMapping : IScriptMapping
   {
      public int ResetCount { get; private set; }

      public int GetIdOf(string scriptName) => throw new KeyNotFoundException(scriptName);

      public ValueTask EnsureDownloaded() => ValueTask.CompletedTask;

      public void Reset() => ResetCount++;
   }

   private sealed class CaptureHandler : HttpMessageHandler
   {
      private readonly List<(string Method, string Path, string Body)> _requests = [];

      public IReadOnlyList<(string Method, string Path, string Body)> Requests => _requests;

      public string Describe() =>
         string.Join(" | ", _requests.Select(request => $"{request.Method} {request.Path} body={request.Body}"));

      public int IndexOf(string pathFragment) =>
         _requests.FindIndex(request => request.Path.Contains(pathFragment, StringComparison.OrdinalIgnoreCase));

      public string BodyOf(string pathFragment)
      {
         var index = IndexOf(pathFragment);
         Assert.True(index >= 0, $"no captured request for '{pathFragment}'. Captured: {Describe()}");
         return _requests[index].Body;
      }

      public static string FormValue(string formBody, string key) =>
         formBody.Split('&', StringSplitOptions.RemoveEmptyEntries)
                 .Select(part => part.Split('=', 2))
                 .Where(pair => pair.Length == 2 && Uri.UnescapeDataString(pair[0]) == key)
                 .Select(pair => Uri.UnescapeDataString(pair[1]))
                 .Single();

      protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
      {
         var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
         var path = request.RequestUri!.AbsolutePath;
         _requests.Add((request.Method.Method, path, body));

         if (path.EndsWith("/connect/token", StringComparison.Ordinal))
            return Json("""{"access_token":"test-token"}""");
         if (path.Contains("/SyncMap/", StringComparison.Ordinal))
            return Json(BuildMapFrom(body));
         if (path.Contains("/UpdateScripts/", StringComparison.Ordinal))
            return Json("{}");

         return new HttpResponseMessage(HttpStatusCode.NotFound) {
            Content = new StringContent($"unexpected request {request.Method} {request.RequestUri}"),
         };
      }

      /// <summary>Builds the deployment map from the scripts the deployer just asked about, fresh for every call.</summary>
      private static string BuildMapFrom(string syncMapBody)
      {
         var map = new JsonObject();
         var id  = 1;
         foreach (var script in JsonNode.Parse(syncMapBody)!.AsArray())
            map[script!["scriptName"]!.GetValue<string>()] = new JsonObject {
               ["allowExecuteWithoutLogin"] = false,
               ["scriptResourceId"]         = id,
               ["sha256"]                   = $"sha-{id++}",
            };
         return map.ToJsonString();
      }

      private static HttpResponseMessage Json(string json) =>
         new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
   }

   private sealed class TempScriptFolder : IDisposable
   {
      public TempScriptFolder() => Root = Directory.CreateTempSubdirectory("cgscript-t3-").FullName;

      public string Root { get; }

      public string Write(string relativePath, string content)
      {
         var fullPath = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
         Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
         File.WriteAllText(fullPath, content);
         return fullPath;
      }

      public void Dispose() => Directory.Delete(Root, recursive: true);
   }
}
