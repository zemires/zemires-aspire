using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Zemires.Aspire;
using Zemires.Aspire.N8n;
using Zemires.N8n.Api;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods for registering an <see cref="N8nClient"/> in an <see cref="IHostApplicationBuilder"/>.
/// </summary>
public static class N8nBuilderExtensions
{
    private const string DefaultConfigSectionName = "Aspire:N8n:Client";

    /// <summary>
    /// Registers <see cref="N8nClient"/> as singleton and adds an n8n health check.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="connectionName">The name of the connection string <c>ConnectionStrings:{connectionName}</c> (the n8n URL).</param>
    /// <param name="configureSettings">Configures the settings after they are read from <c>Aspire:N8n:Client</c>.</param>
    /// <exception cref="InvalidOperationException">No endpoint is configured.</exception>
    public static void AddN8nClient(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<N8nClientSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(connectionName);

        AddN8nClient(builder, configureSettings, connectionName, serviceKey: null);
    }

    /// <summary>
    /// Registers <see cref="N8nClient"/> as keyed singleton for <paramref name="name"/> and adds an n8n health check.
    /// Settings are read from <c>Aspire:N8n:Client</c>, overridden by <c>Aspire:N8n:Client:{name}</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="name">The service key and connection string name.</param>
    /// <param name="configureSettings">Configures the settings after they are read from configuration.</param>
    /// <exception cref="InvalidOperationException">No endpoint is configured.</exception>
    public static void AddKeyedN8nClient(
        this IHostApplicationBuilder builder,
        string name,
        Action<N8nClientSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        AddN8nClient(builder, configureSettings, connectionName: name, serviceKey: name);
    }

    private static void AddN8nClient(
        IHostApplicationBuilder builder,
        Action<N8nClientSettings>? configureSettings,
        string connectionName,
        string? serviceKey)
    {
        var settings = new N8nClientSettings();
        var section = builder.Configuration.GetSection(DefaultConfigSectionName);
        section.Bind(settings);
        if (serviceKey is not null)
        {
            section.GetSection(serviceKey).Bind(settings);
        }

        if (builder.Configuration.GetConnectionString(connectionName) is { Length: > 0 } connectionString)
        {
            settings.Endpoint = new Uri(connectionString);
        }

        configureSettings?.Invoke(settings);

        var httpClientName = serviceKey is null ? "n8n" : $"n8n_{serviceKey}";
        builder.Services.AddHttpClient(httpClientName);

        if (serviceKey is null)
        {
            builder.Services.AddSingleton(CreateClient);
        }
        else
        {
            builder.Services.AddKeyedSingleton(serviceKey, (sp, _) => CreateClient(sp));
        }

        if (!settings.DisableHealthChecks)
        {
            builder.TryAddHealthCheck(new HealthCheckRegistration(
                httpClientName,
                sp => new N8nHealthCheck(serviceKey is null ? sp.GetRequiredService<N8nClient>() : sp.GetRequiredKeyedService<N8nClient>(serviceKey)),
                failureStatus: null,
                tags: null,
                timeout: settings.HealthCheckTimeout.ToTimeout()));
        }

        N8nClient CreateClient(IServiceProvider serviceProvider)
        {
            if (settings.Endpoint is null)
            {
                throw new InvalidOperationException(
                    $"N8nClient could not be configured. Provide 'ConnectionStrings:{connectionName}' or '{nameof(settings.Endpoint)}' in the '{DefaultConfigSectionName}' configuration section.");
            }

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName);
            return new N8nClient(settings.Endpoint.ToString(), settings.ApiKey, httpClient);
        }
    }
}
