namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Settings for the n8n client registered by <c>AddN8nClient</c> / <c>AddKeyedN8nClient</c>.
/// </summary>
public class N8nClientSettings
{
    /// <summary>
    /// Gets or sets the base URI of the n8n instance. Overridden by <c>ConnectionStrings:{name}</c> when present.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the n8n API key sent as the <c>X-N8N-API-KEY</c> header. When empty, no key is sent.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the n8n health check is disabled.
    /// </summary>
    public bool DisableHealthChecks { get; set; }

    /// <summary>
    /// Gets or sets the health check timeout in milliseconds. When <see langword="null"/> the default timeout is used.
    /// </summary>
    public int? HealthCheckTimeout { get; set; }
}
