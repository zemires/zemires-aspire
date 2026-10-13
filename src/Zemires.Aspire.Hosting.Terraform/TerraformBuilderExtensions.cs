using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Zemires.Aspire.Hosting.Terraform;

#pragma warning disable ASPIREPIPELINES001
#pragma warning disable IDE0130

// ReSharper disable once CheckNamespace
namespace Aspire.Hosting;

/// <summary>
/// Provides extension methods for configuring Terraform template-based publishing in Aspire hosting applications.
/// </summary>
public static class TerraformBuilderExtensions
{
    /// <summary>
    /// Adds Terraform template publishing capabilities to the distributed application builder.
    /// This enables generating Terraform configuration files from Handlebars templates for infrastructure deployment.
    /// </summary>
    /// <param name="builder">The distributed application builder instance.</param>
    /// <param name="configureOptions">Optional action to configure publishing options such as templates path and base files.</param>
    /// <returns>The distributed application builder for method chaining.</returns>
    /// <remarks>
    /// This method registers the TerraformTemplatePublisher as a keyed service and configures it to process
    /// Handlebars templates that generate Terraform configuration files. The configuration section
    /// "Terraform:Templates" is automatically bound to the options.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.AddTerraformTemplatePublishing("terraform", options =>
    /// {
    ///     options.TemplatesPath = "./templates/azure";
    ///     options.BaseFiles = "main.tf;variables.tf;outputs.tf";
    /// });
    /// </code>
    /// </example>
    [AspireExportIgnore(Reason = "Configured through a TerraformTemplatePublishingOptions callback, which is not ATS-compatible; AddTerraformEnvironment registers publishing with defaults.")]
    public static IDistributedApplicationBuilder AddTerraformTemplatePublishing(this IDistributedApplicationBuilder builder, Action<TerraformTemplatePublishingOptions>? configureOptions = null)
    {
        var configuration = builder.Configuration.GetSection("Terraform:Templates");
        var optionsBuilder = builder.Services.AddOptions<TerraformTemplatePublishingOptions>()
            .Bind(configuration);

        if (configureOptions != null)
            optionsBuilder.Configure(configureOptions);

        builder.Services.AddSingleton<ITerraformTemplatePublisher, TerraformTemplatePublisher>();
        builder.Services.AddTransient<TerraformTemplateProcessor>();
        builder.Services.AddTransient<TerraformExecutor>();

        return builder;
    }

    /// <summary>
    /// Adds a specific Terraform template resource to the distributed application builder.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Resource name.</param>
    /// <param name="terraformFile">Path to the template file.</param>
    /// <param name="outputFile">Optional output file name.</param>
    /// <param name="appendFile"></param>
    /// <returns>The resource builder for chaining.</returns>
    [AspireExport]
    public static IResourceBuilder<TerraformTemplateResource> AddTerraformTemplate(this IDistributedApplicationBuilder builder, [ResourceName] string name, string terraformFile, string? outputFile = null, bool appendFile = false)
    {
        var env = builder.GetDefaultEnvironment();
        IResourceBuilder<TerraformEnvironmentResource> envBuilder;
        if (env == null)
        {
            builder.AddTerraformTemplatePublishing();
            envBuilder = builder.AddTerraformEnvironment("tf");
        }
        else
        {
            envBuilder = builder.CreateResourceBuilder(env);
        }

        return envBuilder.AddTerraformTemplate(name, terraformFile, outputFile, appendFile);
    }

    /// <summary>
    /// Adds a specific Terraform template resource to the distributed application builder.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Resource name.</param>
    /// <param name="terraformString">content to the template file.</param>
    /// <param name="outputFile">Optional output file name.</param>
    /// <param name="appendFile"></param>
    /// <returns>The resource builder for chaining.</returns>
    [AspireExport]
    public static IResourceBuilder<TerraformTemplateResource> AddTerraformTemplateString(this IDistributedApplicationBuilder builder, [ResourceName] string name, string terraformString, string? outputFile = null, bool appendFile = false)
    {
        var env = builder.GetDefaultEnvironment();
        IResourceBuilder<TerraformEnvironmentResource> envBuilder;
        if (env == null)
        {
            builder.AddTerraformTemplatePublishing();
            envBuilder = builder.AddTerraformEnvironment("tf");
        }
        else
        {
            envBuilder = builder.CreateResourceBuilder(env);
        }

        return envBuilder.AddTerraformTemplateString(name, terraformString, outputFile, appendFile);
    }

    /// <summary>
    /// Adds a Terraform environment that publishes the resources deployed to it as Terraform configuration,
    /// and registers Terraform template publishing with default options.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The name of the environment resource.</param>
    /// <param name="templatesLocation">The directory or URL containing the Handlebars templates.</param>
    /// <returns>The resource builder for chaining.</returns>
    [AspireExport]
    public static IResourceBuilder<TerraformEnvironmentResource> AddTerraformEnvironment(this IDistributedApplicationBuilder builder, string name, string templatesLocation = "./templates")
    {
        builder.AddTerraformTemplatePublishing();

        var resource = new TerraformEnvironmentResource(name, templatesLocation);

        return builder.AddResource(resource)
            .ExcludeFromManifest()
            .WithInitialState(new()
            {
                ResourceType = nameof(TerraformEnvironmentResource),
                Properties = [],
                IsHidden = true // hidden from the dashboard
            });
    }

    internal static TerraformEnvironmentResource? GetDefaultEnvironment(this IDistributedApplicationBuilder builder)
    {
        return builder.Resources.OfType<TerraformEnvironmentResource>().FirstOrDefault();
    }

    internal static string CreateDefaultAzureEnvironmentName(this IDistributedApplicationBuilder builder)
    {
        // Use ProjectNameSha256 for stable naming across deployments
        var applicationHash = builder.Configuration["AppHost:ProjectNameSha256"]?[..5].ToLowerInvariant();
        return $"tf{applicationHash}";
    }
}
