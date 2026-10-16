namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// An n8n queue-mode worker. Shares the encryption key of its parent.
/// </summary>
/// <param name="name">The resource name.</param>
/// <param name="parent">The main n8n resource.</param>
public class N8nWorkerResource(string name, N8nResource parent) : N8nResource(name, parent.EncryptionKeyParameter), IResourceWithParent<N8nResource>
{
    /// <summary>
    /// Gets the main n8n resource.
    /// </summary>
    public N8nResource Parent { get; } = parent;
}
