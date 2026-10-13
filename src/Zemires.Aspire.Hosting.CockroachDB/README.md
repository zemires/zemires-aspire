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

builder.AddProject<Projects.ApiService>("api")
    .WithReference(db)
    .WaitFor(db);

builder.Build().Run();
```

Further options: `WithUserName`, `WithPassword`, `WithHostPort`, `WithDataBindMount`, and `WithCreationScript` on a database to customize the `CREATE DATABASE` statement. The database exposes a PostgreSQL-compatible connection string, so it can be consumed with Npgsql or EF Core (e.g. `CockroachDB.EFCore.Provider`).

## Feedback & contributing

https://github.com/zemires/zemires-aspire
