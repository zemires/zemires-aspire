# Zemires.Aspire.Hosting.Zitadel

An Aspire hosting integration for the [Zitadel](https://zitadel.com) identity provider. Attach a Postgres database (container or Azure Postgres), add the login v2 UI and initialize a Zitadel project to consume directly from web apps and APIs.

## Install the package

In your AppHost project:

```dotnetcli
dotnet add package Zemires.Aspire.Hosting.Zitadel
```

## Example usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var database = builder.AddPostgres("postgres").AddDatabase("zitadel-db", "zitadel");

var zitadel = builder.AddZitadel("zitadel", port: 8501)
    .WithExternalHttpEndpoints()
    .WithDatabase(database)
    .WithOrganizationName("ASPIRE")
    .WithMachineUser()
    .WithLoginClientKey();

// Optional: login v2 UI. Without it, login v1 is used.
zitadel.AddZitadelLoginClient("zitadel-login", port: 8503)
    .WithExternalHttpEndpoints();

var project = zitadel.AddProject("zitadel-project", "Aspire")
    .WithInitialization(async (options, resource) => { /* create apps, roles, ... via the Zitadel API client options */ });

builder.Build().Run();
```

To call the Zitadel API from a service with the machine user, use `zitadel.Resource.ApiConnectionString` (`Endpoint=...;ServiceAccountKey=<base64 key>`) with `builder.AddConnectionString("zitadel-api", ...)` and `AddZitadelApi("zitadel-api")` from `Zemires.Aspire.Zitadel`.

Further options: `WithExternalDomain`, `WithCertificate`, `WithHostPort`, `WithOpenTelemetry`, `WithInitialization`. Initialization callbacks receive ready-to-use Zitadel API client options (authenticated as the machine user).

See https://zitadel.com/docs/guides/overview.

## Feedback & contributing

https://github.com/zemires/zemires-aspire
