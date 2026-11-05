using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zemires.Aspire.Hosting.N8n;

// IUserSecretsManager is experimental; used intentionally to persist the generated API key.
#pragma warning disable ASPIREUSERSECRETS001

namespace Aspire.Hosting;

public static partial class N8nBuilderExtensions
{
    private const string DefaultApiKeyLabel = "Aspire AppHost";

    /// <summary>
    /// Creates an API key with all scopes for the public API once n8n is ready, signed in as the instance owner
    /// (<see cref="WithInstanceOwner"/>). The key is persisted in the AppHost user secrets (<c>Parameters:{name}-api-key</c>)
    /// and reused while n8n accepts it, and is added to the connection string (<c>Endpoint=...;ApiKey=...</c>).
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="label">The label of the key in n8n; earlier keys of the owner with this label are deleted. <c>Aspire AppHost</c> when <see langword="null"/>.</param>
    /// <returns>The resource builder.</returns>
    /// <remarks>
    /// Run mode only, it does nothing in publish mode. Uses n8n's internal REST API (<c>/rest/login</c>, <c>/rest/api-keys</c>),
    /// which is undocumented and may change between n8n versions.
    /// </remarks>
    /// <exception cref="InvalidOperationException">An API key is already configured.</exception>
    [AspireExport]
    public static IResourceBuilder<N8nResource> WithApiKey(this IResourceBuilder<N8nResource> builder, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        EnsureNoApiKey(builder.Resource);

        if (builder.ApplicationBuilder.ExecutionContext.IsPublishMode)
        {
            return builder;
        }

        var apiKey = new N8nApiKeyReference(builder.Resource, string.IsNullOrWhiteSpace(label) ? DefaultApiKeyLabel : label, parameter: null);
        builder.Resource.ApiKey = apiKey;

        return builder.OnResourceReady((n8n, @event, ct) => ProvisionApiKeyAsync(n8n, apiKey, @event.Services, ct));
    }

    /// <summary>
    /// Uses an existing API key for the public API and adds it to the connection string (<c>Endpoint=...;ApiKey=...</c>).
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="apiKey">The API key.</param>
    /// <returns>The resource builder.</returns>
    /// <exception cref="InvalidOperationException">An API key is already configured.</exception>
    [AspireExport("withApiKeyParameter")]
    public static IResourceBuilder<N8nResource> WithApiKey(this IResourceBuilder<N8nResource> builder, IResourceBuilder<ParameterResource> apiKey)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(apiKey);
        EnsureNoApiKey(builder.Resource);

        builder.Resource.ApiKey = new N8nApiKeyReference(builder.Resource, DefaultApiKeyLabel, apiKey.Resource);
        return builder;
    }

    private static void EnsureNoApiKey(N8nResource resource)
    {
        if (resource.ApiKey is not null)
        {
            throw new InvalidOperationException($"An API key is already configured for '{resource.Name}'.");
        }
    }

    private static async Task ProvisionApiKeyAsync(N8nResource n8n, N8nApiKeyReference apiKey, IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ResourceLoggerService>().GetLogger(n8n);
        try
        {
            using var handler = new SocketsHttpHandler { UseCookies = false };
            using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(n8n.PrimaryEndpoint.Url) };
            var provisioner = new N8nApiKeyProvisioner(httpClient, logger);
            await provisioner.WaitUntilStartedAsync(TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);

            var persisted = services.GetService<IConfiguration>()?[apiKey.ConfigurationKey];
            if (!string.IsNullOrEmpty(persisted) && await provisioner.IsValidAsync(persisted, ct).ConfigureAwait(false))
            {
                apiKey.Set(persisted);
                return;
            }

            if (n8n.InstanceOwnerEmail is null || n8n.InstanceOwnerPassword is null)
            {
                throw new InvalidOperationException($"'{n8n.Name}' has no instance owner, call WithInstanceOwner to create its API key.");
            }

            var password = await n8n.InstanceOwnerPassword.GetValueAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"The instance owner password of '{n8n.Name}' has no value.");
            var value = await provisioner.CreateApiKeyAsync(n8n.InstanceOwnerEmail, password, apiKey.Label, ct).ConfigureAwait(false);
            apiKey.Set(value);

            if (services.GetService<IUserSecretsManager>() is not { IsAvailable: true } userSecrets)
            {
                logger.LogWarning("User secrets are not available for the AppHost, the API key of '{Resource}' is not persisted.", n8n.Name);
            }
            else if (!userSecrets.TrySetSecret(apiKey.ConfigurationKey, value))
            {
                logger.LogWarning("Could not persist the API key of '{Resource}' in the user secrets.", n8n.Name);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Creating the API key of '{Resource}' failed.", n8n.Name);
            apiKey.Fail(new InvalidOperationException($"The API key of '{n8n.Name}' is not available, see the logs of '{n8n.Name}'.", ex));
        }
    }
}
