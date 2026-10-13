using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Lifecycle;
using Aspire.Hosting.Pipelines;
using Aspire.Hosting.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Zemires.Aspire.Hosting.Terraform.Azure;
using Zemires.Aspire.Hosting.Terraform;

#pragma warning disable ASPIREPIPELINES001
#pragma warning disable IDE0130

namespace Aspire.Hosting;

/// <summary>
/// Provides extension methods for configuring Azure-specific Terraform template publishing.
/// </summary>
public static class TerraformAzureBuilderExtensions
{
    /// <summary>
    /// Adds Azure-specific Terraform template publishing capabilities with enhanced Azure resource support.
    /// </summary>
    /// <param name="builder">The distributed application builder instance.</param>
    /// <param name="configureOptions">Optional action to configure publishing options.</param>
    /// <returns>The distributed application builder for method chaining.</returns>
    /// <remarks>
    /// This method extends the base Terraform template functionality with Azure-specific features
    /// and optionally disables the built-in Bicep provisioner to avoid conflicts.
    /// </remarks>
    [AspireExportIgnore(Reason = "Configured through a TerraformTemplatePublishingOptions callback, which is not ATS-compatible; AddTerraformAzureEnvironment registers publishing with defaults.")]
    public static IDistributedApplicationBuilder AddTerraformAzureTemplatePublishing(this IDistributedApplicationBuilder builder, Action<TerraformTemplatePublishingOptions>? configureOptions = null)
    {
        var configuration = builder.Configuration.GetSection("Terraform:Templates");

        var optionsBuilder = builder.Services.AddOptions<TerraformTemplatePublishingOptions>()
            .Bind(configuration);

        if (configureOptions != null)
            optionsBuilder.Configure(configureOptions);

        builder.Services.AddOptions<AzureProvisioningOptions>()
            .Configure(options =>
            {
                options.SupportsTargetedRoleAssignments = true;
            });

        builder.Services.TryAddEventingSubscriber<TerraformAzureContainerInfrastructure>();

        builder.Services.AddSingleton<ITerraformTemplatePublisher, TerraformAzureTemplatePublisher>();
        builder.Services.AddTransient<TerraformTemplateProcessor>();
        builder.Services.AddTransient<TerraformExecutor>();

        // Remove Bicep Azure PipelineAnnotations
        builder.Eventing.Subscribe<BeforePublishEvent>(async (evt, ct) =>
        {
#pragma warning disable ASPIREAZURE001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            var azureenv = evt.Model.Resources.OfType<AzureEnvironmentResource>().FirstOrDefault();
            if (azureenv != null)
                evt.Model.Resources.Remove(azureenv);

            var azureResoures = evt.Model.Resources.Where(p => p is AzureProvisioningResource || p is AzureBicepResource).ToList();
            foreach (var resource in azureResoures)
            {
                var psa = resource.Annotations.OfType<PipelineStepAnnotation>().ToList();
                foreach (var annotation in psa)
                {
                    resource.Annotations.Remove(annotation);
                }
            }
#pragma warning restore ASPIREAZURE001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        });

        builder.Eventing.Subscribe<BeforeStartEvent>(async (evt, ct) =>
        {
#pragma warning disable ASPIREAZURE001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            var azureenv = evt.Model.Resources.OfType<AzureEnvironmentResource>().FirstOrDefault();
            if (azureenv != null)
                evt.Model.Resources.Remove(azureenv);

            var azureResoures = evt.Model.Resources.Where(p => p is AzureProvisioningResource || p is AzureBicepResource).ToList();
            foreach (var resource in azureResoures)
            {
                var psa = resource.Annotations.OfType<PipelineStepAnnotation>().ToList();
                foreach (var annotation in psa)
                {
                    resource.Annotations.Remove(annotation);
                }
            }

#pragma warning restore ASPIREAZURE001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        });

        return builder;
    }

    /// <summary>
    /// Adds an Azure Terraform environment that publishes the resources deployed to it as Terraform configuration,
    /// deploys the container registry first, and registers Azure Terraform template publishing with default options.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The name of the environment resource.</param>
    /// <param name="templatesLocation">The directory or URL containing the Handlebars templates.</param>
    /// <returns>The resource builder for chaining.</returns>
    [AspireExport]
    public static IResourceBuilder<TerraformAzureEnvironmentResource> AddTerraformAzureEnvironment(this IDistributedApplicationBuilder builder, string name, string templatesLocation)
    {
        builder.AddTerraformAzureTemplatePublishing();

        var resource = new TerraformAzureEnvironmentResource(name, templatesLocation);

        return builder.AddResource(resource)
            .ExcludeFromManifest()
            .WithInitialState(new()
            {
                ResourceType = nameof(TerraformAzureEnvironmentResource),
                Properties = [],
                IsHidden = true // hidden from the dashboard
            });
    }

    /// <summary>
    /// Adds an Azure Terraform environment that deploys to Azure Container Apps, using the published Azure Container Apps templates by default.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The name of the environment resource.</param>
    /// <param name="templatesLocation">The directory or URL containing the Handlebars templates.</param>
    /// <returns>The resource builder for chaining.</returns>
    [AspireExport]
    public static IResourceBuilder<TerraformAzureEnvironmentResource> AddTerraformAzureContainerAppEnvironment(this IDistributedApplicationBuilder builder, string name, string templatesLocation = "https://raw.githubusercontent.com/zemires/zemires-aspire/refs/heads/main/templates/azure-container-apps/")
        => builder.AddTerraformAzureEnvironment(name, templatesLocation);
}
