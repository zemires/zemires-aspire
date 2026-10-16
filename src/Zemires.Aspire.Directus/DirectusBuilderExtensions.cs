using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Qute.Directus;
using Qute.Directus.Extensions;
using System.Data.Common;
using Zemires.Aspire;
using Zemires.Aspire.Directus;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods for registering a <see cref="DirectusClient"/> in an <see cref="IHostApplicationBuilder"/>.
/// </summary>
public static class DirectusBuilderExtensions
{
    private const string DefaultConfigSectionName = "Aspire:Directus:Client";

    /// <summary>
    /// Registers <see cref="DirectusClient"/> and adds a Directus health check (disable with <c>Aspire:Directus:Client:DisableHealthChecks</c>).
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="connectionName">The name of the connection string <c>BaseUrl=...;Token=...</c>.</param>
    /// <param name="configureSettings">Configures the options after they are read from <c>Aspire:Directus:Client</c> and the connection string.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="InvalidOperationException">The connection string has no <c>BaseUrl</c>.</exception>
    public static IHostApplicationBuilder AddDirectusClient(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<DirectusOptions>? configureSettings = null) =>
        builder.AddDirectusClient(connectionName, DefaultConfigSectionName, configureSettings);

    /// <summary>
    /// Registers <see cref="DirectusClient"/> with options from a custom configuration section and adds a Directus health check
    /// (disable with <c>{configurationSectionName}:DisableHealthChecks</c>).
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="connectionName">The name of the connection string <c>BaseUrl=...;Token=...</c>.</param>
    /// <param name="configurationSectionName">The configuration section of the <see cref="DirectusOptions"/>.</param>
    /// <param name="configureSettings">Configures the options after they are read from configuration and the connection string.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="InvalidOperationException">The connection string has no <c>BaseUrl</c>.</exception>
    public static IHostApplicationBuilder AddDirectusClient(
        this IHostApplicationBuilder builder,
        string connectionName,
        string configurationSectionName,
        Action<DirectusOptions>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(connectionName);
        ArgumentException.ThrowIfNullOrEmpty(configurationSectionName);

        var section = builder.Configuration.GetSection(configurationSectionName);

        builder.Services.AddDirectus(options =>
        {
            section.Bind(options);

            if (builder.Configuration.GetConnectionString(connectionName) is { Length: > 0 } connectionString)
            {
                var connection = new DbConnectionStringBuilder { ConnectionString = connectionString };

                if (!connection.TryGetValue("BaseUrl", out var baseUrl))
                {
                    throw new InvalidOperationException($"'ConnectionStrings:{connectionName}' has no BaseUrl.");
                }

                options.BaseUrl = baseUrl.ToString()!;
                if (connection.TryGetValue("Token", out var token))
                {
                    options.StaticToken = token.ToString();
                }
            }

            configureSettings?.Invoke(options);
        });

        if (!section.GetValue<bool>("DisableHealthChecks"))
        {
            builder.TryAddHealthCheck(new HealthCheckRegistration(
                $"directus_{connectionName}",
                sp => new DirectusHealthCheck(sp.GetRequiredService<DirectusClient>()),
                failureStatus: null,
                tags: null,
                timeout: section.GetValue<int?>("HealthCheckTimeout").ToTimeout()));
        }

        return builder;
    }
}
