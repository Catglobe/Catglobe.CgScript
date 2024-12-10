# Catglobe.ScriptDeployer
Easily handle development and deployment of sites that needs to run CgScripts on a Catglobe site

This helper library makes it trivial to run and maintain 3 seperate branches of a site:
* Development
* Staging
* Production

# Installation

```
npm install catglobe.cgscript.runtime
npm install catglobe.cgscript.deployment
```

## Runtime setup

Runtime requires the user to log in to the Catglobe site, and then the server will call the CgScript with the user's credentials.

### Catglobe setup

Adjust the following cgscript with the parentResourceId, clientId, clientSecret and name of the client and the requested scopes for your purpose and execute it on your Catglobe site.
```cgscript
string clientSecret = User_generateRandomPassword(64);
OidcAuthenticationFlow client = OidcAuthenticationFlow_createOrUpdate("some id, a guid works, but any string is acceptable");
client.OwnerResourceId = 42; // for this library to work, this MUST be a folder
client.CanKeepSecret = true; // demo is a server app, so we can keep secrets
client.SetClientSecret(clientSecret);
client.AskUserForConsent = false;
client.Layout = "";
client.RedirectUris = {"https://staging.myapp.com/signin-oidc", "https://localhost:7176/signin-oidc"};
client.PostLogoutRedirectUris = {"https://staging.myapp.com/signout-callback-oidc", "https://localhost:7176/signout-callback-oidc"};
client.Scopes = {"email", "profile", "roles", "openid", "offline_access"};
client.OptionalScopes = {};
client.DisplayNames = new LocalizedString({"da-DK": "Min Demo App", "en-US": "My Demo App"}, "en-US");
client.Save();

print(clientSecret);
```

Remember to set it up TWICE using 2 different `parentResourceId`, `clientId`!
Once for the production site (where URIs point to production site) and once for the staging and development (where URIs point to both staging and dev).

### asp.net setup

Add the following to the appsettings.json with the scopes you made above and your Catglobe site url.
```json
"CatglobeOidc": {
  "Authority": "https://mysite.catglobe.com/",
  "ClientId": "Production id",
  "ResponseType": "code",
  "DefaultScopes": [ "email", "offline_access", "roles", "and others from above, except profile and openid " ],
  "SaveTokens": true
},
"CatglobeApi": {
  "FolderResourceId": deploymentFolderId,
  "Site": "https://mysite.catglobe.com/"
}
```

and in appsettings.Staging.json:

```json
"CatglobeOidc": {
  "ClientId": "stagingAndDevelopment id",
},
"CatglobeApi": {
  "FolderResourceId": stagingAndDevelopmentFolderId,
}
```

and in appsettings.Development.json:
```json
"CatglobeOidc": {
  "ClientId": "stagingAndDevelopment id",
},
"CatglobeApi": {
  "FolderResourceId": stagingAndDevelopmentFolderId,
}
```

You do NOT want to commit the `ClientSecret` to your source repository, so you should add it to your user secrets or environment variables.

For example you can execute the following in the project folder to add the secrets to the user secrets for development mode:
```cli
dotnet user-secrets set "CatglobeOidc:ClientSecret" "the client secret"
```

and in production/staging, you can set the secrets as environment variables.

```cli
env DOTNET_CatglobeOidc__ClientSecret "the client secret"
```

In your start procedure, add the following:
```csharp
const string SCHEMENAME = "CatglobeOidc"; //must match the section name in appsettings.json

// Add services to the container.
var services = builder.Services;
services.AddAuthentication(SCHEMENAME)
        .AddOpenIdConnect(SCHEMENAME, oidcOptions => {
            builder.Configuration.GetSection(SCHEMENAME).Bind(oidcOptions);
            oidcOptions.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            oidcOptions.TokenValidationParameters.NameClaimType = "name";
            oidcOptions.TokenValidationParameters.RoleClaimType = "cg_roles";
         })
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);
services.AddCgScript(builder.Configuration.GetSection("CatglobeApi"), builder.Environment.IsDevelopment());
```

