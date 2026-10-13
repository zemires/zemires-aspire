using Aspire.Hosting.Azure;

var builder = DistributedApplication.CreateBuilder(args);

var apiKey = builder.AddParameter("foundry-api-key"); // needs to be set manually

var law = builder.AddAzureLogAnalyticsWorkspace("law");
var appi = builder.AddAzureApplicationInsights("appi", law);

var env = builder.AddAzureContainerAppEnvironment("env")
    .WithAzureLogAnalyticsWorkspace(law);

var collector = builder.AddOpenTelemetryCollector("otel-collector")
    .WithOtlpExporter()
    .WithEnvironment((ctx) =>
    {
        var otlpApiKey = builder.Configuration["AppHost:OtlpApiKey"];
        if (!string.IsNullOrEmpty(otlpApiKey))
        {
            ctx.EnvironmentVariables["OTEL_EXPORTER_OTLP_API_KEY"] = otlpApiKey;
        }
    })
    .WithConfig("config.yaml")
    .WithReference(appi);

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
    .WithOtlpExporter(OtlpProtocol.HttpProtobuf)
    .WithExternalHttpEndpoints()
    .WithPostgresDatabase(postgres)
    .WithEnvironment(ctx =>
    {
        // Extension Method .WithOpenTelemetryCollectorRouting(collector) cannot be used as it throws error on azure prepare step.
        var endpoint = collector.Resource.GetEndpoint("http");
        if (!ctx.EnvironmentVariables.TryAdd("OTEL_EXPORTER_OTLP_ENDPOINT", endpoint))
        {
            ctx.EnvironmentVariables["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint;
        }

        ctx.EnvironmentVariables["N8N_OTEL_ENABLED"] = "true";
        if (ctx.EnvironmentVariables.TryGetValue("OTEL_EXPORTER_OTLP_ENDPOINT", out var otlpEndpoint))
            ctx.EnvironmentVariables["N8N_OTEL_EXPORTER_OTLP_ENDPOINT"] = otlpEndpoint;

        if (ctx.EnvironmentVariables.TryGetValue("OTEL_EXPORTER_OTLP_HEADERS", out var otlpHeaders))
            ctx.EnvironmentVariables["N8N_OTEL_EXPORTER_OTLP_HEADERS"] = otlpHeaders;

        if (ctx.EnvironmentVariables.TryGetValue("OTEL_SERVICE_NAME", out var otlpServiceName))
            ctx.EnvironmentVariables["N8N_OTEL_EXPORTER_SERVICE_NAME"] = otlpServiceName;
    })
    .WaitFor(collector)
    .WithReference(chat)
    .WaitFor(chat)                                      // issue: {foundry.Resource.NameOutputReference} contains a dash 
    .WithEnvironment("CREDENTIALS_OVERWRITE_DATA", $"{{\"azureOpenAiApi\": {{\"endpoint\":\"https://{foundry.Resource.NameOutputReference}.openai.azure.com\", \"apiKey\":\"{apiKey}\", \"resourceName\":\"{foundry.Resource.NameOutputReference}\", \"apiVersion\":\"2025-03-01-preview\" }} }}")
    .WithEnvironment("CREDENTIALS_OVERWRITE_PERSISTENCE", "true")
    .PublishAsAzureContainerApp((infra, app) =>
    {
        app.Template.Scale.MaxReplicas = 1;
    });

builder.Build().Run();