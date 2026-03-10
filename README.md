# Editor Support

The CgScript editor extensions bring a full editing experience to `.cgs` files in both Visual Studio and Visual Studio Code.

Download the latest `.vsix` files from the [GitHub Releases](https://github.com/Catglobe/Catglobe.CgScript/releases) page.

## Features

### Syntax coloring
Keywords, strings, numbers, comments, function names, and variable declarations are each colored differently so the structure of your script is immediately obvious.

### Error and warning squiggles
Problems are underlined as you type — red for errors, yellow for warnings. Hover over the underline to see the message, or open the Problems / Error List panel to see all issues at once. Examples of things that are caught:
- Syntax errors (missing brackets, unexpected tokens, …)
- Using a variable before it is declared, or declaring the same variable twice
- Calling a function or using a type that does not exist
- Leaving a `type=""` attribute empty in an XML doc comment

### Code completion
Press `Ctrl+Space` (or just start typing) to get a list of suggestions. The extension suggests:
- Variables and functions that are in scope
- Built-in functions and type names
- Members of an object after you type `.`

### Hover documentation
Hover the mouse over any built-in function or type to read its documentation without leaving the editor.

### Parameter hints
When you open the parentheses of a function call — or type a comma between arguments — a tooltip shows the expected parameter names and types.

### Jump to definition
Press `F12` (or right-click → Go to Definition) on any variable or function to jump straight to where it was declared.

### Find all references
Right-click a variable or function and choose Find All References to see every place it is used in the file. Results in XML doc comment `name=` attributes are included alongside code references.

### Rename
Press `F2` on any variable or function to rename it everywhere it appears — including occurrences in XML doc comment `name=` attributes.

### Highlight occurrences
Place the cursor on a name and every other use of that name in the file is highlighted, including matching `name=` attributes in doc comments.

### Document outline
Open the Outline panel (VS Code) or the document drop-down (Visual Studio) to see a structured list of all functions and top-level variables in the file and jump to any of them quickly.

### Code folding
Blocks of code — `if`, `while`, `for`, `function`, etc. — can be collapsed and expanded in the editor gutter.

### XML doc comment generation
Type `///` on the line immediately above a `function(…)` declaration and press `Tab` (or accept the completion) to automatically insert a documentation template:

```
/// <summary></summary>
/// <param name="paramName"></param>
/// <returns></returns>
```

Parameters that use generic types (`array`, `object`, `question`, `number`) also get a `type=""` attribute to fill in. If a doc comment already exists for the function, the template is not offered again.

## Visual Studio Code

1. Open VS Code
2. Open the Extensions view (`Ctrl+Shift+X`)
3. Click the **`…`** menu (top-right of the Extensions panel) → **Install from VSIX…**
4. Select the downloaded `cgscript-vscode-*.vsix` file

Or via the command line:
```
code --install-extension cgscript-vscode-x.y.z.vsix
```

## Visual Studio

1. Close all Visual Studio instances
2. Double-click the downloaded `Catglobe.CgScript.EditorSupport.VisualStudio-*.vsix`
3. Follow the installer prompts and reopen Visual Studio

Or via the command line:
```
vsixinstaller.exe Catglobe.CgScript.EditorSupport.VisualStudio-x.y.z.vsix
```

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
  "Scope": [ "email", "offline_access", "roles", "and others from above, except profile and openid " ],
  "SaveTokens": true
},
"CatglobeApi": {
  "FolderResourceId": deploymentFolderId
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
         })
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);
services.AddHttpClient("AuthClient", (sp, httpClient) => {
                  var site = sp.GetService<IOptionsMonitor<OpenIdConnectOptions>>()?.Get(SCHEMENAME).Authority;
                  httpClient.BaseAddress = string.IsNullOrEmpty(site) ? null : new(site);
                  httpClient.DefaultRequestHeaders.Accept.Clear();
                  httpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
               })
              .AddUserAccessTokenHandler(); //<-- here we use Duande for token management
