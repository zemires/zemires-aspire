# Zemires.Aspire.Zitadel

An Aspire client integration that registers the [Zitadel](https://zitadel.com) API service clients (from the [Zitadel .NET SDK](https://github.com/smartive/zitadel-net)) and a health check in the DI container.

## Install the package

```dotnetcli
dotnet add package Zemires.Aspire.Zitadel
```

## Usage

In the AppHost, pass `ZitadelResource.ApiConnectionString` (`Endpoint=...;ServiceAccountKey=<base64 machine user key>`, see `Zemires.Aspire.Hosting.Zitadel`) as a connection string resource:

```csharp
var zitadel = builder.AddZitadel("zitadel").WithMachineUser();

var zitadelApi = builder.AddConnectionString("zitadel-api", zitadel.Resource.ApiConnectionString);

builder.AddProject<Projects.AdminApp>("admin")
    .WithReference(zitadelApi)
    .WaitFor(zitadel); // the machine user key is written on the first start of Zitadel
```

`WithReference(zitadel)` alone provides only the endpoint (`ConnectionStrings:zitadel`); credentials can then be passed via configuration, e.g. `Aspire__Zitadel__Client__PersonalAccessToken`.

In the consuming service:

```csharp
builder.AddZitadelApi("zitadel-api");
// or keyed: builder.AddKeyedZitadelApi("zitadel-api");
```

Then inject one of the registered service clients: `AdminService.AdminServiceClient`, `AuthService.AuthServiceClient`, `ManagementService.ManagementServiceClient`, `UserService.UserServiceClient` (v2), `OrganizationService.OrganizationServiceClient` (v2), `SessionService.SessionServiceClient` (v2), `SettingsService.SettingsServiceClient` (v2), `OIDCService.OIDCServiceClient` (v2). For other services, inject `Clients.Options` and create the client yourself (e.g. `Clients.SystemService(options)`).

```csharp
app.MapGet("/users", async (UserService.UserServiceClient users) => await users.ListUsersAsync(new ListUsersRequest()));
```

## Configuration

Settings are read from the `Aspire:Zitadel:Client` configuration section (`Aspire:Zitadel:Client:{name}` for keyed registrations):

| Setting                 | Description                                                                 |
|-------------------------|-----------------------------------------------------------------------------|
| `Endpoint`              | Base URI of the Zitadel instance.                                           |
| `PersonalAccessToken`   | PAT of a machine user.                                                      |
| `ServiceAccountKey`     | JSON key of a machine user (plain or base64 encoded JSON).                  |
| `ServiceAccountKeyPath` | Path to the JSON key file of a machine user.                                |
| `DisableHealthChecks`   | Disables the health check (`/healthz`).                                     |
| `HealthCheckTimeout`    | Health check timeout in milliseconds.                                       |

The connection string `ConnectionStrings:{name}` takes precedence over `Endpoint`. It is either the plain endpoint URL (as provided by the hosting integration) or a key/value string with `Endpoint`, `ServiceAccountKey`, `ServiceAccountKeyPath` and `PersonalAccessToken`, e.g. `Endpoint=https://localhost:8501;ServiceAccountKey=eyJ0eXBlIjoi...`. Without credentials, the clients are created unauthenticated and each call must pass its own token.

## Feedback & contributing

https://github.com/zemires/zemires-aspire
