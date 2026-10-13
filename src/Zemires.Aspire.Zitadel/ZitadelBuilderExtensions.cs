using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Zemires.Aspire.Zitadel;
using Zitadel.Admin.V1;
using Zitadel.Api;
using Zitadel.Auth.V1;
using Zitadel.Credentials;
using Zitadel.Management.V1;
using Zitadel.Oidc.V2;
using Zitadel.Org.V2;
using Zitadel.Session.V2;
using Zitadel.Settings.V2;
using Zitadel.User.V2;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Provides extension methods for registering the Zitadel API service clients in an <see cref="IHostApplicationBuilder"/>.
/// </summary>
public static class ZitadelBuilderExtensions
{
    private const string DefaultConfigSectionName = "Aspire:Zitadel:Client";

    /// <summary>
    /// Registers the Zitadel API clients (<see cref="Clients.Options"/>, <see cref="AdminService.AdminServiceClient"/>,
    /// <see cref="AuthService.AuthServiceClient"/>, <see cref="ManagementService.ManagementServiceClient"/>,
    /// <see cref="UserService.UserServiceClient"/>, <see cref="OrganizationService.OrganizationServiceClient"/>,
    /// <see cref="SessionService.SessionServiceClient"/>, <see cref="SettingsService.SettingsServiceClient"/> and
    /// <see cref="OIDCService.OIDCServiceClient"/>) as singletons in the services provided by the <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder" /> to read config from and add services to.</param>
    /// <param name="connectionName">The connection name to use to find a connection string.</param>
    /// <param name="configureSettings">An optional method that can be used for customizing the <see cref="ZitadelApiSettings"/>. It's invoked after the settings are read from the configuration.</param>
    /// <remarks>Reads the configuration from "Aspire:Zitadel:Client" section.</remarks>
    /// <exception cref="InvalidOperationException">If required ConnectionString is not provided in configuration section</exception>
    public static void AddZitadelApi(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<ZitadelApiSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(connectionName);
        AddZitadelApi(builder, DefaultConfigSectionName, configureSettings, connectionName, serviceKey: null);
    }

    /// <summary>
    /// Registers the Zitadel API clients as keyed singletons for the given <paramref name="name" /> in the services provided by the <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder" /> to read config from and add services to.</param>
    /// <param name="name">The connection name to use to find a connection string.</param>
    /// <param name="configureSettings">An optional method that can be used for customizing the <see cref="ZitadelApiSettings"/>. It's invoked after the settings are read from the configuration.</param>
    /// <remarks>Reads the configuration from "Aspire:Zitadel:Client:{name}" section.</remarks>
    /// <exception cref="InvalidOperationException">If required ConnectionString is not provided in configuration section</exception>
    public static void AddKeyedZitadelApi(
        this IHostApplicationBuilder builder,
        string name,
        Action<ZitadelApiSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        AddZitadelApi(builder, $"{DefaultConfigSectionName}:{name}", configureSettings, connectionName: name, serviceKey: name);
    }

