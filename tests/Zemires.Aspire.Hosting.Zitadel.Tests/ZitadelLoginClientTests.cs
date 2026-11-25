using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace Zemires.Aspire.Hosting.Zitadel.Tests;

public class ZitadelLoginClientTests
{
    [Fact]
    public async Task AddZitadelLoginClient_GeneratesSessionCookieSecret()
    {
        using var builder = DistributedApplicationTestingBuilder.Create();
        var login = builder.AddZitadel("zitadel").WithLoginClientKey()
            .AddZitadelLoginClient("zitadel-login", serviceAccessToken: builder.AddParameter("token", "pat", secret: true));

        ParameterResource? secret = login.Resource.SessionCookieSecret;
        Assert.NotNull(secret);
        Assert.True(secret.Secret);
        Assert.Equal("zitadel-login-session-cookie-secret", secret.Name);

        string? value = await secret.GetValueAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(value);
        Assert.True(value.Length >= 32);
        Assert.DoesNotContain(',', value);

        Dictionary<string, object> env = await GetEnvironmentAsync(builder, login.Resource);
        Assert.Same(secret, env["ZITADEL_SESSION_COOKIE_SECRET"]);
    }

    [Fact]
    public async Task WithSessionCookieSecret_ReplacesGeneratedSecret()
    {
        using var builder = DistributedApplicationTestingBuilder.Create();
        var custom = builder.AddParameter("cookie-secret", "0123456789abcdef0123456789abcdef", secret: true);
        var login = builder.AddZitadel("zitadel").WithLoginClientKey()
            .AddZitadelLoginClient("zitadel-login", serviceAccessToken: builder.AddParameter("token", "pat", secret: true))
            .WithSessionCookieSecret(custom);

        Assert.Same(custom.Resource, login.Resource.SessionCookieSecret);

        Dictionary<string, object> env = await GetEnvironmentAsync(builder, login.Resource);
        Assert.Same(custom.Resource, env["ZITADEL_SESSION_COOKIE_SECRET"]);
    }

    private static async Task<Dictionary<string, object>> GetEnvironmentAsync(IDistributedApplicationTestingBuilder builder, IResource resource)
    {
        var context = new EnvironmentCallbackContext(builder.ExecutionContext);
        foreach (EnvironmentCallbackAnnotation annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return context.EnvironmentVariables;
    }
}
