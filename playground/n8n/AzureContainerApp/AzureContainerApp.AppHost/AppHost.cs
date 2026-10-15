using Aspire.Hosting.Azure;

var builder = DistributedApplication.CreateBuilder(args);

var apiKey = builder.AddParameter("foundry-api-key"); // needs to be set manually

var law = builder.AddAzureLogAnalyticsWorkspace("law");
var appi = builder.AddAzureApplicationInsights("appi", law);

var env = builder.AddAzureContainerAppEnvironment("env")
    .WithAzureLogAnalyticsWorkspace(law);

var postgres = builder.AddPostgres("postgres") 
    .WithImage("k8se/services/postgres", "14") // ACA optimized, don't use in production
    .WithImageRegistry("mcr.microsoft.com")
    .PublishAsAzureContainerApp((infra, app) =>
    {
        app.Template.Scale.MaxReplicas = 1;
    });

var foundry = builder.AddAzureAIFoundry("foundry");
var chat = foundry.AddDeployment("chat", AIFoundryModel.OpenAI.Gpt5Mini);

var n8n = builder.AddN8n("n8n", port: 55678)
    .WithDataVolume()
    .WithInstanceOwner("admin@dev.local", "Admin", "Local")
    .WithOtlpExporter()
    .WithExternalHttpEndpoints()
    .WithPostgresDatabase(postgres)
    .WithReference(chat)
    .WaitFor(chat)                                      // issue: {foundry.Resource.NameOutputReference} contains a dash 
    .WithEnvironment("CREDENTIALS_OVERWRITE_DATA", $"{{\"azureOpenAiApi\": {{\"endpoint\":\"https://{foundry.Resource.NameOutputReference}.openai.azure.com\", \"apiKey\":\"{apiKey}\", \"resourceName\":\"{foundry.Resource.NameOutputReference}\", \"apiVersion\":\"2025-03-01-preview\" }} }}")
    .WithEnvironment("CREDENTIALS_OVERWRITE_PERSISTENCE", "true")
    .PublishAsAzureContainerApp((infra, app) =>
    {
        app.Template.Scale.MaxReplicas = 1;
    });


if (builder.ExecutionContext.IsPublishMode)
{
    n8n.WithEnvironment("N8N_OTEL_EXPORTER_OTLP_ENDPOINT", "http://k8se-otel.k8se-apps.svc.cluster.local:4317"); // for ACA set to cluster internal endpoint of the otel collector
    n8n.WithEnvironment("N8N_OTEL_ENABLED", "true");
    n8n.WithEnvironment("N8N_OTEL_EXPORTER_OTLP_PROTOCOL", "grpc");
}


builder.Build().Run();