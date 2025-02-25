using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Catglobe.CgScript.Deployment;

internal partial class DeploymentAuthenticator(HttpClient httpClient, IOptions<DeploymentOptions> options)
{
   private string? _accessToken;

   public async Task<string> GetToken(CancellationToken cancellationToken) => _accessToken ??= await AcquireToken(cancellationToken).ConfigureAwait(false);

   private async Task<string> AcquireToken(CancellationToken cancellationToken)
   {
      var o = options.Value;
      var requestData = new Dictionary<string, string> {
         {"grant_type", "client_credentials"},
         {"client_id", o.ClientId},
         {"client_secret", o.ClientSecret},
         // ReSharper disable once StringLiteralTypo
         {"scope", "scriptdeployment:w"},
      };

      var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/connect/token") { Content = new FormUrlEncodedContent(requestData), Headers = { Accept = { new("application/json") } } };

      var response = await httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
      response.EnsureSuccessStatusCode();

      var tokenResponse = await response.Content.ReadFromJsonAsync(Serializer.Default.TokenResponse, cancellationToken).ConfigureAwait(false) ?? throw new IOException("Failed to obtain authorization");

      return tokenResponse.AccessToken;
   }

   private class TokenResponse
   {
      [JsonPropertyName("access_token")] public string AccessToken { get; set; } = null!;
   }

   [JsonSerializable(typeof(TokenResponse))]
   [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
   private partial class Serializer : JsonSerializerContext;
}