services
   .AddBlazorServerAccessTokenManagement<HybridCacheTokenStore>()
   .AddOpenIdConnectAccessTokenManagement();

services.AddCgScript(builder.Configuration.GetSection("CatglobeApi"), builder.Environment.IsDevelopment(), "AuthClient");
```

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

## I18n and email

Users language, culture, timezone and email is stored in Catglobe. To use in your app, add the following:

```csharp
.AddOpenIdConnect(SCHEMENAME, oidcOptions => {
   ...
   // to get the locale/culture
   oidcOptions.ClaimActions.MapUniqueJsonKey("locale",  "locale");
   oidcOptions.ClaimActions.MapUniqueJsonKey("culture", "culture");

  // must be true to get the zoneinfo and email claims
  oidcOptions.GetClaimsFromUserInfoEndpoint = true;
  oidcOptions.ClaimActions.MapUniqueJsonKey("zoneinfo", "zoneinfo");
})
```

If you use Blazor WASM, you need to also send these claims to the WASM and parse them:

```csharp
//in SERVER program.cs:
...
    .AddAuthenticationStateSerialization(o=>o.SerializeAllClaims=true);
```

```csharp
builder.Services.AddAuthenticationStateDeserialization(o=>o.DeserializationCallback = ProcessLanguageAndCultureFromClaims(o.DeserializationCallback));

static Func<AuthenticationStateData?, Task<AuthenticationState>> ProcessLanguageAndCultureFromClaims(Func<AuthenticationStateData?, Task<AuthenticationState>> authenticationStateData) =>
   state => {
      var tsk = authenticationStateData(state);
      if (!tsk.IsCompletedSuccessfully) return tsk;
      var authState = tsk.Result;
      if (authState?.User is not { } user) return tsk;
      var userCulture   = user.FindFirst("culture")?.Value;
      var userUiCulture = user.FindFirst("locale")?.Value ?? userCulture;
      if (userUiCulture == null) return tsk;

      CultureInfo.DefaultThreadCurrentCulture   = new(userCulture ?? userUiCulture);
      CultureInfo.DefaultThreadCurrentUICulture = new(userUiCulture);
      return tsk;
   };

```

You can adapt something like https://www.meziantou.net/convert-datetime-to-user-s-time-zone-with-server-side-blazor-time-provider.htm for timezone

## Role based authorization in your app

If you want to use roles in your app, you need to request roles from oidc:
```json
"CatglobeOidc": {
...
  "Scope": [ ... "roles",  ],
},
```

---

# Migrating hand-coded `Execute` calls to the source generator

Version 2.3.0 ships a Roslyn source generator (`Catglobe.CgScript.EditorSupport.SourceGenerator`) that reads your `.cgs` files at compile time and emits a strongly-typed extension method for each script. This eliminates hand-written request records, manual `[JsonSerializable]` registrations for parameter types, and the repetitive `Execute(path, new(…), callType, returnType)` pattern.

## What gets generated

For every `.cgs` file the generator can parse, it emits a `public static partial class CgScriptExtensions` inside a namespace derived from your assembly name and the script's folder path:

| Script path (relative to project) | Generated namespace | Generated method |
|------------------------------------|---------------------|-----------------|
| `CgScript/Company/GetCompanyId.cgs` | `YourApp.CgScript.Company` | `GetCompanyId(…)` |
| `CgScript/Payment/CreateOrder.cgs` | `YourApp.CgScript.Payment` | `CreateOrder(…)` |
| `CgScript/User/DetermineRoles.cgs`  | `YourApp.CgScript.User`    | `DetermineRoles(…)` |

The method is an extension on `ICgScriptApiClient` and returns `Task<ScriptResult<TReturn>>`, so existing `.GetValueOrThrowError()` calls require no change.

## Step 1 — Upgrade packages

In your `.csproj`, upgrade to **2.3.0** and add the source generator as an analyzer-only reference (it is compile-time only; no runtime dependency):

```xml
<PackageReference Include="Catglobe.CgScript.Deployment" Version="2.3.0" />
<PackageReference Include="Catglobe.CgScript.Runtime" Version="2.3.0" />
<PackageReference Include="Catglobe.CgScript.EditorSupport.SourceGenerator" Version="2.3.0">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>analyzers</IncludeAssets>
</PackageReference>
```

## Step 2 — Expose `.cgs` files to the generator

The generator reads scripts via MSBuild `AdditionalFiles`. Keep your existing `<None>` entry (it controls deployment); add a parallel `<AdditionalFiles>` entry:

```xml
<ItemGroup>
  <AdditionalFiles Include="CgScript\**\*.cgs" />
  <None Include="CgScript\**\*.cgs" CopyToPublishDirectory="PreserveNewest" />
