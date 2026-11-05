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
    public async Task WithApiKey_WithoutCaller_DoesNotWaitForKey()
    {
        // Aspire resolves the connection string without caller before it creates the n8n container; waiting would deadlock.
        var builder = DistributedApplication.CreateBuilder();
        var n8n = AddN8nWithAllocatedEndpoint(builder);
        var apiKey = n8n.Resource.ApiKey!;
        var ct = TestContext.Current.CancellationToken;

        Assert.Null(await apiKey.GetValueAsync(new ValueProviderContext(), ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct));
        Assert.Null(await apiKey.GetValueAsync(new ValueProviderContext { Caller = n8n.Resource }, ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct));
        Assert.EndsWith(";ApiKey=", await ((IValueProvider)n8n.Resource.ConnectionStringExpression).GetValueAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct));

        apiKey.Set("key-1");
        Assert.Equal("key-1", await apiKey.GetValueAsync(new ValueProviderContext(), ct));
    }

    [Fact]
    public async Task WithApiKey_ConsumerCaller_WaitsForKey()
    {
        var builder = DistributedApplication.CreateBuilder();
        var n8n = AddN8nWithAllocatedEndpoint(builder);
        var consumer = builder.AddExecutable("consumer", "consumer", "."); // localhost network, like the allocated endpoint
        var ct = TestContext.Current.CancellationToken;

        var pending = ((IValueProvider)n8n.Resource.ConnectionStringExpression)
            .GetValueAsync(new ValueProviderContext { Caller = consumer.Resource }, ct).AsTask();
        await Task.Delay(50, ct);
        Assert.False(pending.IsCompleted);

        n8n.Resource.ApiKey!.Set("key-1");

        Assert.EndsWith(";ApiKey=key-1", await pending.WaitAsync(TimeSpan.FromSeconds(5), ct));
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

    private static IResourceBuilder<N8nResource> AddN8nWithAllocatedEndpoint(IDistributedApplicationBuilder builder) =>
        builder.AddN8n("n8n")
            .WithEndpoint("http", e => e.AllocatedEndpoint = new AllocatedEndpoint(e, "localhost", 5678))
            .WithInstanceOwner("admin@dev.local", "Admin", "Local")
            .WithApiKey();

    private sealed record RecordedRequest(HttpMethod Method, string Path, string? Cookie, string? BrowserId, string? ApiKey, string? Body);

    private sealed class FakeN8nHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        public HttpStatusCode LoginStatus { get; init; } = HttpStatusCode.OK;

        public HttpStatusCode PublicApiStatus { get; init; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(new RecordedRequest(request.Method, path,
                Header(request, "Cookie"), Header(request, "browser-id"), Header(request, "X-N8N-API-KEY"),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));

            switch (request.Method.Method, path)
            {
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
