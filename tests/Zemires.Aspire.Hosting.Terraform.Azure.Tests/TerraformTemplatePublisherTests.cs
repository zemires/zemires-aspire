using System.Runtime.CompilerServices;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Aspire.Hosting.Utils;

namespace Zemires.Aspire.Hosting.Terraform.Azure.Tests;

public class TerraformTemplatePublisherTests(ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task TerraformAzureTemplatePublisher_PublishAzureSql_Success()
    {
        await using var builder = CreatePublishBuilder();

        builder.AddAzureSqlServer("sqlserver")
            .AddDatabase("db");

        var outputPath = await builder.PublishAsync();

        Assert.True(File.Exists(Path.Combine(outputPath, "sqlserver.tf")));
    }

    [Fact]
    public async Task TerraformAzureTemplatePublisher_PublishAzureServiceBus_Success()
    {
        await using var builder = CreatePublishBuilder();

        var bus = builder.AddAzureServiceBus("servicebus");
        var queue = bus.AddServiceBusQueue("queue");
        var topic = bus.AddServiceBusTopic("topic");
        topic.AddServiceBusSubscription("topic-sub1", "sub1");

        builder.AddContainer("container", "container")
            .WithReference(bus)
            .WithReference(queue)
            .WithReference(topic);

        var outputPath = await builder.PublishAsync();

        Assert.True(File.Exists(Path.Combine(outputPath, "servicebus.tf")));
    }

    [Fact]
    public async Task TerraformAzureTemplatePublisher_PublishAzureCosmos_Success()
    {
        await using var builder = CreatePublishBuilder();

        var cosmos = builder.AddAzureCosmosDB("cosmos");
        var db = cosmos.AddCosmosDatabase("db", "dbname");

        builder.AddContainer("container", "container")
            .WithReference(db);

        var outputPath = await builder.PublishAsync();

        Assert.True(File.Exists(Path.Combine(outputPath, "cosmos.tf")));
    }

    [Fact]
    public async Task TerraformAzureTemplatePublisher_PublishAzureSignalr_Success()
    {
        await using var builder = CreatePublishBuilder();

        var signalr = builder.AddAzureSignalR("signalr");

        builder.AddContainer("container", "container")
            .WithReference(signalr);

        var outputPath = await builder.PublishAsync();

        Assert.True(File.Exists(Path.Combine(outputPath, "signalr.tf")));
    }

    [Fact]
    public async Task TerraformAzureTemplatePublisher_PublishAzureSearch_Success()
    {
        await using var builder = CreatePublishBuilder();

        var search = builder.AddAzureSearch("search");

        builder.AddContainer("container", "container")
            .WithReference(search);

        var outputPath = await builder.PublishAsync();

        Assert.True(File.Exists(Path.Combine(outputPath, "search.tf")));
    }

    [Fact]
    public async Task TerraformAzureTemplatePublisher_PublishAzureEventHub_Success()
    {
        await using var builder = CreatePublishBuilder();

        var hubs = builder.AddAzureEventHubs("hubs");
        var hub = hubs.AddHub("hub", "hubname");
        var consumer = hub.AddConsumerGroup("consumer", "consumerGroupName");

        builder.AddContainer("container", "container")
            .WithReference(hubs)
            .WithReference(hub)
            .WithReference(consumer);

        var outputPath = await builder.PublishAsync();

        Assert.True(File.Exists(Path.Combine(outputPath, "hubs.tf")));
    }

    [Fact]
    public async Task TerraformAzureTemplatePublisher_PublishAzureKeyVault_Success()
    {
        await using var builder = CreatePublishBuilder();

        var param = builder.AddParameter("param1", "MyParameter");
        var kv = builder.AddAzureKeyVault("kv");
        kv.AddSecret("secret1", "mysecret", param);

        builder.AddContainer("container", "container")
            .WithReference(kv);

        var outputPath = await builder.PublishAsync();

        Assert.True(File.Exists(Path.Combine(outputPath, "kv.tf")));
        var content = await File.ReadAllTextAsync(Path.Combine(outputPath, "kv.tf"), TestContext.Current.CancellationToken);
        Assert.Contains("azurerm_key_vault.kv", content);
        Assert.Contains("azurerm_key_vault_secret.secret1", content);
    }

    private IDistributedApplicationTestingBuilder CreatePublishBuilder([CallerMemberName] string testName = "")
    {
        var builder = TestDistributedApplicationBuilder.CreateWithOutput(DistributedApplicationOperation.Publish, "terraform", testName, outputHelper,
            TerraformPublishTestExtensions.PublishStep);

        builder.AddTerraformAzureTemplatePublishing(options => options.FilePrefix = null);
        builder.AddTerraformAzureEnvironment(TerraformPublishTestExtensions.EnvironmentName, TerraformPublishTestExtensions.TemplatesPath);

        return builder;
    }
}
