# Repository instructions

Shared guidance for AI coding agents (GitHub Copilot, Claude Code via `CLAUDE.md`) working in this repository.

## What this is

`Zemires.Aspire` — a collection of .NET Aspire extensions (hosting integrations, client integrations and publishers) for:

| Extension   | Hosting package (`src/`)                                   | Client / extra packages (`src/`)               |
|-------------|------------------------------------------------------------|------------------------------------------------|
| n8n         | `Zemires.Aspire.Hosting.N8n`                               | `Zemires.Aspire.N8n`, `Zemires.N8n.Api`        |
| Terraform   | `Zemires.Aspire.Hosting.Terraform`, `.Terraform.Azure`     | templates in `templates/azure-container-apps`  |
| Zitadel     | `Zemires.Aspire.Hosting.Zitadel`                           | —                                              |
| Directus    | `Zemires.Aspire.Hosting.Directus`                          | `Zemires.Aspire.Directus`                      |
| CockroachDB | `Zemires.Aspire.Hosting.CockroachDB`                       | —                                              |

The repository was created by importing and merging several previously separate projects (some originally targeting the CommunityToolkit.Aspire repo). If you find leftovers from that origin (CommunityToolkit names, namespaces or links), align them with the conventions below.

Targets `net10.0`, Aspire `13.x` (`AspireVersion` in `Directory.Build.props`, AppHost SDK in `global.json`).

## Repository layout (core principles)

- **`src/`** — the only projects that are released / packed as NuGet packages (`IsPackable=true` via `src/Directory.Build.props`). One folder per package.
- **`examples/`** — small, runnable documentation examples (AppHosts) referenced from the README files. Keep them minimal and in sync with the documented API.
- **`playground/<extension>/<App>/`** — projects to test, develop and experiment with (e.g. `playground/n8n/WorkflowApp`, `playground/terraform/AzureContainerApps`, `playground/zitadel/ZitadelApp`, `playground/directus/DirectusApp`, `playground/cockroach/CockroachDBTest`). Never packed; may be rough. Group new experiments under the matching extension folder.
- **`templates/<target>/`** — Handlebars (`*.tf.hbs`) templates and static `.tf` files forming Terraform modules used by the Terraform publisher (currently `templates/azure-container-apps`). One folder per deployment target.
- **`tests/`** — xUnit v3 test projects, named `<SrcProject>.Tests`.
- **`docs/`** — shared documentation assets (e.g. `docs/images/nuget.png`, the package icon).

New projects must be added to `Zemires.Aspire.slnx` in the matching solution folder. Project references are relative; playground/example AppHosts reference `src` projects with `IsAspireProjectResource="false"`.

## Workflow

- Never commit directly to `main`. Create a feature branch (e.g. `feature/<topic>`, `fix/<topic>`) for every change.
- Merge into `main` via a pull request using **squash merge**; the PR title becomes the commit message, so make it descriptive.
- Before opening a PR, `dotnet build Zemires.Aspire.slnx` must pass (`src` treats warnings as errors) and relevant tests must pass; the CI workflow must be green before merging.

## Commands

```shell
dotnet restore
dotnet build Zemires.Aspire.slnx
dotnet test --solution Zemires.Aspire.slnx
dotnet test --project tests\Zemires.Aspire.Hosting.N8n.Tests\Zemires.Aspire.Hosting.N8n.Tests.csproj
dotnet test --project tests\Zemires.Aspire.Hosting.N8n.Tests\Zemires.Aspire.Hosting.N8n.Tests.csproj --filter-method "*TestMethod"
dotnet test --project tests\Zemires.Aspire.Hosting.Terraform.Tests\Zemires.Aspire.Hosting.Terraform.Tests.csproj
dotnet test --project tests\Zemires.Aspire.Hosting.Terraform.Azure.Tests\Zemires.Aspire.Hosting.Terraform.Azure.Tests.csproj
dotnet run --project examples\Zemires.Aspire.Hosting.N8n.AppHost\Zemires.Aspire.Hosting.N8n.AppHost.csproj
dotnet pack src\<Project>\<Project>.csproj   # output goes to ./nuget
```

`global.json` opts into the Microsoft.Testing.Platform `dotnet test` mode (required by xunit.v3 4.x), so pass projects with `--project` / `--solution`. Integration tests and most playground AppHosts need a running container runtime (Docker or `podman machine start`). Terraform publishing needs the `terraform` CLI (and `az` for Azure targets).

To regenerate the n8n API client (from `src\Zemires.N8n.Api`, spec file `openapi.yml`, see `kiota-lock.json`):

