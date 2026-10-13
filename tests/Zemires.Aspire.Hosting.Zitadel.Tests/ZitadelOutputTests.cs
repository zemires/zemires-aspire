using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Zitadel.Api;
using Zitadel.Credentials;

#pragma warning disable ASPIREUSERSECRETS001

namespace Zemires.Aspire.Hosting.Zitadel.Tests;

public class ZitadelOutputTests
{
    [Fact]
    public void GetOutput_ReturnsSameInstancePerName()
    {
        using var builder = DistributedApplicationTestingBuilder.Create();
        var project = builder.AddZitadel("zitadel").AddProject("project", "Project");

        ZitadelOutputReference first = project.GetOutput("client-id");
        ZitadelOutputReference second = project.GetOutput("CLIENT-ID");

        Assert.Same(first, second);
        Assert.False(first.IsSecret);
        Assert.Same(project.Resource, first.Resource);
        Assert.Equal("{project.outputs.client-id}", first.ValueExpression);
        Assert.Equal("Parameters:project-client-id", first.ConfigurationKey);
    }

    [Fact]
    public void GetSecretOutput_MarksOutputAsSecret()
    {
        using var builder = DistributedApplicationTestingBuilder.Create();
        var zitadel = builder.AddZitadel("zitadel");

        Assert.True(zitadel.GetSecretOutput("secret").IsSecret);
    }

    [Fact]
    public async Task GetValueAsync_WaitsUntilSet()
    {
        using var builder = DistributedApplicationTestingBuilder.Create();
        ZitadelOutputReference output = builder.AddZitadel("zitadel").AddProject("project", "Project").GetOutput("client-id");

        Task<string?> pending = output.GetValueAsync(TestContext.Current.CancellationToken).AsTask();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(pending.IsCompleted);
        Assert.False(output.IsResolved);

        output.Set("abc");

        Assert.Equal("abc", await pending);
        Assert.True(output.IsResolved);
    }

