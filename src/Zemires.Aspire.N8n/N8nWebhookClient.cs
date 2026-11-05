using System.Net.Http.Json;
using System.Text.Json;

namespace Zemires.Aspire.N8n;

/// <summary>
/// Starts n8n workflows through their Webhook trigger (<c>/webhook/{path}</c>), which the public API cannot do.
/// Registered by <c>AddN8nClient</c> / <c>AddKeyedN8nClient</c> with the configured endpoint.
/// </summary>
/// <param name="httpClient">The HTTP client with the n8n endpoint as base address.</param>
public sealed class N8nWebhookClient(HttpClient httpClient)
{
    private const string ProductionPath = "webhook";
    private const string TestPath = "webhook-test";

    /// <summary>
    /// Gets the HTTP client, e.g. for other HTTP methods or content types.
    /// </summary>
    public HttpClient HttpClient { get; } = httpClient;

    /// <summary>
    /// Gets the URI of a webhook, relative to the n8n endpoint.
    /// </summary>
    /// <param name="path">The webhook path set in the Webhook trigger node.</param>
    /// <param name="test">Whether to use the test URL (<c>/webhook-test/{path}</c>) of a workflow open in the editor.</param>
    /// <returns>The relative webhook URI.</returns>
    public static Uri GetWebhookUri(string path, bool test = false)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new Uri($"{(test ? TestPath : ProductionPath)}/{path.TrimStart('/')}", UriKind.Relative);
    }

    /// <summary>
    /// Posts <paramref name="payload"/> as JSON to a webhook and returns the response without checking its status.
    /// </summary>
    /// <param name="path">The webhook path set in the Webhook trigger node.</param>
    /// <param name="payload">The JSON body. No body when <see langword="null"/>.</param>
    /// <param name="test">Whether to use the test URL (<c>/webhook-test/{path}</c>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The response; dispose it.</returns>
    public Task<HttpResponseMessage> PostAsync(string path, object? payload = null, bool test = false, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, GetWebhookUri(path, test))
        {
            Content = payload is null ? null : JsonContent.Create(payload, payload.GetType())
        };
        return SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Posts <paramref name="payload"/> as JSON to a webhook and reads the JSON response.
    /// </summary>
    /// <typeparam name="TResponse">The response type, e.g. <see cref="JsonElement"/>.</typeparam>
    /// <param name="path">The webhook path set in the Webhook trigger node.</param>
    /// <param name="payload">The JSON body. No body when <see langword="null"/>.</param>
    /// <param name="test">Whether to use the test URL (<c>/webhook-test/{path}</c>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The response, or <see langword="default"/> when n8n answers without JSON body.</returns>
    /// <exception cref="HttpRequestException">n8n answers with an error status code.</exception>
    public async Task<TResponse?> TriggerAsync<TResponse>(string path, object? payload = null, bool test = false, CancellationToken cancellationToken = default)
    {
        using var response = await PostAsync(path, payload, test, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException($"n8n webhook '{path}' failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}",
                inner: null, response.StatusCode);
        }

        return response.Content.Headers.ContentType?.MediaType == "application/json"
            ? await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken).ConfigureAwait(false)
            : default;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            return await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
