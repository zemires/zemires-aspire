# Zemires.Aspire.Hosting.Directus

An Aspire hosting integration for the [Directus](https://directus.io) headless CMS.

## Install the package

In your AppHost project:

```dotnetcli
dotnet add package Zemires.Aspire.Hosting.Directus
```

## Example usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddPostgres("postgres").WithDataVolume().AddDatabase("db");

var directus = builder.AddDirectus("directus", port: 8055)
    .WithUserName(builder.AddParameter("userName", "admin@local.dev"))
    .WithPostgresDatabase(db)
    .WithDataBindMount("./.data");

builder.AddProject<Projects.ApiService>("api")
    .WithReference(directus)
    .WaitFor(directus);

builder.Build().Run();
```

- Database: `WithSqlite3Database()`, `WithPostgresDatabase(db)`, `WithSqlServerDatabase(db)`.
- Credentials: `WithUserName`, `WithPassword`, `WithAdminToken` (generated when not provided).
- Storage: `WithDataVolume`, `WithDataBindMount`.

Use the `Zemires.Aspire.Directus` client integration to consume Directus from your services.

## Feedback & contributing

https://github.com/zemires/zemires-aspire
