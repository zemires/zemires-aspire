using Aspire.Hosting.Pipelines;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

#pragma warning disable ASPIREPIPELINES001

namespace Aspire.Hosting.Utils;

/// <summary>
/// Helpers for tests that publish an application model through a Terraform environment.
/// </summary>
public static class TerraformPublishTestExtensions
{
    /// <summary>
    /// The name of the Terraform environment the tests publish through.
    /// </summary>
    public const string EnvironmentName = "env";

    /// <summary>
    /// The pipeline step that publishes the <see cref="EnvironmentName"/> environment.
    /// </summary>
    public const string PublishStep = "publish-terraform-" + EnvironmentName;

    /// <summary>
    /// The Azure Container Apps templates, relative to the test output directory (bin/Debug/net10.0).
    /// </summary>
    public const string TemplatesPath = "../../../../../templates/azure-container-apps";

    /// <summary>
    /// Builds and runs the publish pipeline, returning the directory the Terraform files were written to.
    /// </summary>
    public static async Task<string> PublishAsync(this IDistributedApplicationTestingBuilder builder)
    {
        await using var app = await builder.BuildAsync(TestContext.Current.CancellationToken);
        var outputPath = app.Services.GetRequiredService<IOptions<PipelineOptions>>().Value.OutputPath;
        Assert.NotNull(outputPath);

        await app.RunAsync(TestContext.Current.CancellationToken);

        return outputPath;
    }
}
