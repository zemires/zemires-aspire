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

### Initialization outputs

Values created by an initialization callback (e.g. the client id and secret of an OIDC application) are passed to other resources as outputs. Declare them with `GetOutput` / `GetSecretOutput` and set them in the callback with `context.SetOutput`. They are resolved lazily: a consumer evaluated before the initialization has completed waits for the value (still use `WaitFor(project)`, so a failed initialization stops the consumer).

```csharp
var project = zitadel.AddProject("zitadel-project", "Aspire")
    .WithInitialization(async context =>
    {
        // create the app with Clients.ManagementService(context.Options) ...
        context.SetOutput("client-id", app.ClientId);
        context.SetOutput("client-secret", app.ClientSecret);
    });

builder.AddProject<Projects.WebApp>("webapp")
    .WithEnvironment("Oidc__Authority", zitadel.Resource.PrimaryEndpoint)
    .WithEnvironment("Oidc__ClientId", project.GetOutput("client-id"))
    .WithEnvironment("Oidc__ClientSecret", project.GetSecretOutput("client-secret"))
    .WaitFor(project);
```

- Values set with `SetOutput` are persisted in the AppHost user secrets as `Parameters:{resource}-{output}` (e.g. `Parameters:zitadel-project-client-secret`). On the next start, `context.GetOutputValueAsync(name)` returns the known value, and outputs not set again by the callback fall back to it — useful for secrets Zitadel only returns on creation.
- The same configuration key can be set manually (user secrets, appsettings, environment) to provide a value.
- `WithOutput(name, parameter)` binds an output to a parameter, e.g. for an existing application; `SetOutput` does not change it then.
- If a callback throws or an output stays unset, the project goes to `FailedToStart` and the output fails instead of resolving to an empty value.

`WithMachineUser()` and `WithLoginClientKey()` write the machine user key and login client PAT to `.zitadel/{name}-keys` in the AppHost directory; add `.zitadel/` to `.gitignore`.

To call the Zitadel API from a service with the machine user, use `zitadel.AddApiConnectionString()` (connection string resource `zitadel-api` holding `Endpoint=...;ServiceAccountKey=<base64 key>`) and `AddZitadelApi("zitadel-api")` from `Zemires.Aspire.Zitadel`.

Further options: `WithExternalDomain`, `WithCertificate`, `WithHostPort`, `WithOtlpExporter`, `WithInitialization`. Initialization callbacks receive ready-to-use Zitadel API client options (authenticated as the machine user).

See https://zitadel.com/docs/guides/overview.

## Feedback & contributing

https://github.com/zemires/zemires-aspire
