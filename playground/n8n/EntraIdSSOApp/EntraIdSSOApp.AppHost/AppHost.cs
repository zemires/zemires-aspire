var builder = DistributedApplication.CreateBuilder(args);

var clientSecret = builder.AddParameter("client-secret", true); // password from: az ad app credential reset --id $APP_ID --display-name "client-app-secret"

var entraIdApp = builder.AddBicepTemplate("entraid-app", "appregistration.bicep")
    .WithParameter("cloudEnvironment", "AzureCloud")
    .WithParameter("clientAppName", "n8n-test-app")
    .WithParameter("clientAppDisplayName", "n8n SSO Test Application")
    .WithParameter("webAppEndpoint", "https://wedontknowyet.com");

var n8n = builder.AddN8n("n8n", port: 5678)
    .WithDataBindMount("./.n8n_data")
    .WithOtlpExporter()
    .WithInstanceOwner("admin@dev.local", "Admin", "Dev")
    .WithLicenseKey();

// with enterprise license
//n8n.WithEnvironment("N8N_SSO_MANAGED_BY_ENV", "true")
//    .WithEnvironment("N8N_SSO_OIDC_LOGIN_ENABLED", "true")
//    .WithEnvironment("N8N_SSO_OIDC_CLIENT_ID", entraIdApp.GetOutput("clientAppId"))
//    .WithEnvironment("N8N_SSO_OIDC_CLIENT_SECRET", clientSecret)
//    .WithEnvironment("N8N_SSO_OIDC_DISCOVERY_ENDPOINT", "https://login.microsoftonline.com/02f3babb-3706-4bcc-b909-60bad09450bd/v2.0/.well-known/openid-configuration")
//    .WaitFor(entraIdApp);

// workaround using a hook implementation by Cameron Eagans, https://github.com/cweagans/n8n-oidc
n8n.WithBindMount("./hooks.js", "/home/node/.n8n/hooks.js")
    .WithEnvironment("EXTERNAL_HOOK_FILES", "/home/node/.n8n/hooks.js")
    .WithEnvironment("OIDC_ISSUER_URL", "https://login.microsoftonline.com/02f3babb-3706-4bcc-b909-60bad09450bd/v2.0")
    .WithEnvironment("OIDC_CLIENT_ID", entraIdApp.GetOutput("clientAppId"))
    .WithEnvironment("OIDC_CLIENT_SECRET", clientSecret)
    .WithEnvironment("OIDC_REDIRECT_URI", $"{n8n.GetEndpoint("http", KnownNetworkIdentifiers.LocalhostNetwork)}/auth/oidc/callback")
    .WithEnvironment("N8N_ADDITIONAL_NON_UI_ROUTES", "auth")
    .WithEnvironment("EXTERNAL_FRONTEND_HOOKS_URLS", "/assets/oidc-frontend-hook.js")
    .WaitFor(entraIdApp);

builder.Build().Run();
