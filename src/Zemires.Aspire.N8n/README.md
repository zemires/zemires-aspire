# Zemires.Aspire.N8n

An Aspire client integration that registers an `N8nClient` (n8n public API, from `Zemires.N8n.Api`), an `N8nWebhookClient` (starts workflows through their Webhook trigger) and a health check in the DI container.

## Install the package

```dotnetcli
dotnet add package Zemires.Aspire.N8n
```

## Usage

In the AppHost, reference the n8n resource (see `Zemires.Aspire.Hosting.N8n`):

```csharp
builder.AddProject<Projects.ApiService>("api").WithReference(n8n);
```

In the consuming service:

```csharp
builder.AddN8nClient("n8n");
// or keyed: builder.AddKeyedN8nClient("n8n");
```

Then inject `N8nClient`:

```csharp
app.MapGet("/workflows", async (N8nClient client) => await client.Workflows.GetAsync());
```

## Webhooks

The public API cannot run workflows. `N8nWebhookClient` posts to the Webhook trigger of a published workflow (`/webhook/{path}`, or `/webhook-test/{path}` with `test: true`) on the configured endpoint:

```csharp
app.MapPost("/orders", async (Order order, N8nWebhookClient webhooks) =>
    await webhooks.TriggerAsync<JsonElement>("new-order", order)); // throws on an error status

// or with the response, without status check:
using var response = await webhooks.PostAsync("new-order", order);
```

Keyed registrations (`AddKeyedN8nClient`) register a keyed `N8nWebhookClient` as well. No API key is sent; secure webhooks with the authentication of the Webhook node.

## Configuration

Settings (`Endpoint`, `ApiKey`, `DisableHealthChecks`, `HealthCheckTimeout`) are read from `Aspire:N8n:Client` (keyed: overridden by `Aspire:N8n:Client:{name}`). `ConnectionStrings:{name}` takes precedence: either the n8n URL, or `Endpoint={url};ApiKey={key}` as passed by the hosting integration with `WithApiKey()`.

## Feedback & contributing

https://github.com/zemires/zemires-aspire
