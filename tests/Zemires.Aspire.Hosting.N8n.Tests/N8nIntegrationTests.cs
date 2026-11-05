namespace Zemires.Aspire.Hosting.N8n.Tests;

// Starts the example AppHost via DistributedApplicationTestingBuilder, which needs a container runtime.
[Trait("Category", "Integration")]
public class N8nIntegrationTests
{
    [Fact]
    public async Task N8n_Starts_And_HealthReady_Ok()
    {
        var n8nName = "n8n";

        // Arange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Zemires_Aspire_Hosting_N8n_AppHost>(TestContext.Current.CancellationToken);
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder => { clientBuilder.AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 10;
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
            });
        });

        await using var app = await appHost.BuildAsync(TestContext.Current.CancellationToken);
        var resourceNotificationService = app.Services.GetRequiredService<ResourceNotificationService>();
        await app.StartAsync(TestContext.Current.CancellationToken);

        // Act
        var httpClient = app.CreateHttpClient(n8nName);

        await resourceNotificationService.WaitForResourceAsync(n8nName, KnownResourceStates.Running, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        var response = await httpClient.GetAsync("/healthz", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task N8n_WithApiKey_CreatesKeyForPublicApi()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Zemires_Aspire_Hosting_N8n_AppHost>(TestContext.Current.CancellationToken);
        await using var app = await appHost.BuildAsync(TestContext.Current.CancellationToken);
        await app.StartAsync(TestContext.Current.CancellationToken);

        var n8n = app.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<N8nResource>().Single(r => r.Name == "n8n");
        var apiKey = await n8n.ApiKey!.GetValueAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromMinutes(3), TestContext.Current.CancellationToken);

        using var httpClient = app.CreateHttpClient("n8n");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/workflows?limit=1");
        request.Headers.Add("X-N8N-API-KEY", apiKey);
        using var response = await httpClient.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

}