using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Zitadel.Admin.V1;
using Zitadel.Api;
using Zitadel.Auth.V1;
using Zitadel.Management.V1;
using Zitadel.Oidc.V2;
using Zitadel.Org.V2;
using Zitadel.Session.V2;
using Zitadel.Settings.V2;
using Zitadel.User.V2;

namespace Zemires.Aspire.Zitadel.Tests;

public class ZitadelBuilderExtensionsTests
{
    private const string Endpoint = "https://localhost:8501";

    private static HostApplicationBuilder CreateBuilder(params (string Key, string? Value)[] configuration)
    {
        var builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration.AddInMemoryCollection(configuration.Select(c => new KeyValuePair<string, string?>(c.Key, c.Value)));
        return builder;
    }

    [Fact]
    public void AddZitadelApiRegistersServiceClientsFromConnectionString()
    {
        var builder = CreateBuilder(("ConnectionStrings:zitadel", Endpoint));

        builder.AddZitadelApi("zitadel");

        using var host = builder.Build();
        var options = host.Services.GetRequiredService<Clients.Options>();
        Assert.Equal(Endpoint, options.Endpoint);
        Assert.Null(options.TokenProvider);

        Assert.NotNull(host.Services.GetRequiredService<AdminService.AdminServiceClient>());
        Assert.NotNull(host.Services.GetRequiredService<AuthService.AuthServiceClient>());
        Assert.NotNull(host.Services.GetRequiredService<ManagementService.ManagementServiceClient>());
        Assert.NotNull(host.Services.GetRequiredService<UserService.UserServiceClient>());
        Assert.NotNull(host.Services.GetRequiredService<OrganizationService.OrganizationServiceClient>());
        Assert.NotNull(host.Services.GetRequiredService<SessionService.SessionServiceClient>());
        Assert.NotNull(host.Services.GetRequiredService<SettingsService.SettingsServiceClient>());
        Assert.NotNull(host.Services.GetRequiredService<OIDCService.OIDCServiceClient>());
    }

    [Fact]
    public void AddKeyedZitadelApiRegistersKeyedServiceClients()
    {
        var builder = CreateBuilder(("ConnectionStrings:zitadel", Endpoint));

        builder.AddKeyedZitadelApi("zitadel");

        using var host = builder.Build();
        Assert.Equal(Endpoint, host.Services.GetRequiredKeyedService<Clients.Options>("zitadel").Endpoint);
        Assert.NotNull(host.Services.GetRequiredKeyedService<UserService.UserServiceClient>("zitadel"));
        Assert.Null(host.Services.GetService<UserService.UserServiceClient>());
    }

    [Fact]
    public void ConnectionStringWithPersonalAccessTokenCreatesTokenProvider()
    {
        var builder = CreateBuilder(("ConnectionStrings:zitadel", $"Endpoint={Endpoint};PersonalAccessToken=test-token"));

        builder.AddZitadelApi("zitadel");

        using var host = builder.Build();
        var options = host.Services.GetRequiredService<Clients.Options>();
        Assert.Equal(Endpoint, options.Endpoint);
        Assert.NotNull(options.TokenProvider);
    }

    [Fact]
    public void ApiConnectionStringWithBase64KeyIsParsed()
    {
        // base64 with '=' padding, as produced by ZitadelResource.ApiConnectionString
        var key = Convert.ToBase64String("{\"keyId\":\"1\"}"u8.ToArray());
        Assert.EndsWith("=", key);
        ZitadelApiSettings settings = new();

        settings.ParseConnectionString($"Endpoint={Endpoint};ServiceAccountKey={key}");

        Assert.Equal(new Uri(Endpoint), settings.Endpoint);
        Assert.Equal(key, settings.ServiceAccountKey);
    }

    [Fact]
    public void ApiConnectionStringWithEmptyKeyIsIgnored()
    {
        ZitadelApiSettings settings = new() { ServiceAccountKey = "configured" };

        settings.ParseConnectionString($"Endpoint={Endpoint};ServiceAccountKey=");

        Assert.Equal("configured", settings.ServiceAccountKey);
    }

    [Fact]
    public void SettingsAreReadFromConfigurationSection()
    {
        var builder = CreateBuilder(
            ("Aspire:Zitadel:Client:Endpoint", Endpoint + "/"),
            ("Aspire:Zitadel:Client:PersonalAccessToken", "test-token"));

        builder.AddZitadelApi("zitadel");

        using var host = builder.Build();
        var options = host.Services.GetRequiredService<Clients.Options>();
        Assert.Equal(Endpoint, options.Endpoint);
        Assert.NotNull(options.TokenProvider);
    }

    [Fact]
    public void ConnectionStringOverridesConfiguredEndpoint()
    {
        var builder = CreateBuilder(
            ("Aspire:Zitadel:Client:Endpoint", "https://other:1234"),
            ("ConnectionStrings:zitadel", Endpoint));

        builder.AddZitadelApi("zitadel");

        using var host = builder.Build();
        Assert.Equal(Endpoint, host.Services.GetRequiredService<Clients.Options>().Endpoint);
    }

    [Fact]
    public void MissingEndpointThrowsOnResolve()
    {
        var builder = CreateBuilder();

        builder.AddZitadelApi("zitadel");

        using var host = builder.Build();
        Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<UserService.UserServiceClient>());
    }

    [Fact]
    public void InvalidServiceAccountKeyThrowsOnResolve()
    {
        var builder = CreateBuilder(("ConnectionStrings:zitadel", Endpoint));

        builder.AddZitadelApi("zitadel", settings => settings.ServiceAccountKey = "not-base64!");

        using var host = builder.Build();
        Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<Clients.Options>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HealthCheckIsRegisteredUnlessDisabled(bool disableHealthChecks)
    {
        var builder = CreateBuilder(("ConnectionStrings:zitadel", Endpoint));

        builder.AddZitadelApi("zitadel", settings => settings.DisableHealthChecks = disableHealthChecks);

        using var host = builder.Build();
        var registrations = host.Services.GetService<IOptions<HealthCheckServiceOptions>>()?.Value.Registrations ?? [];
        Assert.Equal(!disableHealthChecks, registrations.Any(r => r.Name == "zitadel"));
    }
}
