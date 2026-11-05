using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Zemires.Aspire.Hosting.N8n;

/// <summary>
/// Creates an API key for n8n's public API (<c>/api/v1</c>). n8n has no supported way to create API keys from code, so this
/// signs in as the instance owner and uses the internal REST API of the n8n editor (<c>/rest/login</c>, <c>/rest/api-keys</c>).
/// That API is undocumented and may change between n8n versions.
/// </summary>
/// <param name="httpClient">A client with the n8n base address that does not handle cookies itself.</param>
/// <param name="logger">The logger.</param>
internal sealed class N8nApiKeyProvisioner(HttpClient httpClient, ILogger logger)
{
    internal const string ApiKeyHeader = "X-N8N-API-KEY";

    // n8n binds the session cookie to the browser-id header sent at login; later calls must send the same value.
    private const string BrowserId = "zemires-aspire-hosting-n8n";

    /// <summary>Checks whether n8n accepts <paramref name="apiKey"/> for its public API.</summary>
    public async Task<bool> IsValidAsync(string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/workflows?limit=1");
        request.Headers.Add(ApiKeyHeader, apiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return false;
        }

        await EnsureSuccessAsync(response, "check the n8n API key", cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Signs in as the owner, deletes the owner's keys labeled <paramref name="label"/> (their value is lost) and creates
    /// a non-expiring key with all scopes. Returns its raw value, which n8n shows only once.
    /// </summary>
    public async Task<string> CreateApiKeyAsync(string email, string password, string label, CancellationToken cancellationToken)
    {
        var sessionCookie = await LoginAsync(email, password, cancellationToken).ConfigureAwait(false);
        await DeleteApiKeysAsync(sessionCookie, label, cancellationToken).ConfigureAwait(false);
        var scopes = await GetScopesAsync(sessionCookie, cancellationToken).ConfigureAwait(false);

        using var request = CreateSessionRequest(HttpMethod.Post, "/rest/api-keys", sessionCookie);
        request.Content = JsonContent.Create(new { label, scopes, expiresAt = (long?)null });

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "create the n8n API key", cancellationToken).ConfigureAwait(false);

        var data = (await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken).ConfigureAwait(false))?["data"];
        var apiKey = data?["rawApiKey"]?.GetValue<string>();
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("n8n created the API key but returned no rawApiKey.");
        }

        logger.LogInformation("Created n8n API key '{Label}' with {ScopeCount} scopes.", label, scopes.Count);
        return apiKey;
    }

    /// <summary>Signs in to n8n and returns the session cookie (<c>n8n-auth=...</c>).</summary>
    private async Task<string> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/rest/login")
        {
            Content = JsonContent.Create(new { emailOrLdapLoginId = email, password })
        };
        request.Headers.Add("browser-id", BrowserId);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "sign in to n8n", cancellationToken).ConfigureAwait(false);

        var cookie = response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Select(c => c.Split(';', 2)[0]).FirstOrDefault(c => c.StartsWith("n8n-auth=", StringComparison.Ordinal))
            : null;
        return cookie ?? throw new InvalidOperationException("n8n login succeeded but returned no n8n-auth cookie.");
    }

    private async Task DeleteApiKeysAsync(string sessionCookie, string label, CancellationToken cancellationToken)
    {
        // The list is paged ({ data: { items: [...] } }) and the label filter is a substring match, so compare exactly below.
        using var listRequest = CreateSessionRequest(HttpMethod.Get,
            $"/rest/api-keys?ownership=mine&take=250&label={Uri.EscapeDataString(label)}", sessionCookie);
        using var listResponse = await httpClient.SendAsync(listRequest, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(listResponse, "list n8n API keys", cancellationToken).ConfigureAwait(false);

        var body = await listResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken).ConfigureAwait(false);
        var data = body?["data"];
        // older n8n versions return the keys as array, newer ones as page
        var keys = (data is JsonArray array ? array : data?["items"]?.AsArray()) ?? [];

        foreach (var key in keys.Where(key => key?["label"]?.GetValue<string>() == label))
        {
            var id = key!["id"]!.GetValue<string>();
            using var deleteRequest = CreateSessionRequest(HttpMethod.Delete, $"/rest/api-keys/{Uri.EscapeDataString(id)}", sessionCookie);
            using var deleteResponse = await httpClient.SendAsync(deleteRequest, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(deleteResponse, $"delete n8n API key {id}", cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Deleted n8n API key {ApiKeyId} ('{Label}'), its value is unknown.", id, label);
        }
    }

    private async Task<IReadOnlyList<string>> GetScopesAsync(string sessionCookie, CancellationToken cancellationToken)
    {
        using var request = CreateSessionRequest(HttpMethod.Get, "/rest/api-keys/scopes", sessionCookie);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "read n8n API key scopes", cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken).ConfigureAwait(false);
        return body?["data"]?.AsArray().Select(scope => scope!.GetValue<string>()).ToArray() ?? [];
    }

    private static HttpRequestMessage CreateSessionRequest(HttpMethod method, string path, string sessionCookie)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("browser-id", BrowserId);
        request.Headers.Add("Cookie", sessionCookie);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new HttpRequestException($"Failed to {action}: {(int)response.StatusCode} {response.ReasonPhrase} {body}",
            inner: null, response.StatusCode);
    }
}
