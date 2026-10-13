using Aspire.Hosting.Azure;
using Azure.Provisioning.RedisEnterprise;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var tenantId = builder.Configuration["Azure:TenantId"].ToString();

var clientSecret = builder.AddParameter("client-secret", true); // password from: az ad app credential reset --id $APP_ID --display-name "client-app-secret"
var n8nExternalDomain = builder.AddParameter("n8n-external-domain");

var law = builder.AddAzureLogAnalyticsWorkspace("law");
var appi = builder.AddAzureApplicationInsights("appi", law);

var env = builder.AddAzureContainerAppEnvironment("env")
    .WithAzureLogAnalyticsWorkspace(law);

var postgres = builder.AddAzurePostgresFlexibleServer("postgres")
    .WithPasswordAuthentication();
var db = postgres.AddDatabase("n8n-db", "n8n");

var foundry = builder.AddAzureAIFoundry("foundry");
var chat = foundry.AddDeployment("chat", AIFoundryModel.OpenAI.Gpt5Mini);
chat.Resource.SkuCapacity = 1000;

var redis = builder.AddAzureManagedRedis("redis")
    .WithAccessKeyAuthentication()
    .ConfigureInfrastructure((infra) =>
    {
        var redisEnterprise = infra.GetProvisionableResources().OfType<RedisEnterpriseCluster>().FirstOrDefault();
        var redisDb = infra.GetProvisionableResources().OfType<RedisEnterpriseDatabase>().FirstOrDefault();

        var redisEnterpriseDatabase = infra.GetProvisionableResources()
                    .OfType<RedisEnterpriseDatabase>()
                    .SingleOrDefault(db => db.BicepIdentifier == redisEnterprise.BicepIdentifier + "_default");

        if (redisEnterpriseDatabase is null)
        {
            redisEnterpriseDatabase = RedisEnterpriseDatabase.FromExisting(redisEnterprise.BicepIdentifier + "_default");
            redisEnterpriseDatabase.Name = "default";
            redisEnterpriseDatabase.Parent = redisEnterprise;
            infra.Add(redisEnterpriseDatabase);
        }

        redisEnterpriseDatabase.ClusteringPolicy = RedisEnterpriseClusteringPolicy.NoCluster; // n8n cannot handle cluster
        redisEnterpriseDatabase.EvictionPolicy = RedisEnterpriseEvictionPolicy.NoEviction;
    });

// //'AzureADMyOrg'
var appReg = builder.AddBicepTemplateString("appreg", @"
extension microsoftGraphV1
param location string
var msGraphAppId = '00000003-0000-0000-c000-000000000000'
var clientAppScopes array = ['offline_access', 'openid', 'profile', 'email']
resource msGraphSP 'Microsoft.Graph/servicePrincipals@v1.0' existing = {
  appId: msGraphAppId
}
var graphScopes = msGraphSP.oauth2PermissionScopes
resource clientApp 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'n8n-az-app'
  displayName: 'n8n Azure App'
  signInAudience: 'AzureADMultipleOrgs'
  web: {
    redirectUris: [
      'https://localhost:5678/auth/oidc/callback'
      '"+ n8nExternalDomain.Resource.Value + @"/auth/oidc/callback'
      '"+ n8nExternalDomain.Resource.Value + @"/rest/oauth2-credential/callback'
    ]
    implicitGrantSettings: { enableIdTokenIssuance: true }
  }
  requiredResourceAccess: [
    {
      resourceAppId: msGraphAppId
      resourceAccess: [
        for (scope, i) in clientAppScopes: {
          id: filter(graphScopes, graphScopes => graphScopes.value == scope)[0].id
          type: 'Scope'
        }
      ]
    }
  ]
    optionalClaims: {
    idToken: [
    {
        name: 'email'
        essential: false
        source: null
    }
    ]
  }
}
output clientAppId string = clientApp.appId
");

//var collector = builder.AddOpenTelemetryCollector("otel-collector")
//    .WithOtlpExporter()
//    .WithEnvironment((ctx) =>
//    {
//        var otlpApiKey = builder.Configuration["AppHost:OtlpApiKey"];
//        if (!string.IsNullOrEmpty(otlpApiKey))
//        {
//            ctx.EnvironmentVariables["OTEL_EXPORTER_OTLP_API_KEY"] = otlpApiKey;
//        }
//    })
//    .WithConfig("config.yaml")
//    .WithReference(appi);

var api = builder.AddProject<AzureN8n_ApiService>("api")
    .WithReference(appi);

//var credentialsOverwrite = ReferenceExpression.Create($"{{\"azureOpenAiApi\": {{\"endpoint\":\"https://{foundry.Resource.NameOutputReference}.openai.azure.com\", \"apiKey\":\"{apiKey}\", \"resourceName\":\"{foundry.Resource.NameOutputReference}\", \"apiVersion\":\"2025-03-01-preview\" }}, \"azureEntraCognitiveServicesOAuth2Api\": {{ \"endpoint\":\"{foundry.Resource.Endpoint}\", \"clientId\":\"{appReg.GetOutput("clientAppId")}\", \"clientSecret\":\"{clientSecret}\", \"resourceName\":\"{foundry.Resource.NameOutputReference}\", \"tenantId\":\"{tenantId}\", \"apiVersion\":\"2025-03-01-preview\" }} }}");
var credentialsOverwrite = ReferenceExpression.Create($"{{ \"azureEntraCognitiveServicesOAuth2Api\": {{ \"clientId\":\"{appReg.GetOutput("clientAppId")}\", \"clientSecret\":\"{clientSecret}\", \"tenantId\":\"{tenantId}\" }} }}");

