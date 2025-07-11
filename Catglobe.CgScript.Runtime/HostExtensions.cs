using System.Diagnostics.CodeAnalysis;
using Catglobe.CgScript.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

namespace Catglobe.CgScript.Runtime;

/// <summary>
/// Setup methods
/// </summary>
public static class HostExtensions
{
   /// <summary>
   /// Add CgScript support
   /// </summary>
   public static IServiceCollection AddCgScript(this IServiceCollection services, bool isDevelopment, Action<CgScriptOptions>? configurator = null)
   {
      if (configurator is not null) services.Configure(configurator);
      return AddCommonCgScript(services, isDevelopment);
   }
   /// <summary>
   /// Add CgScript support
   /// </summary>
   public static IServiceCollection AddCgScript(this IServiceCollection services, IConfiguration namedConfigurationSection, bool isDevelopment)
   {
      services.Configure<CgScriptOptions>(namedConfigurationSection);
      return AddCommonCgScript(services, isDevelopment);
   }

   private static IServiceCollection AddCommonCgScript(IServiceCollection services, bool isDevelopment)
   {
      services.AddHttpContextAccessor();
      services.AddHttpClient<IScriptMapping, ScriptMapping>((sp, httpClient) => {
         var site = sp.GetRequiredService<IOptions<CgScriptOptions>>().Value.Site;
         httpClient.BaseAddress = new(site + "api/CgScriptDeployment/");
      });

      services.AddScoped<CgScriptAuthHandler>();
      Action<IServiceProvider, HttpClient> configureClient = (sp, httpClient) => {
         var site = sp.GetRequiredService<IOptions<CgScriptOptions>>().Value.Site;
         httpClient.BaseAddress = new(site + "api/CgScript/");
      };
      (isDevelopment
            ? services.AddHttpClient<ICgScriptApiClient, DevelopmentModeCgScriptApiClient>(configureClient)
            : services.AddHttpClient<ICgScriptApiClient, CgScriptApiClient>(configureClient))
        .AddHttpMessageHandler<CgScriptAuthHandler>();
      return services;
   }
}