Optionally, setup refresh-token refreshing as part of the cookie handling:
```csharp
services.AddSingleton<CookieOidcRefresher>();
services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme).Configure<CookieOidcRefresher>((cookieOptions, refresher) => {
   cookieOptions.Events.OnValidatePrincipal = context => refresher.ValidateOrRefreshCookieAsync(context, SCHEMENAME);
});
```
You can find the CookieOidcRefresher [here](https://github.com/dotnet/blazor-samples/blob/main/9.0/BlazorWebAppOidc/BlazorWebAppOidc/CookieOidcRefresher.cs).

Before `app.Run`, add the following:
```csharp
{
  var group = endpoints.MapGroup("/authentication");

  group.MapGet("/login", (string? returnUrl) => TypedResults.Challenge(GetAuthProperties(returnUrl)))
       .AllowAnonymous();

  // Sign out of the Cookie and OIDC handlers. If you do not sign out with the OIDC handler,
  // the user will automatically be signed back in the next time they visit a page that requires authentication
  // without being able to choose another account.
  group.MapPost("/logout", ([FromForm] string? returnUrl) => TypedResults.SignOut(GetAuthProperties(returnUrl), [CookieAuthenticationDefaults.AuthenticationScheme, SCHEMENAME]));

  static AuthenticationProperties GetAuthProperties(string? returnUrl)
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
  }}
```

## Deployment

Deployment requires the a server side app to log in to the Catglobe site, and then the app will sync the scripts with the Catglobe site.

This app does NOT need to be a asp.net app, it can be a console app. e.g. if you have a db migration pre-deployment app.

### Catglobe setup

Adjust the following cgscript with the impersonationResourceId, parentResourceId, clientId, clientSecret and name of the client for your purpose and execute it on your Catglobe site.
You should not adjust scope for this.
```cgscript
string clientSecret = User_generateRandomPassword(64);
OidcServer2ServerClient client = OidcServer2ServerClient_createOrUpdate("some id, a guid works, but any string is acceptable");
client.OwnerResourceId = 42; // for this library to work, this MUST be a folder
client.SetClientSecret(clientSecret);
client.RunAsUserId = User_getCurrentUser().ResourceId;
client.Scopes = {"scriptdeployment:w"};
client.DisplayNames = new LocalizedString({"da-DK": "Min Demo App", "en-US": "My Demo App"}, "en-US");
client.Save();

print(clientSecret);
```

Remember to set it up TWICE using 2 different `parentResourceId` and `ClientId`! Once for the production site and once for the staging site.

### App setup

Edit deployment environment in your hosting environment for both your staging and production site (remember to use 2 different sets of setup) to include:
```json
env DOTNET_CatglobeDeployment__ClientSecret "the client secret"
env DOTNET_CatglobeDeployment__ClientId "the client id"
env DOTNET_CatglobeDeployment__FolderResourceId "the parentResourceId"
```
and edit your appsettings.json for your deployment project to include this:
```json
"CatglobeDeployment": {
  "Authority": "https://mysite.catglobe.com/",
  "ScriptFolder": "./CgScript"
}
```

You do NOT want to commit the `ClientSecret` to your source repository, so you should add it to your user secrets or environment variables.

In your start procedure, add the following:
```csharp
builder.Services.AddCgScriptDeployment(builder.Configuration.GetSection("CatglobeDeployment"));
```

and when suitable for your app, call the following:
```csharp
if (!app.Environment.IsDevelopment())
   await app.Services.GetRequiredService<IDeployer>().Sync(app.Environment.EnvironmentName, default);
```

# Apps that respondents needs to use

If you have an app that respondents needs to use, you can use the following code to make sure that the user is authenticated via a qas, so they can use the app without additional authentication.

```cgscript
client.CanAuthRespondent = true;
```
```csharp
services.AddAuthentication(SCHEMENAME)
        .AddOpenIdConnect(SCHEMENAME, oidcOptions => {
            builder.Configuration.GetSection(SCHEMENAME).Bind(oidcOptions);
            oidcOptions.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            oidcOptions.TokenValidationParameters.NameClaimType = "name";
            oidcOptions.TokenValidationParameters.RoleClaimType = "cg_roles";

            oidcOptions.Events.OnRedirectToIdentityProvider = context => {
               if (context.Properties.Items.TryGetValue("respondent",        out var resp) &&
                   context.Properties.Items.TryGetValue("respondent_secret", out var secret))
               {
                  context.ProtocolMessage.Parameters["respondent"]        = resp!;
                  context.ProtocolMessage.Parameters["respondent_secret"] = secret!;
               }
               return Task.CompletedTask;
            };
         })
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);
...
   group.MapGet("/login", (string? returnUrl, [FromQuery(Name="respondent")]string? respondent, [FromQuery(Name="respondent_secret")]string? secret) => {
            var authenticationProperties = GetAuthProperties(returnUrl);
            if (!string.IsNullOrEmpty(respondent) && !string.IsNullOrEmpty(secret))
            {
               authenticationProperties.Items["respondent"]        = respondent;
               authenticationProperties.Items["respondent_secret"] = secret;
            }
            return TypedResults.Challenge(authenticationProperties);
         })
        .AllowAnonymous();
```
```cgscript
//in gateway or qas dummy script

gotoUrl("https://siteurl.com/authentication/login?respondent=" + User_getCurrentUser().ResourceGuid + "&respondent_secret=" + qas.AccessCode);");
```

# Usage of the library

## Development

Development takes place on a developers personal device, which means that the developer can run the site locally and test it before deploying it to the staging server.

At this stage the scripts are NOT synced to the server, but are instead dynamically executed on the server.

The authentication model is therefore that the developer logs into the using his own personal account. This account needs to have the questionnaire script dynamic execution access (plus any access required by the script).

All scripts are executed as the developer account and public scripts are not supported without authentication!

If you have any public scripts, it is highly recommended you configure the entire site for authorization in development mode:
```csharp
var razor = app.MapRazorComponents<App>()
    ... removed for abbrivity ...;
if (app.Environment.IsDevelopment())
   razor.RequireAuthorization();
```

## Staging and Deployment

Setup `deployment` and sync your scripts to the Catglobe site.

# FAQ

## File name mapping to security

It is possible to specify which user a script runs under and if the script needs a user to be logged in.

See the documentation for ScriptFromFileOnDisk for details.

## Can I adapt my scripts to do something special in development mode?

Yes, the scripts runs through a limited preprocessor that recognizes `#if DEVELOPMENT` and `#endif` directives.

```cgscript
return #if Development "" #endif #IF production "Hello, World!" #ENDIF #if STAGING "Hi there" #endif;
```

Would return empty string for development, "Hello, World!" for production and "Hi there" for staging.

The preprocessor is case insensitive, supports multiline and supports the standard `Environment.EnvironmentName` values.

## You get a 404 on first deployment?

`parentResourceId`/`FolderResourceId` MUST be a folder.

## I marked my script as public, but get 401 in development mode?

Since all scripts are dynamically generated during development, it also requires running as an account that has permission to run dynamic scripts.

See the example above on how to force the site to always force you to login after restart of site.

## Where do I find the scopes that my site supports?

See supported scopes in your Catglobe site `https://mysite.catglobe.com/.well-known/openid-configuration` under `scopes_supported`.

## Can I use AOT compilation for my C# with this library?

Yes