</ItemGroup>
```

## Step 3 — Mark your JSON serializer context

Add `[CgScriptSerializer]` to your `JsonSerializerContext` subclass. Exactly one class per assembly may carry this attribute. The generator uses it to resolve return-type `JsonTypeInfo` properties.

```csharp
using Catglobe.CgScript;

[CgScriptSerializer]
[JsonSerializable(typeof(MyReturnType))]
// … one entry per non-primitive return type …
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
internal partial class CgScriptSerializer : JsonSerializerContext;
```

**You no longer need `[JsonSerializable]` entries for parameter/request types** — the generator emits inline serialization for those.

## Step 4 — Annotate `.cgs` scripts

The generator needs to know the C# types of parameters and return values. CgScript's `number`, `array`, `object`, and `question` are ambiguous; `string` and `bool` are unambiguous and need no annotation. Add XML doc comments at the very top of each script:

```cgscript
/// <summary>Finds a company by name and returns its resource ID.</summary>
/// <param name="companyFolderId" type="int">Root folder ID.</param>
/// <returns type="int">Company resource ID.</returns>
function(number companyFolderId, string companyName) {
    …
}.Invoke(Workflow_getParameters()[0]);
```

### Type annotation reference

| CgScript type | Annotation needed? | Example annotation |
|---------------|-------------------|--------------------|
| `string`      | No                | —                  |
| `bool`        | No                | —                  |
| `number`      | **Yes**           | `type="int"`, `type="double"`, `type="Guid"` |
| `array`       | **Yes**           | `type="IReadOnlyCollection<MyType>"`, `type="Guid[]"` |
| `object` / `Dictionary` | **Yes** | `type="MyRecord"`, `type="object"` |
| `question`    | **Yes**           | `type="MyType"` |
| void (no `return`) | No          | omit `<returns>` entirely |

The three supported parameter-passing patterns are all detected automatically:

- **Pattern A** — `function(type name, …) { }.Invoke(Workflow_getParameters()[0])`
- **Pattern B** — `params[0]["key"]` dict reads
- **Pattern C** — `var dict = Workflow_getParameters()[0]; type name = dict["key"]`

### Build diagnostics

If an annotation is missing or wrong the build emits a diagnostic rather than silently generating bad code:

| Code   | Meaning |
|--------|---------|
| CGS010 | No `[CgScriptSerializer]` class found (or more than one) |
| CGS011 | Return or parameter type not registered with `[JsonSerializable]` |
| CGS012 | Ambiguous parameter type — annotation required |
| CGS013 | Invalid type syntax in annotation |

## Step 5 — Replace `Execute(…)` calls

For each storage/service method, replace the three-line boilerplate with a single generated call:

```csharp
// Before
var callType   = CgScriptSerializer.Default.GetCompanyIdRequest;
var returnType = CgScriptSerializer.Default.Int32;
var result     = await cgScriptClient.Execute(
    "Company/GetCompanyId", new(resources.Value.CompanyFolderId, companyName),
    callType, returnType);
return result.GetValueOrThrowError();

