using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zemires.Aspire.Hosting.Terraform;

#pragma warning disable ASPIREPIPELINES001
#pragma warning disable IDE0130

// ReSharper disable once CheckNamespace
namespace Aspire.Hosting;

/// <summary>
/// Extension methods for publishing the application model as Terraform configuration.
/// </summary>
public static class TerraformBuilderExtensions
{
    private const string DefaultEnvironmentName = "tf";

    /// <summary>
    /// Registers the Terraform template publisher. Options are bound from <c>Terraform:Templates</c>.
    /// Called by <see cref="AddTerraformEnvironment"/>; call it directly only to configure the options.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="configureOptions">Configures the publishing options.</param>
    /// <returns>The builder.</returns>
    /// <example>
    /// <code lang="csharp">
    /// builder.AddTerraformTemplatePublishing(options => options.FilePrefix = "app-");
    /// </code>
    /// </example>
    [AspireExportIgnore(Reason = "Configured through a TerraformTemplatePublishingOptions callback, which is not ATS-compatible; AddTerraformEnvironment registers publishing with defaults.")]
    public static IDistributedApplicationBuilder AddTerraformTemplatePublishing(this IDistributedApplicationBuilder builder, Action<TerraformTemplatePublishingOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddTerraformServices(configureOptions);
        builder.Services.TryAddSingleton<ITerraformTemplatePublisher, TerraformTemplatePublisher>();

        return builder;
    }

    /// <summary>
    /// Adds a Terraform template to the default Terraform environment (created as <c>tf</c> if none exists).
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name.</param>
    /// <param name="terraformFile">The Handlebars template file.</param>
    /// <param name="outputFile">The output file name. Derived from the template when <see langword="null"/>.</param>
    /// <param name="appendFile">Whether to append to an existing output file.</param>
    /// <returns>The template resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<TerraformTemplateResource> AddTerraformTemplate(this IDistributedApplicationBuilder builder, [ResourceName] string name, string terraformFile, string? outputFile = null, bool appendFile = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.GetOrAddDefaultEnvironment().AddTerraformTemplate(name, terraformFile, outputFile, appendFile);
    }

    /// <summary>
    /// Adds a Terraform template given as string to the default Terraform environment (created as <c>tf</c> if none exists).
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name.</param>
    /// <param name="terraformString">The Handlebars template content.</param>
    /// <param name="outputFile">The output file name. Derived from the resource name when <see langword="null"/>.</param>
    /// <param name="appendFile">Whether to append to an existing output file.</param>
    /// <returns>The template resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<TerraformTemplateResource> AddTerraformTemplateString(this IDistributedApplicationBuilder builder, [ResourceName] string name, string terraformString, string? outputFile = null, bool appendFile = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.GetOrAddDefaultEnvironment().AddTerraformTemplateString(name, terraformString, outputFile, appendFile);
    }

    /// <summary>
    /// Adds a Terraform environment: publishing renders the templates, deploying applies them (pipeline steps
    /// <c>prepare-</c>, <c>publish-</c>, <c>deploy-</c> and <c>destroy-terraform-{name}</c>).
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name.</param>
    /// <param name="templatesLocation">The directory or URL of the Handlebars templates.</param>
    /// <returns>The environment resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<TerraformEnvironmentResource> AddTerraformEnvironment(this IDistributedApplicationBuilder builder, [ResourceName] string name, string templatesLocation = "./templates")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(templatesLocation);

        builder.AddTerraformTemplatePublishing();

        return builder.AddEnvironmentResource(new TerraformEnvironmentResource(name, templatesLocation));
    }

    /// <summary>
    /// Registers the options and services shared by all Terraform publishers.
    /// </summary>
    internal static void AddTerraformServices(this IDistributedApplicationBuilder builder, Action<TerraformTemplatePublishingOptions>? configureOptions)
    {
        if (!builder.Services.Any(d => d.ServiceType == typeof(TerraformExecutor)))
        {
            builder.Services.AddOptions<TerraformTemplatePublishingOptions>()
                .Bind(builder.Configuration.GetSection("Terraform:Templates"));
            builder.Services.AddTransient<TerraformTemplateProcessor>();
            builder.Services.AddTransient<TerraformExecutor>();
        }

        if (configureOptions is not null)
        {
            builder.Services.Configure(configureOptions);
        }
    }

    /// <summary>
    /// Adds an environment resource, hidden in the dashboard and excluded from the manifest.
    /// </summary>
    internal static IResourceBuilder<T> AddEnvironmentResource<T>(this IDistributedApplicationBuilder builder, T resource) where T : TerraformEnvironmentResource =>
        builder.AddResource(resource)
            .ExcludeFromManifest()
            .WithInitialState(new()
            {
                ResourceType = typeof(T).Name,
                Properties = [],
                IsHidden = true
            });

    internal static TerraformEnvironmentResource? GetDefaultEnvironment(this IDistributedApplicationBuilder builder) =>
        builder.Resources.OfType<TerraformEnvironmentResource>().FirstOrDefault();

    private static IResourceBuilder<TerraformEnvironmentResource> GetOrAddDefaultEnvironment(this IDistributedApplicationBuilder builder) =>
        builder.GetDefaultEnvironment() is { } environment
            ? builder.CreateResourceBuilder(environment)
            : builder.AddTerraformEnvironment(DefaultEnvironmentName);
}
