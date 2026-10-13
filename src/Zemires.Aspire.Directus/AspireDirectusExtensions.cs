using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Zemires.Aspire.Directus;
using Qute.Directus;
using Qute.Directus.Extensions;
using System.Data.Common;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Provides extension methods for registering a Directus client in an <see cref="IHostApplicationBuilder"/>.
/// </summary>
public static class AspireDirectusExtensions
{
    private const string DefaultConfigSectionName = "Aspire:Directus:Client";

    /// <summary>
    /// Registers <see cref="DirectusClient" /> in the services provided by the <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder" /> to read config from and add services to.</param>
    /// <param name="connectionName">The connection name to use to find a connection string.</param>
    /// <param name="configureSettings">An optional method that can be used for customizing the <see cref="DirectusOptions"/>. It's invoked after the settings are read from the configuration.</param>
    /// <returns>The <paramref name="builder"/> for chaining.</returns>
    /// <remarks>Reads the configuration from "Aspire:Directus:Client" section.</remarks>
    /// <exception cref="InvalidOperationException">If the connection string does not contain a <c>BaseUrl</c>.</exception>
    public static IHostApplicationBuilder AddDirectusClient(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<DirectusOptions>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNullOrEmpty(connectionName);

        return builder.AddDirectusClient(connectionName, DefaultConfigSectionName, configureSettings);
    }

    /// <summary>
    /// Registers <see cref="DirectusClient" /> in the services provided by the <paramref name="builder"/>, reading settings from a custom configuration section.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder" /> to read config from and add services to.</param>
    /// <param name="connectionName">The connection name to use to find a connection string.</param>
    /// <param name="configurationSectionName">The configuration section to bind <see cref="DirectusOptions"/> from.</param>
    /// <param name="configureSettings">An optional method that can be used for customizing the <see cref="DirectusOptions"/>. It's invoked after the settings are read from the configuration.</param>
    /// <returns>The <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">If the connection string does not contain a <c>BaseUrl</c>.</exception>
    public static IHostApplicationBuilder AddDirectusClient(
        this IHostApplicationBuilder builder,
        string connectionName,
        string configurationSectionName,
        Action<DirectusOptions>? configureSettings = null)
    {
        builder.Services.AddDirectus((options) =>
        {
            builder.Configuration.GetSection(configurationSectionName).Bind(options);

            if (builder.Configuration.GetConnectionString(connectionName) is string connectionString)
            {
                var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

                if (!builder.ContainsKey("BaseUrl"))
                {
                    throw new InvalidOperationException("BaseUrl is required in the connection string.");
                }

                options.BaseUrl = (string)builder["BaseUrl"];
                options.StaticToken = builder["Token"].ToString();

                if (configureSettings != null)
                    configureSettings(options);
            }
        });

        var healthCheckName = $"directus_{connectionName}";
        builder.TryAddHealthCheck(new HealthCheckRegistration(
            healthCheckName,
            sp => new DirectusHealthCheck(sp.GetRequiredService<DirectusClient>()),
            failureStatus: null,
            tags: null,
            timeout: null
            ));

        return builder;
    }

    /// <summary>
    /// Invokes the <paramref name="addHealthCheck"/> action if the given <paramref name="name"/> hasn't already been added to the builder.
    /// </summary>
    internal static void TryAddHealthCheck(this IHostApplicationBuilder builder, string name, Action<IHealthChecksBuilder> addHealthCheck)
    {
        var healthCheckKey = $"Aspire.HealthChecks.{name}";
        if (!builder.Properties.ContainsKey(healthCheckKey))
        {
            builder.Properties[healthCheckKey] = true;
            addHealthCheck(builder.Services.AddHealthChecks());
        }
    }

    /// <summary>
    /// Adds a HealthCheckRegistration if one hasn't already been added to the builder.
    /// </summary>
    internal static void TryAddHealthCheck(this IHostApplicationBuilder builder, HealthCheckRegistration healthCheckRegistration)
    {
        builder.TryAddHealthCheck(healthCheckRegistration.Name, hcBuilder => hcBuilder.Add(healthCheckRegistration));
    }
}
