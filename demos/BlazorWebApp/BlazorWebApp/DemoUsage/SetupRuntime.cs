using Catglobe.CgScript.Common;
using Catglobe.CgScript.Runtime;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Transforms;

namespace BlazorWebApp.DemoUsage;

internal class SetupRuntime
{
   internal const string SCHEMENAME = "CatglobeOidc"; //must match the section name in appsettings.json

   public static void Configure(WebApplicationBuilder builder)
   {
      // Add services to the container.
      var services = builder.Services;
      services.AddAuthentication(SCHEMENAME)
              .AddOpenIdConnect(SCHEMENAME, oidcOptions => {
                  builder.Configuration.GetSection(SCHEMENAME).Bind(oidcOptions);
                  // ........................................................................
                  // The OIDC handler must use a sign-in scheme capable of persisting 
                  // user credentials across requests.
                  oidcOptions.SignInScheme                            = CookieAuthenticationDefaults.AuthenticationScheme;
                  oidcOptions.TokenValidationParameters.NameClaimType = "name";

                  // to get the locale/culture
                  oidcOptions.ClaimActions.MapUniqueJsonKey("locale",  "locale");
                  oidcOptions.ClaimActions.MapUniqueJsonKey("culture", "culture");

                 // must be true to get the zoneinfo and email claims
                 oidcOptions.GetClaimsFromUserInfoEndpoint = true;
                 oidcOptions.ClaimActions.MapUniqueJsonKey("zoneinfo", "zoneinfo");
               })
              .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);

      services.AddSingleton<CookieOidcRefresher>();
      // attaches a cookie OnValidatePrincipal callback to get
      // a new access token when the current one expires, and reissue a cookie with the
      // new access token saved inside. If the refresh fails, the user will be signed
      // out. OIDC connect options are set for saving tokens and the offline access
      // scope.
      services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme).Configure<CookieOidcRefresher>((cookieOptions, refresher) => {
         cookieOptions.Events.OnValidatePrincipal = context => refresher.ValidateOrRefreshCookieAsync(context, SCHEMENAME);
      });

      services.AddAuthorization();

      //Add this, if you need the browser (blazor wasm or javascript) to be able to call CgScript
      services.AddHttpForwarder();

      services.AddCgScript(builder.Configuration.GetSection("CatglobeApi"), builder.Environment.IsDevelopment(), "AuthClient");

      //and this is custom to this specific example... The rest above can be reused for pretty much any site
      services.AddSingleton<IWeatherForecaster, ServerWeatherForecaster>();
      services.AddSingleton<IPublicWeatherForecaster, ServerPublicWeatherForecaster>();
   }

   public static void Use(WebApplication app)
   {
      string[] culture = ["da-DK", "en-US", "en-GB"];
      app.UseRequestLocalization(o => {
         o.AddSupportedCultures(culture)
          .AddSupportedUICultures(culture)
          .SetDefaultCulture(culture[0]);
         //insert before the final default provider (the AcceptLanguageHeaderRequestCultureProvider)
         o.RequestCultureProviders.Insert(o.RequestCultureProviders.Count - 1, new OidcClaimsCultureProvider {Options = o});
      });

      app.MapGroup("/authentication").MapLoginAndLogout();

      //Add this, if you need the browser (blazor wasm or javascript) to be able to call CgScript
      //add     <PackageReference Include="Microsoft.Extensions.ServiceDiscovery.Yarp" Version="9.0.0" />
      var site = app.Services.GetService<IOptionsMonitor<OpenIdConnectOptions>>()?.Get(SCHEMENAME).Authority;
      app.MapForwarder("/api/cgscript", site + "api/cgscript", transformBuilder => {
         transformBuilder.AddRequestTransform(async transformContext => {
            var accessToken = await transformContext.HttpContext.GetTokenAsync("access_token");
            transformContext.ProxyRequest.Headers.Authorization = new("Bearer", accessToken);
         });
      }).RequireAuthorization();
   }
}