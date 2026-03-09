namespace BlazorWebApp.DemoUsage;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

internal static class LoginLogoutEndpointRouteBuilderExtensions
{
   internal static IEndpointConventionBuilder MapLoginAndLogout(this IEndpointRouteBuilder endpoints)
   {
      var group = endpoints.MapGroup("");

      group.MapGet("/login", (string? returnUrl) => TypedResults.Challenge(GetAuthProperties(returnUrl)))
           .AllowAnonymous();

      // Sign out of the Cookie and OIDC handlers. If you do not sign out with the OIDC handler,
      // the user will automatically be signed back in the next time they visit a page that requires authentication
      // without being able to choose another account.
      group.MapPost("/logout", ([FromForm] string? returnUrl) => TypedResults.SignOut(GetAuthProperties(returnUrl),
                                                                                      [CookieAuthenticationDefaults.AuthenticationScheme, SetupRuntime.SCHEMENAME]));

      return group;
   }

   private static AuthenticationProperties GetAuthProperties(string? returnUrl)
   {
      // TODO: Use HttpContext.Request.PathBase instead.
      const string pathBase = "/";

      // Prevent open redirects.
      if (string.IsNullOrEmpty(returnUrl))
      {
         returnUrl = pathBase;
      }
      else if (!Uri.IsWellFormedUriString(returnUrl, UriKind.Relative))
      {
         returnUrl = new Uri(returnUrl, UriKind.Absolute).PathAndQuery;
      }
      else if (returnUrl[0] != '/')
      {
         returnUrl = $"{pathBase}{returnUrl}";
      }

      return new AuthenticationProperties { RedirectUri = returnUrl };
   }
}

   public static partial class CgScriptWrappers
   {
      // Generated from WeatherForecast.cgs
      public static global::System.Threading.Tasks.Task<global::Catglobe.CgScript.Runtime.ScriptResult<object[]>> WeatherForecast(this global::Catglobe.CgScript.Runtime.ICgScriptApiClient client, string city, double numberOfDays, global::System.Threading.CancellationToken ct = default)
      {
         return client.Execute<WeatherForecastParams, object[]>("WeatherForecast", new WeatherForecastParams(city, numberOfDays), cancellationToken: ct);
#pragma warning restore IL2026, IL3050
      }

      private record WeatherForecastParams(string City, double NumberOfDays);

   }
