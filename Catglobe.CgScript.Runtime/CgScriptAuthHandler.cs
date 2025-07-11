using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Catglobe.CgScript.Runtime;

/// <summary>
/// A delegating handler that adds the access token to the Authorization header of the request.
/// </summary>
/// <remarks>
/// <example>
/// <code>
/// builder.Services.AddHttpContextAccessor();
/// builder.Services.AddScoped&lt;CgScriptAuthHandler&gt;();
/// builder.Services.AddHttpClient&lt;IWeatherForecaster, ServerWeatherForecaster&gt;(httpClient =&gt; {
///     httpClient.BaseAddress = new(SITE+"/api/cgscript");
///  })
/// .AddHttpMessageHandler&lt;CgScriptAuthHandler&gt;();
/// </code>
/// </example>
/// </remarks>
/// <param name="httpContextAccessor">The accessor </param>
public class CgScriptAuthHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
   /// <summary>
   /// Append the access token to headers
   /// </summary>
   protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
   {
      var httpContext = httpContextAccessor.HttpContext ??
                        throw new InvalidOperationException("No HttpContext available from the IHttpContextAccessor!");

      string? accessToken = null;
      if ((httpContext.Items.TryGetValue("access_token", out var accessTokenObj) && accessTokenObj is string accessToken2)) accessToken = accessToken2;
      if (!string.IsNullOrEmpty(accessToken) || httpContext.User.Identity?.IsAuthenticated == true)
      {
         accessToken ??= await httpContext.GetTokenAsync("access_token") ??
                         throw new InvalidOperationException("No access_token was saved");

         request.Headers.Authorization = new("Bearer", accessToken);
      }
      return await base.SendAsync(request, cancellationToken);
   }
}