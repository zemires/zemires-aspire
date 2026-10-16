namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// An n8n external task runner connected to the broker of its parent.
/// </summary>
/// <param name="name">The resource name.</param>
/// <param name="parent">The n8n resource the runner connects to.</param>
public class N8nTaskRunnerResource(string name, N8nResource parent) : ContainerResource(name), IResourceWithParent<N8nResource>
{
    /// <summary>
    /// Gets the n8n resource the runner connects to.
    /// </summary>
    public N8nResource Parent { get; } = parent;
}
