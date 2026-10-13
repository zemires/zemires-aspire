# Zemires.Aspire.Hosting.Terraform.Azure

An Aspire integration that publishes Azure resources and projects/containers to [Azure Container Apps](https://learn.microsoft.com/azure/container-apps/) using Terraform. Built on `Zemires.Aspire.Hosting.Terraform`.

## Install the package

In your AppHost project:

```dotnetcli
dotnet add package Zemires.Aspire.Hosting.Terraform.Azure
```

## Example usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Templates default to templates/azure-container-apps of https://github.com/zemires/zemires-aspire;
// pass a local folder or another URL to customize them.
builder.AddTerraformAzureContainerAppEnvironment("env");

var storage = builder.AddAzureStorage("storage");
var queue = storage.AddQueue("queue1", "myqueue");
var db = builder.AddAzureSqlServer("sql").AddDatabase("db");

builder.AddProject<Projects.ApiService>("api")
    .WithReference(queue)
    .WithReference(db);

builder.Build().Run();
```

Supported Azure resources include Storage (blob, queue, table), Key Vault, SQL, PostgreSQL, Redis / Managed Redis, Cosmos DB, Service Bus, Event Hubs, SignalR, AI Search, Application Insights, user-assigned identities and role assignments.

`aspire deploy` provisions the container registry, applies the Terraform configuration and deploys the container apps. Requires the `az` CLI and the [OpenTofu](https://opentofu.org) CLI `tofu` (default; use the Terraform CLI instead by setting `Terraform:Executor:ExecutablePath` to `terraform` in the AppHost's `appsettings.json`, see `Zemires.Aspire.Hosting.Terraform`).

## Feedback & contributing

https://github.com/zemires/zemires-aspire
