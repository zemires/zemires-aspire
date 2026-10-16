using Aspire.Hosting.ApplicationModel;
using Zemires.Aspire.Hosting.Terraform.Models;

// ReSharper disable once CheckNamespace
namespace Aspire.Hosting;

/// <summary>
/// Extension methods for attaching custom Terraform templates and parameters to resources.
/// </summary>
public static class TerraformResourceExtensions
{
    /// <summary>
    /// Renders the project with a custom Handlebars template. The template receives a <c>ProjectTemplateResource</c>
    /// (environment, arguments, bindings, replicas, image).
    /// </summary>
    /// <param name="resource">The project resource builder.</param>
    /// <param name="templatePath">The template file, relative to the templates location.</param>
    /// <param name="outputFileName">The output file name. <c>{resource}.tf</c> when <see langword="null"/>.</param>
    /// <param name="appendFile">Whether to append to an existing output file.</param>
    /// <param name="parameters">Additional template parameters.</param>
    /// <returns>The resource builder.</returns>
    /// <example>
    /// <code lang="csharp">
    /// builder.AddProject&lt;Projects.WebApi&gt;("webapi").WithTerraformTemplate("container-app.tf.hbs");
    /// </code>
    /// </example>
    [AspireExport("withProjectTerraformTemplate", MethodName = "withTerraformTemplate")]
    public static IResourceBuilder<ProjectResource> WithTerraformTemplate(this IResourceBuilder<ProjectResource> resource, string templatePath, string? outputFileName = null,
        bool appendFile = false, Dictionary<string, object?>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrEmpty(templatePath);

        return resource.WithAnnotation(new TerraformTemplateAnnotation<ProjectTemplateResource>
        {
            TemplateFile = templatePath,
            OutputFile = outputFileName,
            AppendFile = appendFile,
            Parameters = parameters
        });
    }

    /// <summary>
    /// Renders the container with a custom Handlebars template. The template receives a <c>ContainerTemplateResource</c>
    /// (image, environment, arguments, bindings, volumes, entrypoint, replicas).
    /// </summary>
    /// <param name="resource">The container resource builder.</param>
    /// <param name="templateFile">The template file, relative to the templates location.</param>
    /// <param name="outputFileName">The output file name. <c>{resource}.tf</c> when <see langword="null"/>.</param>
    /// <param name="appendFile">Whether to append to an existing output file.</param>
    /// <returns>The resource builder.</returns>
    /// <example>
    /// <code lang="csharp">
    /// builder.AddContainer("redis", "redis:7-alpine").WithTerraformTemplate("redis-cache.tf.hbs");
    /// </code>
    /// </example>
    [AspireExport("withContainerTerraformTemplate", MethodName = "withTerraformTemplate")]
    public static IResourceBuilder<ContainerResource> WithTerraformTemplate(this IResourceBuilder<ContainerResource> resource, string templateFile, string? outputFileName = null,
        bool appendFile = false)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrEmpty(templateFile);

        return resource.WithAnnotation(new TerraformTemplateAnnotation<ContainerTemplateResource>
        {
            TemplateFile = templateFile,
            OutputFile = outputFileName,
            AppendFile = appendFile
        });
    }

    /// <summary>
    /// Adds a parameter to the template model of the resource (<c>Parameters.{parameterName}</c>).
    /// </summary>
    /// <typeparam name="TDestination">The resource type.</typeparam>
    /// <param name="resource">The resource builder.</param>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="value">The parameter value.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<TDestination> WithTerraformTemplateParameter<TDestination>(this IResourceBuilder<TDestination> resource, string parameterName, object? value)
        where TDestination : IResource
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrEmpty(parameterName);

        return resource.WithAnnotation(new TerraformTemplateParameterAnnotation
        {
            Name = parameterName,
            Value = value
        });
    }
}
