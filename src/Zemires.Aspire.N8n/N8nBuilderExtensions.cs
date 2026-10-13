using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Collections.Generic;
using System.Text;
using Zemires.Aspire.N8n;
using Zemires.N8n.Api;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Provides extension methods for registering an n8n client in an <see cref="IHostApplicationBuilder"/>.
/// </summary>
public static class N8nBuilderExtensions
{
    private const string DefaultConfigSectionName = "Aspire:n8n:Client";

    /// <summary>
    /// Registers <see cref="N8nClient" /> as a singleton in the services provided by the <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder" /> to read config from and add services to.</param>
    /// <param name="connectionName">The connection name to use to find a connection string.</param>
    /// <param name="configureSettings">An optional method that can be used for customizing the <see cref="N8nClientSettings"/>. It's invoked after the settings are read from the configuration.</param>
    /// <remarks>Reads the configuration from "Aspire:N8n:Client" section.</remarks>
    /// <exception cref="InvalidOperationException">If required ConnectionString is not provided in configuration section</exception>
    public static void AddN8nClient(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<N8nClientSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNullOrEmpty(connectionName);
        AddN8nClient(builder, DefaultConfigSectionName, configureSettings, connectionName, serviceKey: null);
    }

    /// <summary>
    /// Registers <see cref="N8nClient" /> as a keyed singleton for the given <paramref name="name" /> in the services provided by the <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder" /> to read config from and add services to.</param>
    /// <param name="name">The connection name to use to find a connection string.</param>
    /// <param name="configureSettings">An optional method that can be used for customizing the <see cref="N8nClientSettings"/>. It's invoked after the settings are read from the configuration.</param>
    /// <remarks>Reads the configuration from "Aspire:N8n:Client" section.</remarks>
    /// <exception cref="InvalidOperationException">If required ConnectionString is not provided in configuration section</exception>
    public static void AddKeyedN8nClient(
        this IHostApplicationBuilder builder,
        string name,
        Action<N8nClientSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNullOrEmpty(name);
        AddN8nClient(builder, $"{DefaultConfigSectionName}:{name}", configureSettings, connectionName: name, serviceKey: name);
    }

    private static void AddN8nClient(
        this IHostApplicationBuilder builder,
        string configurationSectionName,
        Action<N8nClientSettings>? configureSettings,
        string connectionName,
        string? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var settings = new N8nClientSettings();
        builder.Configuration.GetSection(configurationSectionName).Bind(settings);

        if (builder.Configuration.GetConnectionString(connectionName) is string connectionString)
        {
            settings.Endpoint = new Uri(connectionString);
        }

        configureSettings?.Invoke(settings);

        if (serviceKey is null)
        {
            builder.Services.AddSingleton(ConfigureN8nClient);
        }
        else
        {
            builder.Services.AddKeyedSingleton(serviceKey, (sp, key) => ConfigureN8nClient(sp));
        }

        if (!settings.DisableHealthChecks)
        {
            var healthCheckName = serviceKey is null ? "n8n" : $"n8n_{connectionName}";

            builder.TryAddHealthCheck(new HealthCheckRegistration(
                healthCheckName,
                sp => new N8nHealthCheck(serviceKey is null ?
                    sp.GetRequiredService<N8nClient>() :
                    sp.GetRequiredKeyedService<N8nClient>(serviceKey)),
                failureStatus: null,
                tags: null,
                timeout: settings.HealthCheckTimeout > 0 ? TimeSpan.FromMilliseconds(settings.HealthCheckTimeout.Value) : null
                ));
        }

        N8nClient ConfigureN8nClient(IServiceProvider serviceProvider)
        {
            if (settings.Endpoint is not null)
            {
                return new N8nClient(settings.Endpoint.ToString().TrimEnd('/'), apiKey: settings.ApiKey);
            }
            else
            {
                throw new InvalidOperationException(
                        $"A N8nClient could not be configured. Ensure valid connection information was provided in 'ConnectionStrings:{connectionName}' or either " +
                        $"{nameof(settings.Endpoint)} must be provided " +
                        $"in the '{configurationSectionName}' configuration section.");
            }
        }
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
