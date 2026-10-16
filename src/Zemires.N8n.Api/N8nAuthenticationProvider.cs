using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

namespace Zemires.N8n.Api;

/// <summary>
/// Adds the n8n API key as <c>X-N8N-API-KEY</c> header.
/// </summary>
/// <param name="apiKey">The API key; no header is sent when empty.</param>
public class N8nAuthenticationProvider(string? apiKey) : IAuthenticationProvider
{
    /// <summary>
    /// Gets the API key.
    /// </summary>
    public string? ApiKey { get; } = apiKey;

    /// <inheritdoc />
    public Task AuthenticateRequestAsync(RequestInformation request, Dictionary<string, object>? additionalAuthenticationContext = null, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(ApiKey))
        {
            request.Headers.Add("X-N8N-API-KEY", ApiKey);
        }
        return Task.CompletedTask;
    }
}
