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

var worker = n8n.AddWorker("worker", port: 5679) // resource "n8n-worker"
    .WithPostgresDatabase(db)
    .WithQueueMode(redis);

worker.AddTaskRunner("runner");

builder.Build().Run();
```

## API key

`WithApiKey()` creates an API key with all scopes for the n8n public API once n8n is ready. It signs in as the instance owner, so it needs `WithInstanceOwner`. The key is stored in the AppHost user secrets (`Parameters:{name}-api-key`) and reused on the next start while n8n accepts it; otherwise earlier keys with the same label are deleted and a new one is created.

```csharp
var n8n = builder.AddN8n("n8n")
    .WithInstanceOwner("admin@dev.local", "Admin", "Local")
    .WithApiKey(); // label "Aspire AppHost", or WithApiKey("my-label")

builder.AddProject<Projects.ApiService>("api")
    .WithReference(n8n) // ConnectionStrings__n8n = Endpoint=http://...;ApiKey=...
    .WaitFor(n8n);
```

With an API key the connection string is `Endpoint={url};ApiKey={key}` (connection properties `Uri` and `ApiKey`), read by `AddN8nClient` of `Zemires.Aspire.N8n`. `n8n.Resource.ApiKey` is the key as value (e.g. for `WithEnvironment`), resolved lazily once it exists: consumers wait for it, while the connection string shown in the dashboard has an empty `ApiKey` until then.

`WithApiKey()` works in run mode only and does nothing in publish mode. It uses n8n's internal REST API (`/rest/login`, `/rest/api-keys`), which is undocumented and may change between n8n versions. To use an existing key instead, pass a parameter: `WithApiKey(builder.AddParameter("n8n-api-key", secret: true))`.

## Custom nodes

`WithCustomNodes` bind-mounts a built node package read-only to `/home/node/.n8n/custom/{name}`, where n8n loads it on startup. Pass the resource that builds the package to wait for its completion:

```csharp
var nodes = builder.AddJavaScriptApp("n8n-nodes", "../n8n-nodes-acme", "build");

var n8n = builder.AddN8n("n8n")
    .WithCustomNodes("../n8n-nodes-acme/dist", buildResource: nodes); // mounted as custom/n8n-nodes-acme
```

The folder name is the directory name of the source (its parent for `dist`) unless `name` is given. In queue mode call it on the workers too. n8n reads custom nodes only on startup; restart it after a rebuild.

## More options

Further options: `WithTimeZone`, `WithMetrics`, `WithCommunityPackages`, `WithWebhookUrl` (public URL, `WEBHOOK_URL`), `WithDataVolume`.

Workers and task runners are named `{n8n}-{name}`. All task runners of a resource share one auth token. `WithOtlpExporter()` must be called on each resource (main and workers) whose telemetry should be exported.

Use the `Zemires.Aspire.N8n` client integration to call the n8n API and trigger webhooks from your services.

## Feedback & contributing

https://github.com/zemires/zemires-aspire