```shell
kiota generate -d .\openapi.yml -l csharp -n Zemires.N8n.Api -c N8nClient -o .
```

## Build & versioning

- Package versions are centrally managed in `Directory.Packages.props` — never put `Version=` on a `PackageReference`.
- Shared properties (`DefaultTargetFramework`, `AspireVersion`, `AspireMajorVersion`, `ToolkitMinorVersion`, `ToolkitPatchVersion`) live in the root `Directory.Build.props`. Package version = `$(AspireMajorVersion).$(ToolkitMinorVersion).$(ToolkitPatchVersion)`; local builds get a `-dev.<timestamp>` suffix, release builds are validated in `Directory.Build.targets`.
- `src/Directory.Build.props` / `.targets` add packaging metadata, SourceLink, XML docs (CS1591 etc. as errors), the package icon and pack each project's `README.md` as the NuGet readme — **every `src` project needs a `README.md`**.
- `src` projects build with `TreatWarningsAsErrors` and run `Aspire.Hosting.Integration.Analyzers`: public hosting extension methods need `[AspireExport]` or `[AspireExportIgnore]`. Playground, examples and tests keep warnings as warnings.
- AppHost projects use `Sdk="Aspire.AppHost.Sdk"` (versionless; pinned in `global.json`). Preserve this when editing.

## Architecture

### n8n
- **`Zemires.Aspire.Hosting.N8n`** — `AddN8n()` in `N8nBuilderExtensions.cs` (+ partials `.Worker.cs`, `.TaskRunner.cs`). `N8nResource` extends `ContainerResource` and implements `IResourceWithConnectionString` (`{scheme}://{host}:{port}`). `N8nWorkerResource` (via `AddWorker()`) extends `N8nResource`, implements `IResourceWithParent<N8nResource>` and shares the parent's encryption key; `N8nTaskRunnerResource` via `AddTaskRunner()`. Further options: `WithPostgresDatabase`, `WithQueueMode` (Redis), `WithInstanceOwner`, `WithLicenseKey`, `WithOtlpExporter`, `WithMetrics`, `WithCommunityPackages`, `WithWebhookUrl`, `WithTimeZone`, data volume / bind mount.
  - `WithPostgresDatabase` / `WithQueueMode` extract connection properties (Host, Port, Password, …) from `IResourceWithConnectionString` and map them to n8n environment variables.
  - `WithOtlpExporter` calls `WithOtlpExporter(OtlpProtocol.HttpProtobuf)` then re-maps the `OTEL_*` env vars to `N8N_OTEL_*`.
  - HTTPS via `WithHttpsCertificateConfiguration` (pragma `ASPIRECERTIFICATES001` suppressed intentionally), run mode only; workers have no HTTPS but trust the dev CA via `NODE_EXTRA_CA_CERTS`.
- **`Zemires.Aspire.N8n`** — client integration: `AddN8nClient()` / `AddKeyedN8nClient()` on `IHostApplicationBuilder`. Config from `Aspire:N8n:Client`, falls back to `ConnectionStrings:{name}`. Registers `N8nClient` singleton plus `N8nHealthCheck` unless `DisableHealthChecks`.
- **`Zemires.N8n.Api`** — Kiota-generated n8n public API client. Do not hand-edit generated files; regenerate. Hand-written: `N8nAuthenticationProvider.cs`, `N8nClient.Extensions.cs`.

### Terraform
- **`Zemires.Aspire.Hosting.Terraform`** — publishes the Aspire application model as Terraform configuration rendered from Handlebars (`*.tf.hbs`) templates (Handlebars.Net + helpers). `AddTerraformEnvironment(name, templatesLocation)` adds a compute environment with pipeline steps `prepare-terraform-<env>`, `publish-terraform-<env>`, `deploy-terraform-<env>` (and a destroy step) that render templates and run `terraform` via `TerraformExecutor`. Custom templates: `AddTerraformTemplate` / `AddTerraformTemplateString`, `WithTerraformTemplate`, `WithParameter` / `WithTerraformTemplateParameter`; consume outputs with `GetOutput` / `GetSecretOutput` / `GetTerraformOutput` / `GetTerraformLocal`. `InternalsVisibleTo` the Azure package.
- **`Zemires.Aspire.Hosting.Terraform.Azure`** — `AddTerraformAzureEnvironment` / `AddTerraformAzureContainerAppEnvironment` map Azure resources (Storage, Key Vault, SQL, Postgres, Redis, Cosmos DB, Service Bus, Event Hubs, SignalR, Search, App Insights, managed identities, role assignments) and projects/containers to Azure Container Apps. Adds `deploy-terraform-registry-<env>` and `read-terraform-output-<env>` steps.
- **`templates/azure-container-apps/`** — the Terraform module: one `<resource-kind>.tf.hbs` per Aspire resource type plus static `main.tf`, `providers.tf`, `versions.tf`, `backend.tf`, `variables.tf`, `outputs.tf`. The template file name is derived from the resource type name (`NormalizeTypeName`). When adding Azure resource support, add the template here and a test in `Zemires.Aspire.Hosting.Terraform.Azure.Tests`.
- `playground/terraform/terragrunt` shows consuming the generated module with Terragrunt.

