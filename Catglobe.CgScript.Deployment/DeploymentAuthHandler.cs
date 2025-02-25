namespace Catglobe.CgScript.Deployment;

internal class DeploymentAuthHandler(DeploymentAuthenticator authenticator) : DelegatingHandler
{
   protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
   {
      request.Headers.Authorization = new("Bearer", await authenticator.GetToken(cancellationToken));
      return await base.SendAsync(request, cancellationToken);
   }
}