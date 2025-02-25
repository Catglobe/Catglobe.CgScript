using System.Diagnostics.CodeAnalysis;
using Catglobe.CgScript.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Catglobe.CgScript.Deployment;

/// <summary>
/// Setup methods
/// </summary>
public static class HostExtensions
{
   /// <summary>
   /// Add CgScript support.
   /// </summary>
   /// <remarks>
   /// To customize the way scripts are discovered, you can implement your own <see cref="IScriptProvider"/> and register it with the DI container before calling this method.
   /// </remarks>
   public static IServiceCollection AddCgScriptDeployment(this IServiceCollection services, Action<DeploymentOptions>? configurator = null)
   {
      if (configurator is not null) services.Configure(configurator);
      return AddCommonCgScript(services);
   }
   /// <summary>
   /// Add CgScript support
   /// </summary>
   /// <remarks>
   /// To customize the way scripts are discovered, you can implement your own <see cref="IScriptProvider"/> and register it with the DI container before calling this method.
   /// </remarks>
   public static IServiceCollection AddCgScriptDeployment(this IServiceCollection services, IConfiguration namedConfigurationSection)
   {
      services.Configure<DeploymentOptions>(namedConfigurationSection);
      return AddCommonCgScript(services);
   }

   private static IServiceCollection AddCommonCgScript(IServiceCollection services)
   {
      services.TryAddSingleton<IScriptProvider, FilesFromDirectoryScriptProvider>();
      services.AddScoped<DeploymentAuthenticator>();
      services.AddHttpClient<IDeployer, Deployer>((sp, httpClient) => {
                  var site = sp.GetRequiredService<IOptions<DeploymentOptions>>().Value.Authority;
                  httpClient.BaseAddress = new(site + "api/CgScriptDeployment/");
               })
              .AddHttpMessageHandler<DeploymentAuthHandler>();
      services.AddHttpClient<DeploymentAuthenticator>((sp, httpClient) => {
                  httpClient.BaseAddress = sp.GetRequiredService<IOptions<DeploymentOptions>>().Value.Authority;
               });
      return services;
   }
}

