namespace Zemires.Aspire.Hosting.Terraform.Models;

/// <summary>
/// Template resource that includes connection string information.
/// </summary>
public class TemplateResourceWithConnectionString : TemplateResource
{
    /// <summary>
    /// Gets or sets the connection string for the resource.
    /// </summary>
    public string? ConnectionString { get; set; }
}
