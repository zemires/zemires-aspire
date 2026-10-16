# Zemires.Aspire.N8n

An Aspire client integration that registers an `N8nClient` (n8n public API, from `Zemires.N8n.Api`) and a health check in the DI container.

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

## Configuration

Settings (`Endpoint`, `ApiKey`, `DisableHealthChecks`, `HealthCheckTimeout`) are read from `Aspire:N8n:Client` (keyed: overridden by `Aspire:N8n:Client:{name}`). `ConnectionStrings:{name}` (the n8n URL) takes precedence over `Endpoint`.

## Feedback & contributing

https://github.com/zemires/zemires-aspire