    private static void AddZitadelApi(
        IHostApplicationBuilder builder,
        string configurationSectionName,
        Action<ZitadelApiSettings>? configureSettings,
        string connectionName,
        string? serviceKey)
    {
        var settings = new ZitadelApiSettings();
        builder.Configuration.GetSection(configurationSectionName).Bind(settings);

        if (builder.Configuration.GetConnectionString(connectionName) is string connectionString)
        {
            settings.ParseConnectionString(connectionString);
        }

        configureSettings?.Invoke(settings);

        if (serviceKey is null)
        {
            builder.Services.AddSingleton(_ => CreateOptions(settings, configurationSectionName, connectionName));
            AddServiceClient(Clients.AdminService);
            AddServiceClient(Clients.AuthService);
            AddServiceClient(Clients.ManagementService);
            AddServiceClient(Clients.UserService);
            AddServiceClient(Clients.OrganizationService);
            AddServiceClient(Clients.SessionService);
            AddServiceClient(Clients.SettingsService);
            AddServiceClient(Clients.OidcService);
        }
        else
        {
            builder.Services.AddKeyedSingleton(serviceKey, (_, _) => CreateOptions(settings, configurationSectionName, connectionName));
            AddKeyedServiceClient(Clients.AdminService);
            AddKeyedServiceClient(Clients.AuthService);
            AddKeyedServiceClient(Clients.ManagementService);
            AddKeyedServiceClient(Clients.UserService);
            AddKeyedServiceClient(Clients.OrganizationService);
            AddKeyedServiceClient(Clients.SessionService);
            AddKeyedServiceClient(Clients.SettingsService);
            AddKeyedServiceClient(Clients.OidcService);
        }

        if (!settings.DisableHealthChecks && settings.Endpoint is not null)
        {
            var healthCheckName = serviceKey is null ? "zitadel" : $"zitadel_{connectionName}";

            builder.TryAddHealthCheck(new HealthCheckRegistration(
                healthCheckName,
                _ => new ZitadelHealthCheck(settings.Endpoint),
                failureStatus: null,
                tags: null,
                timeout: settings.HealthCheckTimeout > 0 ? TimeSpan.FromMilliseconds(settings.HealthCheckTimeout.Value) : null));
        }

        void AddServiceClient<TClient>(Func<Clients.Options, TClient> factory) where TClient : class
            => builder.Services.AddSingleton(sp => factory(sp.GetRequiredService<Clients.Options>()));

        void AddKeyedServiceClient<TClient>(Func<Clients.Options, TClient> factory) where TClient : class
            => builder.Services.AddKeyedSingleton(serviceKey, (sp, key) => factory(sp.GetRequiredKeyedService<Clients.Options>(key)));
    }

    private static Clients.Options CreateOptions(ZitadelApiSettings settings, string configurationSectionName, string connectionName)
    {
        if (settings.Endpoint is null)
        {
            throw new InvalidOperationException(
                $"The Zitadel API clients could not be configured. Ensure valid connection information was provided in 'ConnectionStrings:{connectionName}' or " +
                $"{nameof(settings.Endpoint)} must be provided in the '{configurationSectionName}' configuration section.");
        }

        var endpoint = settings.Endpoint.ToString().TrimEnd('/');
        return new Clients.Options(endpoint, CreateTokenProvider(settings, endpoint));
    }

    private static ITokenProvider? CreateTokenProvider(ZitadelApiSettings settings, string endpoint)
    {
        if (!string.IsNullOrEmpty(settings.PersonalAccessToken))
        {
            return ITokenProvider.Static(settings.PersonalAccessToken);
        }

        ServiceAccount? serviceAccount = null;
        if (!string.IsNullOrEmpty(settings.ServiceAccountKey))
        {
            serviceAccount = ServiceAccount.LoadFromJsonString(DecodeServiceAccountKey(settings.ServiceAccountKey));
        }
        else if (!string.IsNullOrEmpty(settings.ServiceAccountKeyPath))
        {
            serviceAccount = ServiceAccount.LoadFromJsonFile(settings.ServiceAccountKeyPath);
        }

        return serviceAccount is null
            ? null
            : ITokenProvider.ServiceAccount(endpoint, serviceAccount, new ServiceAccount.AuthOptions { ApiAccess = true });
    }

    private static string DecodeServiceAccountKey(string key)
    {
        if (key.TrimStart().StartsWith('{'))
        {
            return key;
        }

        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(key));
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"{nameof(ZitadelApiSettings.ServiceAccountKey)} must be a JSON key or a base64 encoded JSON key.", ex);
        }
    }

    /// <summary>
    /// Adds a HealthCheckRegistration if one hasn't already been added to the builder.
    /// </summary>
    private static void TryAddHealthCheck(this IHostApplicationBuilder builder, HealthCheckRegistration healthCheckRegistration)
    {
        var healthCheckKey = $"Aspire.HealthChecks.{healthCheckRegistration.Name}";
        if (!builder.Properties.ContainsKey(healthCheckKey))
        {
            builder.Properties[healthCheckKey] = true;
            builder.Services.AddHealthChecks().Add(healthCheckRegistration);
        }
    }
}
