namespace Zemires.Aspire.Hosting.CockroachDB.Tests;

public class CockroachDBBuilderExtensionsTests
{
    [Fact]
    public async Task AddCockroachDB_RunsSecureNodeWithCertificatesInDataFolder()
    {
        using var builder = DistributedApplicationTestingBuilder.Create();
        var roach = builder.AddCockroachDB("roach");

        var args = await GetArgumentsAsync(roach.Resource);

        Assert.Equal(["start-single-node", "--certs-dir=/cockroach/cockroach-data/certs"], args);
    }

    [Fact]
    public void AddCockroachDB_SetsUserPasswordAndSkipsKeyPermissionCheck()
    {
        using var builder = DistributedApplicationTestingBuilder.Create();
        var password = builder.AddParameter("pwd", "secret", secret: true);
        var roach = builder.AddCockroachDB("roach", password: password);

        var environment = GetEnvironment(builder, roach.Resource);

        Assert.Equal("cockroach", Assert.IsType<ReferenceExpression>(environment["COCKROACH_USER"]).ValueExpression);
        Assert.Same(password.Resource, environment["COCKROACH_PASSWORD"]);
        Assert.Equal("true", environment["COCKROACH_SKIP_KEY_PERMISSION_CHECK"]);
    }

    [Fact]
    public void WithUserNameAndPassword_ReplaceParametersInEnvironment()
    {
        using var builder = DistributedApplicationTestingBuilder.Create();
        var user = builder.AddParameter("user", "app");
        var password = builder.AddParameter("pwd", "secret", secret: true);
        var roach = builder.AddCockroachDB("roach").WithUserName(user).WithPassword(password);

        var environment = GetEnvironment(builder, roach.Resource);

        Assert.Equal("{user.value}", Assert.IsType<ReferenceExpression>(environment["COCKROACH_USER"]).ValueExpression);
        Assert.Same(password.Resource, environment["COCKROACH_PASSWORD"]);
    }

    [Fact]
    public void AddCockroachDB_ExposesSqlEndpointAndHttpsConsole()
    {
        using var builder = DistributedApplicationTestingBuilder.Create();
        var roach = builder.AddCockroachDB("roach", port: 26000, consolePort: 8000);

        var endpoints = roach.Resource.Annotations.OfType<EndpointAnnotation>().ToDictionary(e => e.Name);

        Assert.Equal(26257, endpoints["tcp"].TargetPort);
        Assert.Equal(26000, endpoints["tcp"].Port);
        Assert.Equal("https", endpoints["https"].UriScheme);
        Assert.Equal(8080, endpoints["https"].TargetPort);
        Assert.Equal(8000, endpoints["https"].Port);
        Assert.Equal("https", roach.Resource.ConsoleEndpoint.EndpointName);
    }

    [Fact]
    public void AddDatabase_RegistersDatabaseWithConnectionString()
    {
        using var builder = DistributedApplicationTestingBuilder.Create();
        var roach = builder.AddCockroachDB("roach");
        var db = roach.AddDatabase("db", "appdb");

        Assert.Equal("appdb", db.Resource.DatabaseName);
        Assert.Equal("appdb", roach.Resource.Databases["db"]);
        Assert.Equal(
            "Host={roach.bindings.tcp.host};Port={roach.bindings.tcp.port};Username=cockroach;Password={roach-password.value}",
            roach.Resource.ConnectionStringExpression.ValueExpression);
        Assert.Contains("Database=appdb", db.Resource.ConnectionStringExpression.ValueExpression);
    }

    private static async Task<List<string>> GetArgumentsAsync(IResource resource)
    {
        var args = new List<object>();
        var context = new CommandLineArgsCallbackContext(args, resource, TestContext.Current.CancellationToken);
        foreach (var annotation in resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return args.Select(a => a.ToString()!).ToList();
    }

    private static Dictionary<string, object> GetEnvironment(IDistributedApplicationTestingBuilder builder, IResource resource)
    {
        var context = new EnvironmentCallbackContext(builder.ExecutionContext);
        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            annotation.Callback(context);
        }

        return context.EnvironmentVariables;
    }
}
