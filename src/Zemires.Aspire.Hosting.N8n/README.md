# Zemires.Aspire.Hosting.N8n

An Aspire hosting integration for [n8n](https://n8n.io), a fair-code licensed workflow automation tool that combines AI capabilities with business process automation.

## Install the package

In your AppHost project:

```dotnetcli
dotnet add package Zemires.Aspire.Hosting.N8n
```

## Example usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);
var n8n = builder.AddN8n("n8n");
builder.Build().Run();
```

## Advanced example

Postgres database, queue-mode worker with task runner using Redis, instance owner and license key:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var ownerPassword = builder.AddParameter("owner-password", secret: true);
var licenseKey = builder.AddParameter("license-key", secret: true);

var db = builder.AddPostgres("postgres").WithDataVolume().AddDatabase("n8n-db");
var redis = builder.AddRedis("redis").WithDataVolume();

var n8n = builder.AddN8n("n8n", port: 5678)
    .WithDataBindMount("./.n8n_data")
    .WithPostgresDatabase(db)
    .WithQueueMode(redis)
    .WithInstanceOwner("admin@dev.local", "Admin", "Local", ownerPassword)
    .WithLicenseKey(licenseKey)
    .WithOtlpExporter();

var worker = n8n.AddWorker("worker", port: 5679)
    .WithPostgresDatabase(db)
    .WithQueueMode(redis);

worker.AddTaskRunner("runner");

builder.Build().Run();
```

Further options: `WithTimeZone`, `WithMetrics`, `WithCommunityPackages`, `WithWebhookUrl`, `WithDataVolume`.

Use the `Zemires.Aspire.N8n` client integration to call the n8n API from your services.

## Feedback & contributing

https://github.com/zemires/zemires-aspire
