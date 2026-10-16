using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Zemires.Aspire;
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
    /// Registers <see cref="Clients.Options"/> and the Zitadel service clients (<see cref="AdminService.AdminServiceClient"/>,
    /// <see cref="AuthService.AuthServiceClient"/>, <see cref="ManagementService.ManagementServiceClient"/>,
    /// <see cref="UserService.UserServiceClient"/>, <see cref="OrganizationService.OrganizationServiceClient"/>,
    /// <see cref="SessionService.SessionServiceClient"/>, <see cref="SettingsService.SettingsServiceClient"/>,
    /// <see cref="OIDCService.OIDCServiceClient"/>) as singletons and adds a Zitadel health check.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="connectionName">The name of the connection string <c>ConnectionStrings:{connectionName}</c>.</param>
    /// <param name="configureSettings">Configures the settings after they are read from <c>Aspire:Zitadel:Client</c>.</param>
    /// <exception cref="InvalidOperationException">No endpoint is configured (when a client is resolved).</exception>
    public static void AddZitadelApi(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<ZitadelApiSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(connectionName);
        AddZitadelApi(builder, configureSettings, connectionName, serviceKey: null);
    }

    /// <summary>
    /// Registers the Zitadel API clients as keyed singletons for <paramref name="name"/> and adds a Zitadel health check.
    /// Settings are read from <c>Aspire:Zitadel:Client</c>, overridden by <c>Aspire:Zitadel:Client:{name}</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="name">The service key and connection string name.</param>
    /// <param name="configureSettings">Configures the settings after they are read from configuration.</param>
    /// <exception cref="InvalidOperationException">No endpoint is configured (when a client is resolved).</exception>
    public static void AddKeyedZitadelApi(
        this IHostApplicationBuilder builder,
        string name,
        Action<ZitadelApiSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        AddZitadelApi(builder, configureSettings, connectionName: name, serviceKey: name);
    }

    private static void AddZitadelApi(
        IHostApplicationBuilder builder,
        Action<ZitadelApiSettings>? configureSettings,
        string connectionName,
        string? serviceKey)
    {
        var settings = new ZitadelApiSettings();
        var section = builder.Configuration.GetSection(DefaultConfigSectionName);
        section.Bind(settings);
        if (serviceKey is not null)
        {
            section.GetSection(serviceKey).Bind(settings);
        }

        if (builder.Configuration.GetConnectionString(connectionName) is string connectionString)
        {
            settings.ParseConnectionString(connectionString);
        }

        configureSettings?.Invoke(settings);

        if (serviceKey is null)
        {
            builder.Services.AddSingleton(_ => CreateOptions(settings, connectionName));
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
            builder.Services.AddKeyedSingleton(serviceKey, (_, _) => CreateOptions(settings, connectionName));
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
            var endpoint = settings.Endpoint;
            builder.Services.AddHttpClient(healthCheckName);

            builder.TryAddHealthCheck(new HealthCheckRegistration(
                healthCheckName,
                sp => new ZitadelHealthCheck(sp.GetRequiredService<IHttpClientFactory>().CreateClient(healthCheckName), endpoint),
                failureStatus: null,
                tags: null,
                timeout: settings.HealthCheckTimeout.ToTimeout()));
        }

        void AddServiceClient<TClient>(Func<Clients.Options, TClient> factory) where TClient : class
            => builder.Services.AddSingleton(sp => factory(sp.GetRequiredService<Clients.Options>()));

        void AddKeyedServiceClient<TClient>(Func<Clients.Options, TClient> factory) where TClient : class
            => builder.Services.AddKeyedSingleton(serviceKey, (sp, key) => factory(sp.GetRequiredKeyedService<Clients.Options>(key)));
    }

    private static Clients.Options CreateOptions(ZitadelApiSettings settings, string connectionName)
    {
        if (settings.Endpoint is null)
        {
            throw new InvalidOperationException(
                $"The Zitadel API clients could not be configured. Ensure valid connection information was provided in 'ConnectionStrings:{connectionName}' or " +
                $"{nameof(settings.Endpoint)} must be provided in the '{DefaultConfigSectionName}' configuration section.");
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
}