// After  (add `using YourApp.CgScript.Company;` at the top of the file)
var result = await cgScriptClient.GetCompanyId(resources.Value.CompanyFolderId, companyName);
return result.GetValueOrThrowError();
```

The generated method takes the same individual parameters the script declares, in declaration order. Any parameter that was previously bundled into a request record is now passed directly.

### Adding `using` directives

The generated class lives in `{AssemblyName}.{ScriptFolder}` (dots instead of slashes). Each storage class needs a `using` for the folders it calls:

```csharp
using YourApp.CgScript.Company;   // in CompanyStorage.cs
using YourApp.CgScript.Payment;   // in PaymentStorage.cs
using YourApp.CgScript.Project;   // in ProjectStorage.cs
using YourApp.CgScript.Report;    // in ReportStorage.cs
using YourApp.CgScript.User;      // in DetermineRoles.cs, CompanyContext.cs
using YourApp.CgScript.MediaApi;  // in MediaApi.cs
```

## Step 6 — Clean up what's no longer needed

Once all `Execute(…)` calls are replaced:

- **Delete internal request records** that were only used as the `new(…)` argument — e.g. `CompanyRequest`, `VoucherValidationRequest`, `PaymentStatusRequest`, etc. Any record used as a *return type* or by UI code outside the CgScript folder must be kept.
- **Remove `[JsonSerializable]` entries** for those same input/request types from the serializer context.
- Do **not** remove `[JsonSerializable]` for return types — the generator still needs them to resolve the `JsonTypeInfo` property for deserialization.

## Scripts the generator skips

The generator only produces a wrapper when it can detect a recognised workflow parameter pattern. Scripts that are called only from other CgScripts (automation helpers, internal subroutines) and scripts with no parameters at all will not get wrappers — this is expected and harmless. Leave those scripts unannotated; they are ignored without a build error.

Next, you need to make a script that detect the users roles:

```cgscript
array scopesRequested = Workflow_getParameters()[0]["scopes"];
...do some magic to figure out the roles...
return {"thisUserIsAdmin"};
```

You can make this script public.

```cgscript
OidcAuthenticationFlow client = OidcAuthenticationFlow_createOrUpdate("some id, a guid works, but any string is acceptable");
client.AppRolesScriptId = 424242; // the script that returns the roles
...
```

and finally in any page, you can add either `@attribute [Authorize(Roles = "thisUserIsAdmin")]` or `<AuthorizeView Roles="thisUserIsAdmin">Only visible to admins<AuthorizeView>`.

Why can the script NOT be in the app? Because it needs to run __before__ the app is ever deployed.

**NOTICE!** We may change the way to setup the script in the future to avoid the bootstrapping issue.

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

### Impersonation during development

If you want to seperate the development and production/staging accounts, you can use the following code to map impersonation to a different user during development:
```json
"CatglobeApi": {
  "ImpersonationMapping": {
    "115": 0
  }
}
```

`0` means it is mapped to the developer account.

The recommended setup is that the developer has full access to the staging data and that all mapping therefore is set to 0.

In production, the impersonation accounts there can then be set up to have the correct access to users and data.

## Staging and Deployment

Setup `deployment` and sync your scripts to the Catglobe site.

# Telemetry and OpenTelemetry integration

To enable distributed tracing and telemetry for CgScript operations, you can use the provided `AddCgScriptInstrumentation` extension method. This will register the CgScript telemetry source with OpenTelemetry, allowing you to collect traces for script execution and deployment flows.

### Example: Setting up OpenTelemetry with CgScript
```
using Catglobe.CgScript.Common;
using Catglobe.CgScript.Deployment;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry().WithTracing(tracerProviderBuilder =>
{
    tracerProviderBuilder
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddCgScriptInstrumentation() // Registers CgScript telemetry source
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("MyApp"));
});
```
You can now view CgScript-related activities in your OpenTelemetry-compatible backend (such as Jaeger, Zipkin, or Azure Monitor).

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

## Can I make a request during authentication?

Yes, e.g. in `OnTicketReceived`, you can set the `httpContext.Items["access_token"]` and that will be used to make the next request.
