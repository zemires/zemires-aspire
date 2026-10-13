# Zemires.Aspire

.NET Aspire extensions by zemires for **n8n**, **Terraform**, **Zitadel**, **Directus** and **CockroachDB**.

## Packages

| Extension   | Package                                    | Description |
|-------------|--------------------------------------------|-------------|
| n8n         | `Zemires.Aspire.Hosting.N8n`               | Hosting integration for [n8n](https://n8n.io) workflow automation: main instance, queue-mode workers, task runners, Postgres, Redis, OpenTelemetry, metrics. |
|             | `Zemires.Aspire.N8n`                       | Client integration registering an `N8nClient` with health check. |
|             | `Zemires.N8n.Api`                          | Kiota-generated client for the n8n public API. |
| Terraform   | `Zemires.Aspire.Hosting.Terraform`         | Publishes the Aspire application model as Terraform configuration from Handlebars templates and deploys it with OpenTofu (default) or Terraform. |
|             | `Zemires.Aspire.Hosting.Terraform.Azure`   | Maps Azure resources and Azure Container Apps to Terraform (templates in [`templates/azure-container-apps`](templates/azure-container-apps)). |
| Zitadel     | `Zemires.Aspire.Hosting.Zitadel`           | Hosting integration for the [Zitadel](https://zitadel.com) identity provider incl. login v2 UI, machine user and project initialization. |
| Directus    | `Zemires.Aspire.Hosting.Directus`          | Hosting integration for the [Directus](https://directus.io) headless CMS (SQLite, Postgres or SQL Server). |
|             | `Zemires.Aspire.Directus`                  | Client integration registering a Directus client with health check. |
| CockroachDB | `Zemires.Aspire.Hosting.CockroachDB`       | Hosting integration for [CockroachDB](https://www.cockroachlabs.com) with databases and creation scripts. |

## Repository layout

| Folder        | Purpose |
|---------------|---------|
| `src/`        | Projects that are released as NuGet packages. |
| `examples/`   | Minimal runnable examples used for documentation. |
| `playground/` | Projects to test, develop and experiment, grouped per extension (`n8n`, `terraform`, `zitadel`, `directus`, `cockroach`). |
| `templates/`  | Handlebars (`*.tf.hbs`) Terraform module templates, one folder per target (e.g. `azure-container-apps`). |
| `tests/`      | xUnit v3 test projects. |
| `docs/`       | Shared documentation assets. |

## Prerequisites

- .NET SDK 10 (packages target `net8.0`, `net9.0` and `net10.0`, Aspire 13)
- Docker or Podman for running AppHosts and integration tests
- OpenTofu CLI (`tofu`, default) or Terraform CLI, plus Azure CLI for Azure targets, for Terraform deployment
- Kiota — only to regenerate the n8n API client

## Build & test

```shell
dotnet restore
dotnet build Zemires.Aspire.slnx
dotnet test --solution Zemires.Aspire.slnx
```

Run the n8n example AppHost:

```shell
dotnet run --project examples\Zemires.Aspire.Hosting.N8n.AppHost\Zemires.Aspire.Hosting.N8n.AppHost.csproj
```

## Usage

### n8n

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres").WithDataVolume();
var db = postgres.AddDatabase("n8n-db");
var redis = builder.AddRedis("redis").WithDataVolume();

var n8n = builder.AddN8n("n8n", port: 5678)
    .WithDataBindMount("./.n8n_data")
    .WithPostgresDatabase(db)
    .WithQueueMode(redis)
    .WithTimeZone("Europe/Zurich")
    .WithOtlpExporter();

var worker = n8n.AddWorker("n8n-worker", port: 5679)
    .WithPostgresDatabase(db)
    .WithQueueMode(redis);

worker.AddTaskRunner("runner");

builder.Build().Run();
```

In a consuming service: `builder.AddN8nClient("n8n");`

### Terraform (Azure Container Apps)

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddTerraformAzureContainerAppEnvironment("env", "../templates/azure-container-apps");

var storage = builder.AddAzureStorage("storage");
var queue = storage.AddQueue("queue1", "myqueue");

var tf = builder.AddTerraformTemplate("tf-template", "my-template.tf.hbs")
    .WithParameter("tfp1", "Hello");

builder.AddProject<Projects.ApiService>("api")
    .WithReference(queue)
    .WithEnvironment("OUTPUT_TF", tf.GetOutput("output1"));

builder.Build().Run();
```

`aspire publish` renders the Terraform configuration; `aspire deploy` runs `tofu apply`. For a generic (non-Azure) environment use `builder.AddTerraformEnvironment("env")`.

The default tool is [OpenTofu](https://opentofu.org) (`tofu`). To use another executable, e.g. the Terraform CLI, set `Terraform:Executor:ExecutablePath` in the AppHost's `appsettings.json`:

```json
{
  "Terraform": {
    "Executor": {
      "ExecutablePath": "terraform"
    }
  }
}
```

### Zitadel

```csharp
var database = builder.AddPostgres("postgres").AddDatabase("zitadel-db", "zitadel");

var zitadel = builder.AddZitadel("zitadel", port: 8501)
    .WithExternalHttpEndpoints()
    .WithDatabase(database)
    .WithOrganizationName("ASPIRE")
    .WithMachineUser()
    .WithLoginClientKey();

zitadel.AddZitadelLoginClient("zitadel-login", port: 8503)
    .WithExternalHttpEndpoints();

var project = zitadel.AddProject("zitadel-project", "Aspire");
```

### Directus

```csharp
var db = builder.AddPostgres("postgres").WithDataVolume().AddDatabase("db");

var directus = builder.AddDirectus("directus", port: 8055)
    .WithUserName(builder.AddParameter("userName", "admin@local.dev"))
    .WithPostgresDatabase(db)
    .WithDataBindMount("./.data");
```

In a consuming service: `builder.AddDirectusClient("directus");`

### CockroachDB

```csharp
var roach = builder.AddCockroachDB("roach").WithDataVolume();
var db = roach.AddDatabase("mydb");

builder.AddProject<Projects.ApiService>("api")
    .WithReference(db)
    .WaitFor(db);
```

More complete scenarios live in [`playground/`](playground).

## Contributing

- Work on a feature branch and merge into `main` via pull request (squash merge).
- Packable code goes to `src/`, documentation examples to `examples/`, experiments to `playground/<extension>/`, Terraform module templates to `templates/<target>/`, tests to `tests/`.
- Package versions are managed centrally in `Directory.Packages.props`.

See [`.github/copilot-instructions.md`](.github/copilot-instructions.md) for detailed conventions.

## License

[MIT](LICENSE)
