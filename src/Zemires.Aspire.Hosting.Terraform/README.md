# Zemires.Aspire.Hosting.Terraform

An Aspire integration that publishes the application model as [Terraform](https://www.terraform.io) configuration rendered from Handlebars (`*.tf.hbs`) templates, and deploys it with `terraform apply`.

## Install the package

In your AppHost project:

```dotnetcli
dotnet add package Zemires.Aspire.Hosting.Terraform
```

## Example usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddTerraformEnvironment("env", "./templates");

var tf = builder.AddTerraformTemplate("tf1", "tf1.tf.hbs")
    .WithParameter("len", "5");

builder.AddContainer("container1", "mcr.microsoft.com/azuredocs/aci-helloworld:latest")
    .WithEnvironment("TEST", tf.GetOutput("result"));

builder.Build().Run();
```

`aspire publish` renders the templates (pipeline steps `prepare-terraform-<env>`, `publish-terraform-<env>`); `aspire deploy` runs `terraform apply` (`deploy-terraform-<env>`). Requires the `terraform` CLI.

- `AddTerraformTemplate` / `AddTerraformTemplateString` — add custom templates.
- `WithTerraformTemplate`, `WithParameter`, `WithTerraformTemplateParameter` — attach templates and parameters to resources.
- `GetOutput`, `GetSecretOutput`, `GetTerraformOutput`, `GetTerraformLocal` — reference Terraform outputs and locals.

For Azure targets see `Zemires.Aspire.Hosting.Terraform.Azure`.

## Feedback & contributing

https://github.com/zemires/zemires-aspire
