using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Zemires.Aspire.Hosting.N8n;

namespace Aspire.Hosting;

/// <summary>
/// Extension methods for adding n8n resources to the application model.
/// </summary>
public static partial class N8nBuilderExtensions
{
    private const int N8nPort = 5678;
    private const string DataPath = "/home/node/.n8n";

    /// <summary>
    /// Adds an n8n container resource (image <inheritdoc cref="N8nContainerImageTags.Image"/>:<inheritdoc cref="N8nContainerImageTags.Tag"/>).
    /// </summary>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/>.</param>
    /// <param name="name">The resource name, also used as connection string name.</param>
    /// <param name="encryptionKey">The n8n encryption key. Generated when <see langword="null"/>.</param>
    /// <param name="port">The host port of the HTTP endpoint.</param>
    /// <returns>The resource builder.</returns>
    /// <example>
    /// <code lang="csharp">
    /// var n8n = builder.AddN8n("n8n");
    /// </code>
    /// </example>
    [AspireExport]
    public static IResourceBuilder<N8nResource> AddN8n(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        IResourceBuilder<ParameterResource>? encryptionKey = null,
        int? port = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var encryptionKeyParameter = encryptionKey?.Resource ?? ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, $"{name}-encryption-key");

        var n8n = new N8nResource(name, encryptionKeyParameter);

        var n8nBuilder = builder.AddResource(n8n)
            .WithN8nImage(N8nContainerImageTags.Image)
            .WithHttpEndpoint(targetPort: N8nPort, port: port, name: N8nResource.PrimaryEndpointName, env: "N8N_PORT")
            .WithUrlForEndpoint(N8nResource.PrimaryEndpointName, e => e.DisplayText = "n8n App")
            .WithHttpHealthCheck("/healthz", 200, N8nResource.PrimaryEndpointName)
            .WithIconName("BranchFork", IconVariant.Regular)
            .WithEnvironment("QUEUE_HEALTH_CHECK_ACTIVE", "true")
            .WithEnvironment("OFFLOAD_MANUAL_EXECUTIONS_TO_WORKERS", "true")
            .WithEnvironment("N8N_ENFORCE_SETTINGS_FILE_PERMISSIONS", "false")
            .WithEnvironment("N8N_ENCRYPTION_KEY", encryptionKeyParameter)
            .WithEnvironment("WEBHOOK_URL", n8n.GetWebhookEndpoint(builder.ExecutionContext))
            .WithEnvironment("N8N_PROXY_HOPS", "1");

#pragma warning disable ASPIRECERTIFICATES001
        n8nBuilder
            .WithHttpsCertificateConfiguration(ctx =>
            {
                ctx.EnvironmentVariables["N8N_PROTOCOL"] = "https";
                ctx.EnvironmentVariables["N8N_SSL_KEY"] = ctx.KeyPath;
                ctx.EnvironmentVariables["N8N_SSL_CERT"] = ctx.CertificatePath;
                ctx.EnvironmentVariables["NODE_EXTRA_CA_CERTS"] = ctx.CertificatePath;
                return Task.CompletedTask;
            })
            .SubscribeHttpsEndpointsUpdate(_ => n8nBuilder.WithEndpoint(N8nResource.PrimaryEndpointName, ep => ep.UriScheme = "https"));
#pragma warning restore ASPIRECERTIFICATES001

