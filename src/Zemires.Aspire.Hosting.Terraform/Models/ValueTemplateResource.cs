namespace Zemires.Aspire.Hosting.Terraform.Models;

/// <summary>
/// Template resource for value-based resources.
/// </summary>
public class ValueTemplateResource : TemplateResourceWithConnectionString
{
    /// <summary>
    /// Gets or sets value.
    /// </summary>
    public string Value { get; set; } = null!;
}
