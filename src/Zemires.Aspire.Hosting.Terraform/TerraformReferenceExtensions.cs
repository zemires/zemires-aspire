using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// Extension methods creating Terraform references (<c>${local...}</c>, <c>${output...}</c>) to a resource, for use in templates.
/// </summary>
public static class TerraformReferenceExtensions
{
    /// <summary>
    /// Creates the reference <c>${local.{resource}.{name}}</c>.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="name">The local value name.</param>
    /// <returns>The reference expression.</returns>
    [AspireExport]
    public static ReferenceExpression GetTerraformLocal(this IResource resource, string name)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrEmpty(name);

        return ReferenceExpression.Create($"${{local.{resource.Name}.{name}}}");
    }

    /// <summary>
    /// Creates the reference <c>${output.{resource}.{name}}</c>.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="name">The output name.</param>
    /// <returns>The reference expression.</returns>
    [AspireExport]
    public static ReferenceExpression GetTerraformOutput(this IResource resource, string name)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrEmpty(name);

        return ReferenceExpression.Create($"${{output.{resource.Name}.{name}}}");
    }
}
