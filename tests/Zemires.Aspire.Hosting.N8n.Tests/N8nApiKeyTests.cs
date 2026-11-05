using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Aspire.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Zemires.Aspire.Hosting.N8n.Tests;

public class N8nApiKeyTests
{
    [Fact]
    public async Task WithApiKey_AddsKeyToConnectionString()
    {
        var builder = DistributedApplication.CreateBuilder();
        var n8n = builder.AddN8n("n8n").WithInstanceOwner("admin@dev.local", "Admin", "Local").WithApiKey();

        var apiKey = Assert.IsType<N8nApiKeyReference>(n8n.Resource.ApiKey);
        Assert.Equal("Aspire AppHost", apiKey.Label);
        Assert.Null(apiKey.Parameter);
        Assert.Equal("Parameters:n8n-api-key", apiKey.ConfigurationKey);
        Assert.Equal("{n8n.outputs.api-key}", apiKey.ValueExpression);
        Assert.StartsWith("Endpoint=", n8n.Resource.ConnectionStringExpression.ValueExpression);
        Assert.EndsWith(";ApiKey={n8n.outputs.api-key}", n8n.Resource.ConnectionStringExpression.ValueExpression);

        var properties = ((IResourceWithConnectionString)n8n.Resource).GetConnectionProperties().Select(p => p.Key);
        Assert.Contains("ApiKey", properties);

        // resolved lazily
        var pending = apiKey.GetValueAsync(TestContext.Current.CancellationToken).AsTask();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(pending.IsCompleted);
        apiKey.Set("key-1");
        Assert.Equal("key-1", await pending);

        // restart: replaced
        apiKey.Set("key-2");
        Assert.Equal("key-2", await apiKey.GetValueAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WithApiKey_ResolvedWithoutCaller_DoesNotWait()
    {
        // Aspire resolves the connection string without a caller before starting n8n; waiting would deadlock the start.
        var builder = DistributedApplication.CreateBuilder();
        var n8n = builder.AddN8n("n8n").WithInstanceOwner("admin@dev.local", "Admin", "Local").WithApiKey();
        var apiKey = n8n.Resource.ApiKey!;
        var expression = ReferenceExpression.Create($"ApiKey={apiKey}");

        Assert.Equal("ApiKey=", await expression.GetValueAsync(TestContext.Current.CancellationToken));
        Assert.Null(await apiKey.GetValueAsync(new ValueProviderContext(), TestContext.Current.CancellationToken));

        // a consuming resource waits for the key
        var consumer = builder.AddContainer("consumer", "image").Resource;
        var pending = expression.GetValueAsync(new ValueProviderContext { Caller = consumer }, TestContext.Current.CancellationToken).AsTask();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(pending.IsCompleted);

        apiKey.Set("key-1");
        Assert.Equal("ApiKey=key-1", await pending);
        Assert.Equal("ApiKey=key-1", await expression.GetValueAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void WithoutApiKey_ConnectionStringIsUrl()
    {
        var builder = DistributedApplication.CreateBuilder();
        var n8n = builder.AddN8n("n8n");

        Assert.Null(n8n.Resource.ApiKey);
        Assert.Equal(n8n.Resource.UriExpression.ValueExpression, n8n.Resource.ConnectionStringExpression.ValueExpression);
        Assert.DoesNotContain("ApiKey", ((IResourceWithConnectionString)n8n.Resource).GetConnectionProperties().Select(p => p.Key));
    }

    [Fact]
    public async Task WithApiKey_Parameter_UsesParameterValue()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.Configuration["Parameters:key"] = "secret-key";
        var n8n = builder.AddN8n("n8n").WithApiKey(builder.AddParameter("key", secret: true));

        Assert.Equal("{key.value}", n8n.Resource.ApiKey!.ValueExpression);
        Assert.Equal("secret-key", await n8n.Resource.ApiKey.GetValueAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void WithApiKey_Twice_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();
        var n8n = builder.AddN8n("n8n").WithApiKey();

        Assert.Throws<InvalidOperationException>(() => n8n.WithApiKey("other"));
    }

    [Fact]
    public void WithApiKey_PublishMode_DoesNothing()
    {
        var builder = DistributedApplication.CreateBuilder(["--publisher", "manifest", "--output-path", Path.GetTempPath()]);
        var n8n = builder.AddN8n("n8n").WithInstanceOwner("admin@dev.local", "Admin", "Local").WithApiKey();

        Assert.True(builder.ExecutionContext.IsPublishMode);
        Assert.Null(n8n.Resource.ApiKey);
    }

    [Fact]
    public async Task Provisioner_SignsIn_DeletesOldKeys_AndCreatesKeyWithAllScopes()
    {
        var handler = new FakeN8nHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://n8n.test") };
        var provisioner = new N8nApiKeyProvisioner(httpClient, NullLogger.Instance);

        var key = await provisioner.CreateApiKeyAsync("admin@dev.local", "pw", "Aspire AppHost", TestContext.Current.CancellationToken);

        Assert.Equal("raw-key", key);
        Assert.Equal(["POST /rest/login", "GET /rest/api-keys", "DELETE /rest/api-keys/1", "GET /rest/api-keys/scopes", "POST /rest/api-keys"],
            handler.Requests.Select(r => $"{r.Method} {r.Path}"));
        Assert.All(handler.Requests.Skip(1), r => Assert.Equal("n8n-auth=session", r.Cookie));
        Assert.All(handler.Requests, r => Assert.Equal("zemires-aspire-hosting-n8n", r.BrowserId));

        var login = JsonNode.Parse(handler.Requests[0].Body!)!;
        Assert.Equal("admin@dev.local", login["emailOrLdapLoginId"]!.GetValue<string>());
        Assert.Equal("pw", login["password"]!.GetValue<string>());

        var create = JsonNode.Parse(handler.Requests[^1].Body!)!;
        Assert.Equal("Aspire AppHost", create["label"]!.GetValue<string>());
        Assert.Equal(["workflow:read", "workflow:list"], create["scopes"]!.AsArray().Select(s => s!.GetValue<string>()));
        Assert.Null(create["expiresAt"]);
    }

    [Fact]
    public async Task Provisioner_LoginFails_Throws()
    {
        var handler = new FakeN8nHandler { LoginStatus = HttpStatusCode.Unauthorized };
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://n8n.test") };
        var provisioner = new N8nApiKeyProvisioner(httpClient, NullLogger.Instance);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            provisioner.CreateApiKeyAsync("admin@dev.local", "wrong", "label", TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task Provisioner_WaitUntilReady_RetriesUntilRestApiIsRegistered()
    {
        // n8n passes /healthz before its REST routes exist; they answer 404 until then.
        var handler = new FakeN8nHandler { RestNotFoundCount = 2 };
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://n8n.test") };
        var provisioner = new N8nApiKeyProvisioner(httpClient, NullLogger.Instance) { RetryDelay = TimeSpan.FromMilliseconds(1) };

        await provisioner.WaitUntilReadyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, handler.Requests.Count(r => r is { Method.Method: "GET", Path: "/rest/login" }));
    }

    [Fact]
    public async Task Provisioner_WaitUntilReady_TimesOut()
    {
        var handler = new FakeN8nHandler { RestNotFoundCount = int.MaxValue };
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://n8n.test") };
        var provisioner = new N8nApiKeyProvisioner(httpClient, NullLogger.Instance)
        {
            RetryDelay = TimeSpan.FromMilliseconds(1),
            ReadyTimeout = TimeSpan.FromMilliseconds(50)
        };

        await Assert.ThrowsAsync<TimeoutException>(() => provisioner.WaitUntilReadyAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public async Task Provisioner_IsValid_ChecksPublicApi(HttpStatusCode status, bool expected)
    {
        var handler = new FakeN8nHandler { PublicApiStatus = status };
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://n8n.test") };
        var provisioner = new N8nApiKeyProvisioner(httpClient, NullLogger.Instance);

        Assert.Equal(expected, await provisioner.IsValidAsync("key", TestContext.Current.CancellationToken));
        Assert.Equal("key", handler.Requests.Single().ApiKey);
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string? Cookie, string? BrowserId, string? ApiKey, string? Body);

    private sealed class FakeN8nHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        public HttpStatusCode LoginStatus { get; init; } = HttpStatusCode.OK;

        public HttpStatusCode PublicApiStatus { get; init; } = HttpStatusCode.OK;

        public int RestNotFoundCount { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(new RecordedRequest(request.Method, path,
                Header(request, "Cookie"), Header(request, "browser-id"), Header(request, "X-N8N-API-KEY"),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));

            switch (request.Method.Method, path)
            {
                case ("GET", "/rest/login"):
                    return new HttpResponseMessage(RestNotFoundCount-- > 0 ? HttpStatusCode.NotFound : HttpStatusCode.Unauthorized);
                case ("POST", "/rest/login"):
                    var login = new HttpResponseMessage(LoginStatus) { Content = JsonContent.Create(new { data = new { } }) };
                    login.Headers.Add("Set-Cookie", "other=1; Path=/");
                    login.Headers.Add("Set-Cookie", "n8n-auth=session; Path=/; HttpOnly");
                    return login;
                case ("GET", "/rest/api-keys"):
                    return Json(new { data = new { items = new[] { new { id = "1", label = "Aspire AppHost" }, new { id = "2", label = "Aspire AppHost 2" } } } });
                case ("DELETE", _):
                    return Json(new { data = true });
                case ("GET", "/rest/api-keys/scopes"):
                    return Json(new { data = new[] { "workflow:read", "workflow:list" } });
                case ("POST", "/rest/api-keys"):
                    return Json(new { data = new { id = "3", rawApiKey = "raw-key" } });
                case ("GET", "/api/v1/workflows"):
                    return new HttpResponseMessage(PublicApiStatus) { Content = JsonContent.Create(new { data = Array.Empty<object>() }) };
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        private static string? Header(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
}