        return n8nBuilder;
    }

    /// <summary>
    /// Adds a named volume for the n8n data folder.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">The volume name. Generated when <see langword="null"/>.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<N8nResource> WithDataVolume(this IResourceBuilder<N8nResource> builder, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithVolume(name ?? VolumeNameGenerator.Generate(builder, "data"), DataPath);
    }

    /// <summary>
    /// Adds a bind mount for the n8n data folder.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="source">The host directory to mount.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<N8nResource> WithDataBindMount(this IResourceBuilder<N8nResource> builder, string source)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(source);

        return builder.WithBindMount(source, DataPath);
    }

    /// <summary>
    /// Stores n8n data in a PostgreSQL database. Reads <c>Host</c>, <c>Port</c>, <c>DatabaseName</c>, <c>Username</c> and
    /// <c>Password</c> from the connection properties of <paramref name="database"/> and waits for it.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="database">The PostgreSQL database.</param>
    /// <param name="useTls">Whether to connect with TLS.</param>
    /// <returns>The resource builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="database"/> has no connection string.</exception>
    [AspireExport]
    public static IResourceBuilder<N8nResource> WithPostgresDatabase(this IResourceBuilder<N8nResource> builder, IResourceBuilder<IResource> database, bool useTls = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(database);

        var db = GetConnectionStringResource(database, "database");

        builder.WithEnvironment("DB_TYPE", "postgresdb")
            .WithEnvironment("DB_POSTGRESDB_DATABASE", db.GetConnectionProperty("DatabaseName"))
            .WithEnvironment("DB_POSTGRESDB_HOST", db.GetConnectionProperty("Host"))
            .WithEnvironment("DB_POSTGRESDB_PORT", db.GetConnectionProperty("Port"))
            .WithEnvironment("DB_POSTGRESDB_USER", db.GetConnectionProperty("Username"))
            .WithEnvironment("DB_POSTGRESDB_PASSWORD", db.GetConnectionProperty("Password"))
            .WithReferenceRelationship(database)
            .WaitFor(database);

        if (useTls)
        {
            builder.WithEnvironment("DB_POSTGRESDB_SSL_ENABLED", "true")
                .WithEnvironment("DB_POSTGRESDB_SSL_REJECT_UNAUTHORIZED", "true");
        }

        return builder;
    }

    /// <summary>
    /// Sets the time zone (<c>GENERIC_TIMEZONE</c> and <c>TZ</c>).
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="timeZone">The IANA time zone, e.g. <c>Europe/Zurich</c>.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<N8nResource> WithTimeZone(this IResourceBuilder<N8nResource> builder, string timeZone)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(timeZone);

        return builder
            .WithEnvironment("GENERIC_TIMEZONE", timeZone)
            .WithEnvironment("TZ", timeZone);
    }

    /// <summary>
    /// Creates the instance owner account. The password is passed bcrypt-hashed (<c>N8N_INSTANCE_OWNER_PASSWORD_HASH</c>).
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="email">The owner email address.</param>
    /// <param name="firstName">The owner first name.</param>
    /// <param name="lastName">The owner last name.</param>
    /// <param name="password">The owner password. Generated when <see langword="null"/>.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<N8nResource> WithInstanceOwner(
        this IResourceBuilder<N8nResource> builder,
        string email,
        string firstName,
        string lastName,
        IResourceBuilder<ParameterResource>? password = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(email);

        var passwordParameter = password?.Resource
            ?? ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder.ApplicationBuilder, $"{builder.Resource.Name}-instance-owner-password");

        builder.Resource.InstanceOwnerPassword = passwordParameter;

        return builder
            .WithEnvironment("N8N_INSTANCE_OWNER_MANAGED_BY_ENV", "true")
            .WithEnvironment("N8N_INSTANCE_OWNER_EMAIL", email)
            .WithEnvironment("N8N_INSTANCE_OWNER_FIRST_NAME", firstName)
            .WithEnvironment("N8N_INSTANCE_OWNER_LAST_NAME", lastName)
            .WithEnvironment(async ctx =>
            {
                var plainPassword = await passwordParameter.GetValueAsync(ctx.CancellationToken).ConfigureAwait(false);
                ctx.EnvironmentVariables["N8N_INSTANCE_OWNER_PASSWORD_HASH"] = BCrypt.Net.BCrypt.HashPassword(plainPassword);
            });
    }

    /// <summary>
    /// Sets the enterprise license key (<c>N8N_LICENSE_ACTIVATION_KEY</c>) and hides the usage page.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="licenseKey">The license activation key.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport("withLicenseKeyParameter")]
    public static IResourceBuilder<N8nResource> WithLicenseKey(
        this IResourceBuilder<N8nResource> builder,
        IResourceBuilder<ParameterResource> licenseKey)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(licenseKey);

        return builder
            .WithEnvironment("N8N_LICENSE_ACTIVATION_KEY", licenseKey.Resource)
            .WithEnvironment("N8N_HIDE_USAGE_PAGE", "true");
    }

    /// <summary>
    /// Sets the enterprise license key from a value, stored as secret parameter <c>{name}-license-key</c>.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="licenseKey">The license activation key.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport("withLicenseKeyString")]
    public static IResourceBuilder<N8nResource> WithLicenseKey(
        this IResourceBuilder<N8nResource> builder,
        string licenseKey)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(licenseKey);

        return builder.WithLicenseKey(builder.ApplicationBuilder.AddParameter($"{builder.Resource.Name}-license-key", value: licenseKey, secret: true));
    }

    /// <summary>
    /// Sets the enterprise license key from the secret parameter <c>{name}-license-key</c>.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<N8nResource> WithLicenseKey(
        this IResourceBuilder<N8nResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithLicenseKey(builder.ApplicationBuilder.AddParameter($"{builder.Resource.Name}-license-key", secret: true));
    }

    /// <summary>
    /// Adds the Aspire OTLP exporter and maps its <c>OTEL_*</c> variables to the <c>N8N_OTEL_*</c> variables read by n8n.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<N8nResource> WithOtlpExporter(this IResourceBuilder<N8nResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        OtlpConfigurationExtensions.WithOtlpExporter(builder);

        return builder.WithEnvironment(ctx =>
        {
            var env = ctx.EnvironmentVariables;
            if (env.TryGetValue("OTEL_EXPORTER_OTLP_ENDPOINT", out var endpoint))
            {
                env["N8N_OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint;
                env["N8N_OTEL_ENABLED"] = "true";
            }
            else
            {
                ctx.Logger.LogWarning("OTEL_EXPORTER_OTLP_ENDPOINT is not set, the n8n OTLP exporter is not enabled.");
            }

            if (env.TryGetValue("OTEL_EXPORTER_OTLP_HEADERS", out var headers))
            {
                env["N8N_OTEL_EXPORTER_OTLP_HEADERS"] = headers;
            }

            env["N8N_OTEL_EXPORTER_SERVICE_NAME"] = env.TryGetValue("OTEL_SERVICE_NAME", out var serviceName) ? serviceName : builder.Resource.Name;
            env["N8N_OTEL_EXPORTER_OTLP_PROTOCOL"] = env.TryGetValue("OTEL_EXPORTER_OTLP_PROTOCOL", out var protocol) ? protocol : "grpc";
        });
    }

    /// <summary>
    /// Enables community packages and installs the given packages.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="packageNames">Package names, e.g. <c>n8n-nodes-foo</c> or <c>n8n-nodes-foo@1.0.0</c>.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<N8nResource> WithCommunityPackages(this IResourceBuilder<N8nResource> builder, params string[] packageNames)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(packageNames);

        builder.WithEnvironment("N8N_COMMUNITY_PACKAGES_ENABLED", "true");

        if (packageNames.Length > 0)
        {
            builder.WithEnvironment("N8N_COMMUNITY_PACKAGES_MANAGED_BY_ENV", "true")
                .WithEnvironment("N8N_COMMUNITY_PACKAGES", JsonSerializer.Serialize(packageNames.Select(p => new { name = p }), JsonSerializerOptions.Web));
        }

        return builder;
    }

    /// <summary>
    /// Enables the metrics endpoint (<c>N8N_METRICS</c>).
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="configureOptions">Configures the metric categories. All are enabled by default.</param>
    /// <returns>The resource builder.</returns>
    /// <example>
    /// <code lang="csharp">
    /// builder.AddN8n("n8n").WithMetrics(options => options.IncludeWebhookMetrics = false);
    /// </code>
    /// </example>
    [AspireExport(RunSyncOnBackgroundThread = true)]
    public static IResourceBuilder<N8nResource> WithMetrics(this IResourceBuilder<N8nResource> builder, Action<N8nMetricOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new N8nMetricOptions();
        configureOptions?.Invoke(options);

        return builder.WithEnvironment("N8N_METRICS", "true")
            .WithEnvironment("N8N_METRICS_INCLUDE_WEBHOOK_METRICS", ToEnv(options.IncludeWebhookMetrics))
            .WithEnvironment("N8N_METRICS_INCLUDE_WORKFLOW_INFO", ToEnv(options.IncludeWorkflowInfo))
            .WithEnvironment("N8N_METRICS_INCLUDE_FORM_METRICS", ToEnv(options.IncludeFormMetrics))
            .WithEnvironment("N8N_METRICS_INCLUDE_WORKFLOW_ID_LABEL", ToEnv(options.IncludeWorkflowIdLabel))
            .WithEnvironment("N8N_METRICS_INCLUDE_NODE_TYPE_LABEL", ToEnv(options.IncludeNodeTypeLabel))
            .WithEnvironment("N8N_METRICS_INCLUDE_CREDENTIAL_TYPE_LABEL", ToEnv(options.IncludeCredentialTypeLabel))
            .WithEnvironment("N8N_METRICS_INCLUDE_API_ENDPOINTS", ToEnv(options.IncludeApiEndpoints))
            .WithEnvironment("N8N_METRICS_INCLUDE_API_PATH_LABEL", ToEnv(options.IncludeApiPathLabel))
            .WithEnvironment("N8N_METRICS_INCLUDE_QUEUE_METRICS", ToEnv(options.IncludeQueueMetrics));
    }

    /// <summary>
    /// Sets the public URL used for webhooks (<c>WEBHOOK_URL</c>), replacing the default endpoint URL.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="webhookUrl">The public URL, e.g. <c>https://n8n.example.com</c>.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<N8nResource> WithWebhookUrl(
        this IResourceBuilder<N8nResource> builder,
        string webhookUrl)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(webhookUrl);

        return builder.WithEnvironment("WEBHOOK_URL", webhookUrl);
    }

    private static IResourceBuilder<T> WithN8nImage<T>(this IResourceBuilder<T> builder, string image) where T : ContainerResource =>
        builder.WithImage(image)
            .WithImageTag(N8nContainerImageTags.Tag)
            .WithImageRegistry(N8nContainerImageTags.Registry);

    private static EndpointReference GetWebhookEndpoint(this N8nResource n8n, DistributedApplicationExecutionContext executionContext) =>
        n8n.GetEndpoint(N8nResource.PrimaryEndpointName, executionContext.IsPublishMode ? KnownNetworkIdentifiers.PublicInternet : KnownNetworkIdentifiers.LocalhostNetwork);

    private static IResourceWithConnectionString GetConnectionStringResource(IResourceBuilder<IResource> resource, string usage) =>
        resource.Resource as IResourceWithConnectionString
            ?? throw new ArgumentException($"Resource '{resource.Resource.Name}' has no connection string and cannot be used as {usage} for n8n.", usage);

    private static string ToEnv(bool value) => value ? "true" : "false";
}