var n8n = builder.AddN8n("n8n", port: 5567)
    .WithDataVolume()
    .WithInstanceOwner("admin@dev.local", "Admin", "Local")
    .WithTimeZone("Europe/Zurich")
    //.WithOtlpExporter()
    ////.WithOpenTelemetryCollectorRouting(collector); // has error
    //.WithEnvironment(callback =>
    //{
    //    var otlpProtocol = callback.EnvironmentVariables.GetValueOrDefault("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc");
    //    var endpoint = collector.Resource.GetEndpoint(otlpProtocol.ToString() ?? "grpc");
    //    if (!callback.EnvironmentVariables.TryAdd("OTEL_EXPORTER_OTLP_ENDPOINT", endpoint))
    //    {
    //        callback.EnvironmentVariables["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint;
    //    }
    //})
    .WithExternalHttpEndpoints()
    .WithPostgresDatabase(db, true)
    .WithQueueMode(redis)
    .WithReference(chat)
    .WaitFor(chat)                                      // issue: {foundry.Resource.NameOutputReference} contains a dash 
    .WithEnvironment("CREDENTIALS_OVERWRITE_DATA", credentialsOverwrite)
    //.WithEnvironment("CREDENTIALS_OVERWRITE_PERSISTENCE", "true") //  only overwrite endpoint is used for data persistence
    .WithCommunityPackages() // e.g. "n8n-nodes-openapi-node@1.0.9"
    .WithReference(api);

// with enterprise license
//n8n.WithEnvironment("N8N_SSO_MANAGED_BY_ENV", "true")
//    .WithEnvironment("N8N_SSO_OIDC_LOGIN_ENABLED", "true")
//    .WithEnvironment("N8N_SSO_OIDC_CLIENT_ID", entraIdApp.GetOutput("clientAppId"))
//    .WithEnvironment("N8N_SSO_OIDC_CLIENT_SECRET", clientSecret)
//    .WithEnvironment("N8N_SSO_OIDC_DISCOVERY_ENDPOINT", $"https://login.microsoftonline.com/{tenantId}/v2.0/.well-known/openid-configuration")
//    .WaitFor(entraIdApp);

// workaround using a hook implementation by Cameron Eagans, https://github.com/cweagans/n8n-oidc
if (builder.ExecutionContext.IsRunMode)
{
    n8n.WithBindMount("./hooks.js", "/home/node/.n8n/hooks.js"); // for publish: copy manually
}
n8n.WithEnvironment("EXTERNAL_HOOK_FILES", "/home/node/.n8n/hooks.js")
    .WithEnvironment("OIDC_ISSUER_URL", $"https://login.microsoftonline.com/{tenantId}/v2.0")
    .WithEnvironment("OIDC_CLIENT_ID", appReg.GetOutput("clientAppId"))
    .WithEnvironment("OIDC_CLIENT_SECRET", clientSecret)
    .WithEnvironment("OIDC_REDIRECT_URI", $"{n8n.GetEndpoint("http", KnownNetworkIdentifiers.PublicInternet)}/auth/oidc/callback")
    .WithEnvironment("N8N_ADDITIONAL_NON_UI_ROUTES", "auth")
    .WithEnvironment("EXTERNAL_FRONTEND_HOOKS_URLS", "/assets/oidc-frontend-hook.js")
    .WaitFor(appReg);

var worker = n8n.AddWorker("worker", port: 5568)
    .WithPostgresDatabase(db, true)
    .WithQueueMode(redis)
    .WithTimeZone("Europe/Zurich")
    .WithCommunityPackages()
    .WithEnvironment("CREDENTIALS_OVERWRITE_DATA", credentialsOverwrite) // needs to be set for worker as well, only endpoint is used for data persistence
    .WithReference(api);
    //.WithOtlpExporter()
    ////.WithOpenTelemetryCollectorRouting(collector); // has error
    //.WithEnvironment(callback =>
    //{
    //    var otlpProtocol = callback.EnvironmentVariables.GetValueOrDefault("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc");
    //    var endpoint = collector.Resource.GetEndpoint(otlpProtocol.ToString() ?? "grpc");
    //    if (!callback.EnvironmentVariables.TryAdd("OTEL_EXPORTER_OTLP_ENDPOINT", endpoint))
    //    {
    //        callback.EnvironmentVariables["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint;
    //    }
    //});

var taskRunner = worker.AddTaskRunner("runner")
    .PublishAsAzureContainerAppSidecar(worker, (sidecar) =>
    {
        // adjust uri to use localhost
        var env = sidecar.Env.FirstOrDefault(p => p.Value.Name.Value == "N8N_RUNNERS_TASK_BROKER_URI");
        env.Value.Value = "http://localhost:5679";
    });

if (builder.ExecutionContext.IsRunMode)
{
    postgres.RunAsContainer();
    redis.RunAsContainer();
}

builder.Build().Run();
