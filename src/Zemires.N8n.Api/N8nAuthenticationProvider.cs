using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

namespace Zemires.N8n.Api;

public class N8nAuthenticationProvider(string? apiKey) : IAuthenticationProvider
{
    public string? ApiKey { get; } = apiKey;

    public Task AuthenticateRequestAsync(RequestInformation request, Dictionary<string, object>? additionalAuthenticationContext = null, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(ApiKey))
        {
            request.Headers.Add("X-N8N-API-KEY", ApiKey);
        }
        return Task.CompletedTask;
    }
}
