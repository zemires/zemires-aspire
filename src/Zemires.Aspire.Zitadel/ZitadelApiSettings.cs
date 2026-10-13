namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Settings for the Zitadel API clients registered by <c>AddZitadelApi</c> / <c>AddKeyedZitadelApi</c>.
/// </summary>
public class ZitadelApiSettings
{
    /// <summary>
    /// Gets or sets the base URI of the Zitadel instance. Overridden by <c>ConnectionStrings:{name}</c> when present.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets a personal access token (PAT) of a machine user used to authenticate the API calls.
    /// </summary>
    public string? PersonalAccessToken { get; set; }

    /// <summary>
    /// Gets or sets the JSON key of a machine user (service account), either as plain JSON or base64 encoded JSON.
    /// </summary>
    public string? ServiceAccountKey { get; set; }

    /// <summary>
    /// Gets or sets the path to the JSON key file of a machine user (service account).
    /// </summary>
    public string? ServiceAccountKeyPath { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the Zitadel health check is disabled.
    /// </summary>
    public bool DisableHealthChecks { get; set; }

    /// <summary>
    /// Gets or sets the health check timeout in milliseconds. When <see langword="null"/> the default timeout is used.
    /// </summary>
    public int? HealthCheckTimeout { get; set; }

    internal void ParseConnectionString(string connectionString)
    {
        if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
        {
            Endpoint = uri;
            return;
        }

        var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString };

        if (builder.TryGetValue(nameof(Endpoint), out var endpoint) && endpoint is string endpointValue)
        {
            Endpoint = new Uri(endpointValue);
        }

        if (builder.TryGetValue(nameof(PersonalAccessToken), out var pat) && pat is string patValue)
        {
            PersonalAccessToken = patValue;
        }

        if (builder.TryGetValue(nameof(ServiceAccountKey), out var key) && key is string keyValue && keyValue.Length > 0)
        {
            ServiceAccountKey = keyValue;
        }

        if (builder.TryGetValue(nameof(ServiceAccountKeyPath), out var keyPath) && keyPath is string keyPathValue)
        {
            ServiceAccountKeyPath = keyPathValue;
        }
    }
}
