using Aspire.Hosting.ApplicationModel;
using Zemires.Aspire.Hosting.N8n;

namespace Aspire.Hosting;

public static partial class N8nBuilderExtensions
{
    /// <summary>
    /// Adds a queue-mode worker (<c>n8n worker</c>) named <c>{n8n}-{name}</c>, sharing the encryption key and webhook URL of the main instance.
    /// </summary>
    /// <param name="n8nBuilder">The main n8n resource builder.</param>
    /// <param name="name">The worker name suffix.</param>
    /// <param name="port">The host port of the worker health endpoint.</param>
    /// <returns>The worker resource builder.</returns>
    /// <remarks>Workers serve HTTP only; in run mode they trust the developer certificate (e.g. for Redis TLS).</remarks>
    [AspireExport]
    public static IResourceBuilder<N8nWorkerResource> AddWorker(this IResourceBuilder<N8nResource> n8nBuilder, string name, int? port = null)
    {
        ArgumentNullException.ThrowIfNull(n8nBuilder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var worker = new N8nWorkerResource($"{n8nBuilder.Resource.Name}-{name}", n8nBuilder.Resource);

        var workerBuilder = n8nBuilder.ApplicationBuilder.AddResource(worker)
            .WithN8nImage(N8nContainerImageTags.Image)
            .WithArgs("worker")
            .WithIconName("SettingsCogMultiple", IconVariant.Filled)
            .WithHttpEndpoint(targetPort: N8nPort, port: port, name: N8nResource.PrimaryEndpointName, env: "N8N_PORT")
            .WithHttpHealthCheck("/healthz", 200, N8nResource.PrimaryEndpointName)
            .WithEnvironment("QUEUE_HEALTH_CHECK_ACTIVE", "true")
            .WithEnvironment("N8N_ENFORCE_SETTINGS_FILE_PERMISSIONS", "false")
            .WithEnvironment("N8N_ENCRYPTION_KEY", n8nBuilder.Resource.EncryptionKeyParameter)
            .WithEnvironment("WEBHOOK_URL", n8nBuilder.Resource.GetWebhookEndpoint(n8nBuilder.ApplicationBuilder.ExecutionContext))
            .WithParentRelationship(n8nBuilder);

        if (n8nBuilder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
#pragma warning disable ASPIRECERTIFICATES001
            workerBuilder.WithHttpsCertificateConfiguration(ctx =>
            {
                ctx.EnvironmentVariables["NODE_EXTRA_CA_CERTS"] = ctx.CertificatePath;
                return Task.CompletedTask;
            });
#pragma warning restore ASPIRECERTIFICATES001
        }

        return workerBuilder;
    }

    /// <summary>
    /// Runs n8n in queue mode (<c>EXECUTIONS_MODE=queue</c>) with the given Redis. Reads <c>Host</c>, <c>Port</c> and
    /// <c>Password</c> from the connection properties of <paramref name="redis"/> and waits for it.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="redis">The Redis resource.</param>
    /// <returns>The resource builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="redis"/> has no connection string.</exception>
    [AspireExport]
    public static IResourceBuilder<N8nResource> WithQueueMode(this IResourceBuilder<N8nResource> builder, IResourceBuilder<IResource> redis)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(redis);

        var cache = GetConnectionStringResource(redis, "redis");

        return builder.WithEnvironment("EXECUTIONS_MODE", "queue")
            .WithEnvironment("QUEUE_BULL_REDIS_HOST", cache.GetConnectionProperty("Host"))
            .WithEnvironment("QUEUE_BULL_REDIS_PORT", cache.GetConnectionProperty("Port"))
            .WithEnvironment("QUEUE_BULL_REDIS_PASSWORD", cache.GetConnectionProperty("Password"))
            .WithEnvironment("QUEUE_BULL_REDIS_TLS", "true")
            .WithReferenceRelationship(redis)
            .WaitFor(redis);
    }
}
