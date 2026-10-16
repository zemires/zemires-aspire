using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Lifecycle;
using Aspire.Hosting.Pipelines;
using Aspire.Hosting.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zemires.Aspire.Hosting.Terraform;
using Zemires.Aspire.Hosting.Terraform.Azure;

#pragma warning disable ASPIREPIPELINES001
#pragma warning disable IDE0130

namespace Aspire.Hosting;

/// <summary>
/// Extension methods for publishing Azure resources and Azure Container Apps as Terraform configuration.
/// </summary>
public static class TerraformAzureBuilderExtensions
{
    private const string DefaultTemplatesLocation = "https://raw.githubusercontent.com/zemires/zemires-aspire/refs/heads/main/templates/azure-container-apps/";

    /// <summary>
    /// Registers the Azure Terraform template publisher and removes the Bicep pipeline steps of Azure resources.
    /// Called by <see cref="AddTerraformAzureEnvironment"/>; call it directly only to configure the options.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="configureOptions">Configures the publishing options.</param>
    /// <returns>The builder.</returns>
    [AspireExportIgnore(Reason = "Configured through a TerraformTemplatePublishingOptions callback, which is not ATS-compatible; AddTerraformAzureEnvironment registers publishing with defaults.")]
    public static IDistributedApplicationBuilder AddTerraformAzureTemplatePublishing(this IDistributedApplicationBuilder builder, Action<TerraformTemplatePublishingOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddTerraformServices(configureOptions);

        if (builder.Services.Any(d => d.ImplementationType == typeof(TerraformAzureTemplatePublisher)))
        {
            return builder;
        }

        builder.Services.Replace(ServiceDescriptor.Singleton<ITerraformTemplatePublisher, TerraformAzureTemplatePublisher>());
        builder.Services.AddOptions<AzureProvisioningOptions>()
            .Configure(options => options.SupportsTargetedRoleAssignments = true);
        builder.Services.TryAddEventingSubscriber<TerraformAzureContainerInfrastructure>();

        builder.Eventing.Subscribe<BeforePublishEvent>((evt, _) => RemoveBicepDeployment(evt.Model));
        builder.Eventing.Subscribe<BeforeStartEvent>((evt, _) => RemoveBicepDeployment(evt.Model));

        return builder;
    }

    /// <summary>
    /// Adds an Azure Terraform environment. Deploying first provisions the container registry, then applies the configuration.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name.</param>
    /// <param name="templatesLocation">The directory or URL of the Handlebars templates.</param>
    /// <returns>The environment resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<TerraformAzureEnvironmentResource> AddTerraformAzureEnvironment(this IDistributedApplicationBuilder builder, [ResourceName] string name, string templatesLocation)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(templatesLocation);

        builder.AddTerraformAzureTemplatePublishing();

        return builder.AddEnvironmentResource(new TerraformAzureEnvironmentResource(name, templatesLocation));
    }

    /// <summary>
    /// Adds an Azure Terraform environment deploying to Azure Container Apps, by default with the
    /// <c>templates/azure-container-apps</c> templates of the <c>main</c> branch on GitHub.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name.</param>
    /// <param name="templatesLocation">The directory or URL of the Handlebars templates.</param>
    /// <returns>The environment resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<TerraformAzureEnvironmentResource> AddTerraformAzureContainerAppEnvironment(this IDistributedApplicationBuilder builder, [ResourceName] string name, string templatesLocation = DefaultTemplatesLocation)
        => builder.AddTerraformAzureEnvironment(name, templatesLocation);

    /// <summary>
    /// Removes the Azure (Bicep) environment and the pipeline steps of Azure resources; Terraform deploys them instead.
    /// </summary>
    private static Task RemoveBicepDeployment(DistributedApplicationModel model)
    {
#pragma warning disable ASPIREAZURE001
        if (model.Resources.OfType<AzureEnvironmentResource>().FirstOrDefault() is { } azureEnvironment)
        {
            model.Resources.Remove(azureEnvironment);
        }
#pragma warning restore ASPIREAZURE001

        foreach (var resource in model.Resources.Where(r => r is AzureProvisioningResource or AzureBicepResource))
        {
            foreach (var annotation in resource.Annotations.OfType<PipelineStepAnnotation>().ToList())
            {
                resource.Annotations.Remove(annotation);
            }
        }

        return Task.CompletedTask;
    }
}