    [Fact]
    public async Task GetValueAsync_ReturnsLatestValueAfterReinitialization()
    {
        using var builder = DistributedApplicationTestingBuilder.Create();
        ZitadelOutputReference output = builder.AddZitadel("zitadel").GetOutput("value");

        output.Set("first");
        output.Set("second");

        Assert.Equal("second", await output.GetValueAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetValueAsync_ThrowsWhenFailed()
    {
        using var builder = DistributedApplicationTestingBuilder.Create();
        ZitadelOutputReference output = builder.AddZitadel("zitadel").GetOutput("value");

        Task<string?> pending = output.GetValueAsync(TestContext.Current.CancellationToken).AsTask();
        output.Fail(new InvalidOperationException("init failed"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Equal("init failed", exception.Message);
    }

    [Fact]
    public async Task GetValueAsync_HonorsCancellation()
    {
        using var builder = DistributedApplicationTestingBuilder.Create();
        ZitadelOutputReference output = builder.AddZitadel("zitadel").GetOutput("value");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => output.GetValueAsync(cts.Token).AsTask());
    }

    [Fact]
    public async Task WithOutput_ResolvesFromParameter()
    {
        using var builder = DistributedApplicationTestingBuilder.Create();
        builder.Configuration["Parameters:existing-secret"] = "from-parameter";
        var parameter = builder.AddParameter("existing-secret", secret: true);
        var project = builder.AddZitadel("zitadel").AddProject("project", "Project")
            .WithOutput("client-secret", parameter);

        ZitadelOutputReference output = project.GetOutput("client-secret");

        Assert.Same(parameter.Resource, output.Parameter);
        Assert.True(output.IsSecret);
        Assert.Equal("{existing-secret.value}", output.ValueExpression);
        Assert.Equal("from-parameter", await output.GetValueAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void WithEnvironment_UsesOutputAsValue()
    {
        using var builder = DistributedApplicationTestingBuilder.Create();
        ZitadelOutputReference output = builder.AddZitadel("zitadel").AddProject("project", "Project").GetSecretOutput("client-secret");

        var container = builder.AddContainer("web", "image").WithEnvironment("ClientSecret", output);

        Assert.True(container.Resource.TryGetAnnotationsOfType<EnvironmentCallbackAnnotation>(out var annotations));
        var context = new EnvironmentCallbackContext(builder.ExecutionContext);
        foreach (EnvironmentCallbackAnnotation annotation in annotations)
        {
            annotation.Callback(context);
        }

        Assert.Same(output, context.EnvironmentVariables["ClientSecret"]);
    }

    [Fact]
    public async Task Context_SetOutput_ResolvesAndPersistsInUserSecrets()
    {
        using var builder = DistributedApplicationTestingBuilder.Create();
        var project = builder.AddZitadel("zitadel").AddProject("project", "Project");
        ZitadelOutputReference output = project.GetSecretOutput("client-secret");
        var userSecrets = new FakeUserSecretsManager();
        ZitadelInitializationContext context = CreateContext(project.Resource, new Dictionary<string, string?>(), userSecrets);

        context.SetOutput("client-secret", "s3cret");

        Assert.Equal("s3cret", await output.GetValueAsync(TestContext.Current.CancellationToken));
        Assert.Equal("s3cret", userSecrets.Secrets["Parameters:project-client-secret"]);
        Assert.Equal("s3cret", await context.GetOutputValueAsync("client-secret"));
    }

    [Fact]
    public async Task Context_SetOutput_IgnoresParameterBoundOutput()
    {
        using var builder = DistributedApplicationTestingBuilder.Create();
        builder.Configuration["Parameters:existing-secret"] = "from-parameter";
        var project = builder.AddZitadel("zitadel").AddProject("project", "Project")
            .WithOutput("client-secret", builder.AddParameter("existing-secret", secret: true));
        var userSecrets = new FakeUserSecretsManager();
        ZitadelInitializationContext context = CreateContext(project.Resource, new Dictionary<string, string?>(), userSecrets);

        context.SetOutput("client-secret", "generated");

        Assert.Empty(userSecrets.Secrets);
        Assert.Equal("from-parameter", await context.GetOutputValueAsync("client-secret"));
    }

    [Fact]
    public async Task Context_GetOutputValueAsync_ReadsPersistedValue()
    {
        using var builder = DistributedApplicationTestingBuilder.Create();
        var project = builder.AddZitadel("zitadel").AddProject("project", "Project");
        project.GetOutput("client-id");
        ZitadelInitializationContext context = CreateContext(project.Resource,
            new Dictionary<string, string?> { ["Parameters:project-client-id"] = "persisted" }, new FakeUserSecretsManager());

        Assert.Equal("persisted", await context.GetOutputValueAsync("client-id"));
        Assert.Null(await context.GetOutputValueAsync("unknown"));
    }

    private static ZitadelInitializationContext CreateContext(IResource resource, Dictionary<string, string?> configuration, IUserSecretsManager userSecrets)
    {
        ServiceProvider services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build())
            .AddSingleton(userSecrets)
            .BuildServiceProvider();

        var options = new Clients.Options("https://localhost:8080", ITokenProvider.Static("token"));
        return new ZitadelInitializationContext(options, resource, services, NullLogger.Instance, TestContext.Current.CancellationToken);
    }

    private sealed class FakeUserSecretsManager : IUserSecretsManager
    {
        public Dictionary<string, string> Secrets { get; } = new();

        public bool IsAvailable => true;

        public string FilePath => "secrets.json";

        public bool TrySetSecret(string name, string value)
        {
            Secrets[name] = value;
            return true;
        }

        public void GetOrSetSecret(IConfigurationManager configuration, string name, Func<string> valueGenerator) =>
            throw new NotSupportedException();

        public Task SaveStateAsync(JsonObject state, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
