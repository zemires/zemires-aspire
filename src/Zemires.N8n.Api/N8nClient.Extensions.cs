using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Zemires.N8n.Api;

public partial class N8nClient
{
    private const string ApiPath = "/api/v1";

    /// <summary>
    /// Creates a client for the n8n instance at <paramref name="baseUrl"/>.
    /// </summary>
    /// <param name="baseUrl">The n8n base URL, without <c>/api/v1</c>.</param>
    /// <param name="apiKey">The API key sent as <c>X-N8N-API-KEY</c>; none when empty.</param>
    /// <param name="httpClient">The HTTP client to use; a new one when <see langword="null"/>.</param>
    public N8nClient(string baseUrl, string? apiKey, HttpClient? httpClient = null)
        : this(new HttpClientRequestAdapter(new N8nAuthenticationProvider(apiKey), httpClient: httpClient))
    {
        RequestAdapter.BaseUrl = $"{baseUrl.TrimEnd('/')}{ApiPath}";
    }

    /// <summary>
    /// Calls the n8n health endpoint <c>/healthz</c>.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if n8n responds with a success status code.</returns>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        var baseUrl = RequestAdapter.BaseUrl ?? "";
        if (baseUrl.EndsWith(ApiPath, StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl[..^ApiPath.Length];
        }

        try
        {
            // /healthz is served at the root, not below /api/v1
            var requestInfo = new RequestInformation
            {
                HttpMethod = Method.GET,
                URI = new Uri($"{baseUrl}/healthz"),
            };
            await RequestAdapter.SendNoContentAsync(requestInfo, cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
