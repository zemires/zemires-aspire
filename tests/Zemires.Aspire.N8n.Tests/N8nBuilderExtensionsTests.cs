using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Zemires.Aspire.N8n;
using Zemires.N8n.Api;

namespace Zemires.Aspire.N8n.Tests;

public class N8nBuilderExtensionsTests
{
    [Fact]
    public void ParseConnectionString_Url_SetsEndpoint()
    {
        var settings = new N8nClientSettings { ApiKey = "configured" };

        N8nBuilderExtensions.ParseConnectionString(settings, "http://localhost:5678");

        Assert.Equal(new Uri("http://localhost:5678"), settings.Endpoint);
        Assert.Equal("configured", settings.ApiKey);
    }

    [Fact]
    public void ParseConnectionString_EndpointAndApiKey_SetsBoth()
    {
        var settings = new N8nClientSettings();

        N8nBuilderExtensions.ParseConnectionString(settings, "Endpoint=https://localhost:5678;ApiKey=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.abc-_");

        Assert.Equal(new Uri("https://localhost:5678"), settings.Endpoint);
        Assert.Equal("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.abc-_", settings.ApiKey);
    }

    [Fact]
    public void ParseConnectionString_EndpointOnly_KeepsApiKey()
    {
        var settings = new N8nClientSettings { ApiKey = "configured" };

        N8nBuilderExtensions.ParseConnectionString(settings, "endpoint=http://n8n:5678");

        Assert.Equal(new Uri("http://n8n:5678"), settings.Endpoint);
        Assert.Equal("configured", settings.ApiKey);
    }

    [Fact]
    public async Task AddN8nClient_ReadsApiKeyFromConnectionString()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { data = Array.Empty<object>() }) });
        var builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration["ConnectionStrings:n8n"] = "Endpoint=http://localhost:5678;ApiKey=key-1";
        builder.Services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));

        N8nClientSettings? settings = null;
        builder.AddN8nClient("n8n", s => settings = s);
        using var host = builder.Build();

        Assert.Equal("key-1", settings!.ApiKey);
        await host.Services.GetRequiredService<N8nClient>().Workflows.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://localhost:5678/api/v1/workflows", request.Uri.GetLeftPart(UriPartial.Path));
        Assert.Equal("key-1", request.ApiKey);
    }

    [Fact]
    public void AddKeyedN8nClient_RegistersKeyedWebhookClient()
    {
        var builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration["ConnectionStrings:n8n"] = "http://localhost:5678/sub";

        builder.AddKeyedN8nClient("n8n");
        using var host = builder.Build();

        var webhooks = host.Services.GetRequiredKeyedService<N8nWebhookClient>("n8n");
        Assert.Equal(new Uri("http://localhost:5678/sub/"), webhooks.HttpClient.BaseAddress);
    }

    [Theory]
    [InlineData("orders", false, "webhook/orders")]
    [InlineData("/orders/new", false, "webhook/orders/new")]
    [InlineData("orders", true, "webhook-test/orders")]
    public void GetWebhookUri_IsRelative(string path, bool test, string expected)
    {
        var uri = N8nWebhookClient.GetWebhookUri(path, test);

        Assert.False(uri.IsAbsoluteUri);
        Assert.Equal(expected, uri.ToString());
    }

    [Fact]
    public async Task Webhook_PostsJsonToEndpoint()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { ok = true }) });
        var builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration["ConnectionStrings:n8n"] = "Endpoint=http://n8n.test/base;ApiKey=key-1";
        builder.Services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));
        builder.AddN8nClient("n8n");
        using var host = builder.Build();

        var webhooks = host.Services.GetRequiredService<N8nWebhookClient>();
        var result = await webhooks.TriggerAsync<JsonElement>("orders", new { id = 42 }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.GetProperty("ok").GetBoolean());
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("http://n8n.test/base/webhook/orders"), request.Uri);
        Assert.Equal("{\"id\":42}", request.Body);
        Assert.False(request.HasApiKey);
    }

    [Fact]
    public async Task Webhook_NoJsonResponse_ReturnsDefault()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Workflow was started") });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://n8n.test/") };

        var result = await new N8nWebhookClient(httpClient).TriggerAsync<JsonElement?>("orders", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Null(handler.Requests.Single().Body);
    }

    [Fact]
    public async Task Webhook_ErrorStatus_TriggerThrows_PostReturnsResponse()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("not registered") });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://n8n.test/") };
        var webhooks = new N8nWebhookClient(httpClient);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            webhooks.TriggerAsync<JsonElement>("orders", new { }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Contains("not registered", ex.Message);

        using var response = await webhooks.PostAsync("orders", test: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(new Uri("http://n8n.test/webhook-test/orders"), handler.Requests[^1].Uri);
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body, string? ApiKey)
    {
        public bool HasApiKey => ApiKey is not null;
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.TryGetValues("X-N8N-API-KEY", out var apiKey) ? string.Join(",", apiKey) : null));
            return respond(request);
        }
    }
}
