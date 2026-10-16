using Aspire.Hosting.ApplicationModel;
using Zemires.Aspire.Hosting.N8n;

namespace Aspire.Hosting;

public static partial class N8nBuilderExtensions
{
    private const int N8nRunnersPort = 5680;
    private const int N8nBrokerPort = 5679;
    private const string BrokerEndpointName = "broker";

    /// <summary>
    /// Adds an external task runner (image <inheritdoc cref="N8nContainerImageTags.ImageRunners"/>) named <c>{n8n}-{name}</c>
    /// and switches the n8n resource to external runner mode. All runners of a resource share one auth token.
    /// </summary>
    /// <param name="n8nBuilder">The n8n (or worker) resource builder the runner connects to.</param>
    /// <param name="name">The runner name suffix.</param>
    /// <param name="port">The host port of the runner health endpoint.</param>
    /// <param name="sharedAuthToken">The auth token shared with the broker. Generated when <see langword="null"/>.</param>
    /// <returns>The task runner resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<N8nTaskRunnerResource> AddTaskRunner(this IResourceBuilder<N8nResource> n8nBuilder,
        string name,
        int? port = null,
        IResourceBuilder<ParameterResource>? sharedAuthToken = null)
    {
        ArgumentNullException.ThrowIfNull(n8nBuilder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var n8n = n8nBuilder.Resource;

        if (n8n.RunnersAuthTokenParameter is null)
        {
            // first runner: configure the broker once, the token is resolved late so a later sharedAuthToken applies to all
            n8nBuilder.WithEnvironment("N8N_RUNNERS_MODE", "external")
                .WithEnvironment("N8N_RUNNERS_BROKER_LISTEN_ADDRESS", "0.0.0.0")
                .WithEnvironment(ctx => ctx.EnvironmentVariables["N8N_RUNNERS_AUTH_TOKEN"] = n8n.RunnersAuthTokenParameter!)
                .WithHttpEndpoint(targetPort: N8nBrokerPort, name: BrokerEndpointName);
        }

        n8n.RunnersAuthTokenParameter = sharedAuthToken?.Resource
            ?? n8n.RunnersAuthTokenParameter
            ?? ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(n8nBuilder.ApplicationBuilder, $"{n8n.Name}-runners-auth-token");

        var runner = new N8nTaskRunnerResource($"{n8n.Name}-{name}", n8n);

        return n8nBuilder.ApplicationBuilder.AddResource(runner)
            .WithN8nImage(N8nContainerImageTags.ImageRunners)
            .WithIconName("SettingsCogMultiple", IconVariant.Filled)
            .WithHttpEndpoint(targetPort: N8nRunnersPort, port: port, name: N8nResource.PrimaryEndpointName, env: "N8N_PORT")
            .WithHttpHealthCheck("/healthz", 200, N8nResource.PrimaryEndpointName)
            .WithEnvironment(ctx => ctx.EnvironmentVariables["N8N_RUNNERS_AUTH_TOKEN"] = n8n.RunnersAuthTokenParameter!)
            .WithEnvironment("N8N_RUNNERS_TASK_BROKER_URI", n8n.GetEndpoint(BrokerEndpointName))
            .WithParentRelationship(n8nBuilder);
    }
}
