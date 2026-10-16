using Zitadel.Management.V1;

// ReSharper disable once CheckNamespace
namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A Zitadel project, created when its Zitadel resource is ready.
/// </summary>
/// <param name="name">The resource name.</param>
/// <param name="projectRequest">The project creation request.</param>
/// <param name="parent">The Zitadel resource.</param>
public class ZitadelProjectResource(string name, AddProjectRequest projectRequest, ZitadelResource parent)
    : Resource(name), IResourceWithParent<ZitadelResource>, IResourceWithWaitSupport
{
    /// <summary>
    /// Gets the project creation request.
    /// </summary>
    public AddProjectRequest ProjectRequest { get; } = projectRequest;

    /// <summary>
    /// Gets or sets the project id, set once the project exists.
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// Gets the Zitadel resource.
    /// </summary>
    public ZitadelResource Parent { get; } = parent;
}
