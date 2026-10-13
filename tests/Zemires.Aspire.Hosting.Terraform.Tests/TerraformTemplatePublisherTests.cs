using System.Runtime.CompilerServices;
using Aspire.Hosting.Testing;
using Aspire.Hosting.Utils;

namespace Zemires.Aspire.Hosting.Terraform.Tests;

public class TerraformTemplatePublisherTests(ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task TerraformTemplatePublisher_PublishContainer_Success()
    {
        await using var builder = CreatePublishBuilder();

        builder.AddContainer("container", "image");

        var outputPath = await builder.PublishAsync();

        Assert.True(File.Exists(Path.Combine(outputPath, "container.tf")));
    }

    [Fact]
    public async Task TerraformTemplatePublisher_PublishParameter_Success()
    {
        await using var builder = CreatePublishBuilder();

        builder.AddParameter("parameter", "1");

        var outputPath = await builder.PublishAsync();

        Assert.True(File.Exists(Path.Combine(outputPath, "variables.tf")));
        var content = await File.ReadAllTextAsync(Path.Combine(outputPath, "variables.tf"), TestContext.Current.CancellationToken);
        Assert.Contains("variable \"parameter\"", content);
    }

    private IDistributedApplicationTestingBuilder CreatePublishBuilder([CallerMemberName] string testName = "")
    {
        var builder = TestDistributedApplicationBuilder.CreateWithOutput(DistributedApplicationOperation.Publish, "terraform", testName, outputHelper,
            TerraformPublishTestExtensions.PublishStep);

        builder.AddTerraformTemplatePublishing(options => options.FilePrefix = null);
        builder.AddTerraformEnvironment(TerraformPublishTestExtensions.EnvironmentName, TerraformPublishTestExtensions.TemplatesPath);

        return builder;
    }
}
