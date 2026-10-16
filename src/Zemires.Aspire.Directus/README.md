# Zemires.Aspire.Directus

An Aspire client integration that registers a [Directus](https://directus.io) client (`Qute.Directus`) and a health check in the DI container.

## Install the package

```dotnetcli
dotnet add package Zemires.Aspire.Directus
```

## Usage

In the AppHost, reference the Directus resource (see `Zemires.Aspire.Hosting.Directus`). In the consuming service:

```csharp
builder.AddDirectusClient("directus");
```

Then inject `DirectusClient`:

```csharp
app.MapGet("/users", async (DirectusClient client, CancellationToken ct) =>
    await client.Users.GetManyAsync(ct: ct));
```

## Configuration

Options are bound from the `Aspire:Directus:Client` section (or a custom section via the overload). The connection string `ConnectionStrings:{name}` must contain `BaseUrl` and may contain `Token` (static token); it takes precedence over the section. Set `DisableHealthChecks` / `HealthCheckTimeout` (ms) in the same section to configure the health check.

## Feedback & contributing

https://github.com/zemires/zemires-aspire
