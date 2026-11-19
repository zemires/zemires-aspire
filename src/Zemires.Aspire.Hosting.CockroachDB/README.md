# Zemires.Aspire.Hosting.CockroachDB

An Aspire hosting integration for [CockroachDB](https://www.cockroachlabs.com), a distributed SQL database compatible with the PostgreSQL wire protocol.

## Install the package

In your AppHost project:

```dotnetcli
dotnet add package Zemires.Aspire.Hosting.CockroachDB
```

## Example usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var roach = builder.AddCockroachDB("roach")
    .WithDataVolume();

var db = roach.AddDatabase("mydb");

builder.AddDotnetProject("api", "../ApiService/ApiService.csproj")
    .WithReference(db)
    .WaitFor(db);

builder.Build().Run();
```

Further options: `WithUserName`, `WithPassword`, `WithHostPort`, `WithDataBindMount`, and `WithCreationScript` on a database to customize the `CREATE DATABASE` statement. The database exposes a PostgreSQL-compatible connection string, so it can be consumed with Npgsql or EF Core (e.g. `CockroachDB.EFCore.Provider`).

The node runs in secure mode: password authentication is not available with `--insecure`. CockroachDB generates its certificates on first start and keeps them in the data folder (`/cockroach/cockroach-data/certs`). SQL clients connect over TLS. Npgsql's default `SSL Mode=Prefer` accepts the self-signed certificate. The DB console (`https` endpoint) is served over HTTPS; sign in with the user name and password.

A data volume or bind mount created by an earlier version (insecure mode) has no certificates. Delete it, or the node fails with "no certificates found".

## Feedback & contributing

https://github.com/zemires/zemires-aspire
