using Catglobe.CgScript.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Catglobe.CgScript.Runtime;

/// <summary>
/// Setup methods
/// </summary>
public static class HostExtensions
{
   /// <summary>
   /// Add CgScript support
   /// </summary>
   public static IServiceCollection AddCgScript(this IServiceCollection services, bool isDevelopment, string nameOfHttpClient, Action<CgScriptOptions>? configurator = null)
   {
      if (configurator is not null) services.Configure(configurator);
      return AddCommonCgScript(services, isDevelopment, nameOfHttpClient);
   }
   /// <summary>
   /// Add CgScript support
   /// </summary>
   public static IServiceCollection AddCgScript(this IServiceCollection services, IConfiguration namedConfigurationSection, bool isDevelopment, string nameOfHttpClient)
   {
      services.Configure<CgScriptOptions>(namedConfigurationSection);
      return AddCommonCgScript(services, isDevelopment, nameOfHttpClient);
   }

   private static IServiceCollection AddCommonCgScript(IServiceCollection services, bool isDevelopment, string nameOfHttpClient)
   {
      services.AddHttpContextAccessor();
      services.AddHttpClient<IScriptMapping, ScriptMapping>(nameOfHttpClient);

      if (isDevelopment)
         services.AddHttpClient<ICgScriptApiClient, DevelopmentModeCgScriptApiClient>(nameOfHttpClient);
      else
         services.AddHttpClient<ICgScriptApiClient, CgScriptApiClient>(nameOfHttpClient);

#pragma warning disable EXTEXP0001
      (isDevelopment
            ? services.AddHttpClient<ILongRunningCgScriptApiClient, DevelopmentModeCgScriptApiClient>(nameOfHttpClient)
            : services.AddHttpClient<ILongRunningCgScriptApiClient, CgScriptApiClient>(nameOfHttpClient))
        .RemoveAllResilienceHandlers()
#pragma warning restore EXTEXP0001
        .AddStandardResilienceHandler(o => {
            o.AttemptTimeout.Timeout          = TimeSpan.FromMinutes(30);
            o.TotalRequestTimeout.Timeout     = TimeSpan.FromMinutes(90);
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(90);
         });
      return services;
   }
}

