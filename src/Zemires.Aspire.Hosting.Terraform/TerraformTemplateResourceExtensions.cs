using Aspire.Hosting.ApplicationModel;

#pragma warning disable IDE0130
namespace Aspire.Hosting;
#pragma warning restore IDE0130

/// <summary>
/// Extension methods for <see cref="TerraformTemplateResource"/> builders.
/// </summary>
public static class TerraformTemplateResourceExtensions
{
    /// <summary>
    /// Gets a reference to an output of the template (<c>local.{template}.{name}</c>), resolved after deployment.
    /// </summary>
    /// <param name="builder">The template resource builder.</param>
    /// <param name="name">The output name.</param>
    /// <returns>The output reference.</returns>
    [AspireExportIgnore(Reason = "Returns TerraformOutputReference, which is not ATS-compatible; use GetTerraformOutput instead.")]
    public static TerraformOutputReference GetOutput(this IResourceBuilder<TerraformTemplateResource> builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        builder.Resource.AddOutput(name);
        return new TerraformOutputReference(name, builder.Resource);
    }

    /// <summary>
    /// Gets a reference to a secret output of the template (<c>local.{template}.{name}</c>), resolved after deployment.
    /// </summary>
    /// <param name="builder">The template resource builder.</param>
    /// <param name="name">The output name.</param>
    /// <returns>The output reference.</returns>
    [AspireExportIgnore(Reason = "Returns TerraformSecretOutputReference, which is not ATS-compatible.")]
    public static TerraformSecretOutputReference GetSecretOutput(this IResourceBuilder<TerraformTemplateResource> builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        builder.Resource.AddOutput(name);
        return new TerraformSecretOutputReference(name, builder.Resource);
    }

    /// <summary>
    /// Adds a template parameter (<c>Parameters.{parameterName}</c>).
    /// </summary>
    /// <param name="builder">The template resource builder.</param>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="value">The parameter value.</param>
    /// <returns>The template resource builder.</returns>
    [AspireExport("withTerraformTemplateResourceParameter", MethodName = "withParameter")]
    public static IResourceBuilder<TerraformTemplateResource> WithParameter(this IResourceBuilder<TerraformTemplateResource> builder, string parameterName, object? value) =>
        builder.WithTerraformTemplateParameter(parameterName, value);
}
