using ZitadelApp.AppHost;
using Projects;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<PostgresServerResource> postgres = builder.AddPostgres("postgres");

IResourceBuilder<PostgresDatabaseResource> database = postgres.AddDatabase("zitadel-db", "zitadel");

var otelCollector = builder.AddOpenTelemetryCollector("otel-collector")
    .WithConfig("./otel-forward.yaml") // uses 443 fo grpc, zitadel has port hardcoded, requires rootfull mode
    .WithEndpoint(targetPort: 443, name: "grpc", scheme: "https")
    .WithContainerRuntimeArgs("--sysctl", "net.ipv4.ip_unprivileged_port_start=443");

var zitadel = builder.AddZitadel("zitadel", port: 8501)
    .WithExternalHttpEndpoints()
    .WithDatabase(database)
    .WithOrganizationName("ASPIRE")
    .WithMachineUser()
    .WithLoginClientKey()
    .WithInitialization(CustomZitadelInitialization.Initialize)
    .WithOtlpExporter(otelCollector.Resource.GetEndpoint("grpc"));

zitadel.AddZitadelLoginClient("zitadel-login", port: 8503)
    .WithExternalHttpEndpoints();

var project = zitadel.AddProject("zitadel-project", "Aspire")
    .WithInitialization(CustomZitadelInitialization.InitializeProject);

var clientId = project.GetOutput(CustomZitadelInitialization.ClientIdOutput);
var clientSecret = project.GetSecretOutput(CustomZitadelInitialization.ClientSecretOutput);

builder.AddProject<ZitadelApp_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(zitadel)
    .WithEnvironment("OpenIDConnectSettings__Authority", zitadel.Resource.PrimaryEndpoint)
    .WithEnvironment("OpenIDConnectSettings__ClientId", clientId)
    .WithEnvironment("OpenIDConnectSettings__ClientSecret", clientSecret)
    .WaitFor(project);

var zitadelApi = zitadel.AddApiConnectionString();

builder.AddProject<ZitadelApp_AdminConsoleApp>("admin-console")
    .WithReference(zitadelApi)
    .WaitFor(zitadel); // the machine user key file is written on the first start of zitadel

await builder.Build().RunAsync();