### Zitadel
- **`Zemires.Aspire.Hosting.Zitadel`** — `AddZitadel()` (container `zitadel/zitadel`) with `WithDatabase` (Postgres), `WithOrganizationName`, `WithMachineUser`, `WithLoginClientKey`, `WithExternalDomain`, `WithCertificate`, `WithOpenTelemetry`, `WithInitialization` (callback using the Zitadel .NET SDK). `AddZitadelLoginClient()` adds the v2 login UI container; `AddProject()` creates a `ZitadelProjectResource`. `ZitadelResourceExtensions` provides `GetMachineServiceAccount` / `CreateApiClientOptions` for initialization code.

### Directus
- **`Zemires.Aspire.Hosting.Directus`** — `AddDirectus()` (container `directus/directus`) with `WithUserName`, `WithPassword`, `WithAdminToken`, and storage via `WithSqlite3Database`, `WithPostgresDatabase`, `WithSqlServerDatabase`, data volume / bind mount.
- **`Zemires.Aspire.Directus`** — client integration `AddDirectusClient()` (based on `Qute.Directus`) with `DirectusHealthCheck`.

### CockroachDB
- **`Zemires.Aspire.Hosting.CockroachDB`** — `AddCockroachDB()` (container `cockroachdb/cockroach`), `AddDatabase()` returning `CockroachDBDatabaseResource` (with `WithCreationScript`), `WithUserName`, `WithPassword`, `WithHostPort`, data volume / bind mount. Health check via `AspNetCore.HealthChecks.NpgSql` (Postgres wire protocol). EF Core usage is shown in `playground/cockroach` with `CockroachDB.EFCore.Provider`.

### Tests
- xUnit v3 (`xunit.v3`) with explicit `<Using>` elements and `IsTestProject` metadata.
- n8n integration tests use `DistributedApplicationTestingBuilder.CreateAsync<Projects.Zemires_Aspire_Hosting_N8n_AppHost>()` to boot the example AppHost, then `app.CreateHttpClient(resourceName)` and wait for `KnownResourceStates.Running` via `ResourceNotificationService`.
- Terraform tests verify the rendered templates of the publisher (`TerraformTemplatePublisherTests`).
- Zitadel, Directus and CockroachDB have no test projects yet — add `tests/<SrcProject>.Tests` when changing them.

## Key conventions

- Extension methods live in `namespace Aspire.Hosting` (hosting) or `namespace Microsoft.Extensions.Hosting` (client); resource classes in `namespace Aspire.Hosting.ApplicationModel`. This keeps IntelliSense discoverability consistent with the Aspire ecosystem.
- Container image/tag constants live in `<Name>ContainerImageTags.cs` (internal, library namespace `Zemires.Aspire.Hosting.<Name>`) per hosting package; update tags there.
- Internal helpers (health checks, publishers, executors) stay in the library namespace (`Zemires.Aspire.<Name>`).
- Package names, READMEs and links use `Zemires.*` and `https://github.com/zemires/zemires-aspire`.
- Each hosting extension follows the same shape: `<Name>Resource` (`ContainerResource`, usually `IResourceWithConnectionString`), `<Name>BuilderExtensions` with `Add<Name>()` and fluent `With…()` methods, parameters via `IResourceBuilder<ParameterResource>` for secrets.
- Client integrations bind settings from `Aspire:<name>:Client`, fall back to `ConnectionStrings:{name}`, support keyed registration and register a health check unless disabled.
- Do not commit local runtime data: `.n8n_data/`, `.data/`, `aspire-output/`, `.terragrunt-cache/`, `node_modules/`, `*.user` are git-ignored.
- CI: `.github/workflows/ci.yml` runs restore, Release build, all tests (incl. container-based integration tests) and `dotnet pack` of every `src` project on pull requests and pushes to `main`. Keep it green; run the same commands locally before pushing.
