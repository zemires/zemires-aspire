using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Zemires.Aspire.Hosting.Terraform;

/// <summary>
/// Defines a contract for publishing distributed application models as Terraform templates.
/// </summary>
public interface ITerraformTemplatePublisher
{
    /// <summary>
    /// Collects the Terraform template annotations of all resources in the application model.
    /// </summary>
    /// <param name="model">The distributed application model.</param>
    /// <returns>The template annotations to publish.</returns>
    Task<List<ITerraformTemplateAnnotation>> GetTerraformTemplateAnnotations(DistributedApplicationModel model);

    /// <summary>
    /// Prepares the output directory and template location before individual templates are published.
    /// </summary>
    /// <param name="templatesLocation">The directory or URL containing the Handlebars templates.</param>
    /// <param name="terraformAnnotations">The template annotations that will be published.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task InitPublishing(string templatesLocation, List<ITerraformTemplateAnnotation> terraformAnnotations);
    /// <summary>
    /// Renders a single template annotation and writes the resulting Terraform file to the output directory.
    /// </summary>
    /// <param name="terraformTemplateAnnotation">The template annotation to render.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task PublishTemplateAsync(ITerraformTemplateAnnotation terraformTemplateAnnotation);

    /// <summary>
    /// Prepares the resources in the distributed application model before publishing templates.
    /// </summary>
    /// <param name="model"></param>
    /// <returns></returns>
    Task PrepareResources(DistributedApplicationModel model);
}
