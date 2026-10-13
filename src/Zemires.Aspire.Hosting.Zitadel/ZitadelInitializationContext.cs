using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zemires.Aspire.Hosting.Zitadel;
using Zitadel.Api;

// IUserSecretsManager is experimental; used intentionally to persist initialization outputs.
#pragma warning disable ASPIREUSERSECRETS001

// ReSharper disable once CheckNamespace
namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// Context passed to Zitadel initialization callbacks configured with <c>WithInitialization</c>.
/// Provides the authenticated API client options and access to the outputs of the initialized resource.
/// </summary>
public sealed class ZitadelInitializationContext
{
    private readonly IConfiguration? _configuration;
    private readonly IUserSecretsManager? _userSecretsManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZitadelInitializationContext" /> class.
    /// </summary>
    /// <param name="options">The Zitadel API client options, authenticated as the machine user.</param>
    /// <param name="resource">The initialized resource, a <see cref="ZitadelResource" /> or <see cref="ZitadelProjectResource" />.</param>
    /// <param name="services">The application services.</param>
    /// <param name="logger">The logger of the initialized resource.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ZitadelInitializationContext(Clients.Options options, IResource resource, IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        Options = options;
        Resource = resource;
        Services = services;
        Logger = logger;
        CancellationToken = cancellationToken;
        _configuration = services.GetService<IConfiguration>();
        _userSecretsManager = services.GetService<IUserSecretsManager>();
    }

    /// <summary>
    /// Gets the Zitadel API client options, authenticated as the machine user.
    /// </summary>
    public Clients.Options Options { get; }

    /// <summary>
    /// Gets the initialized resource, a <see cref="ZitadelResource" /> or <see cref="ZitadelProjectResource" />.
    /// </summary>
    public IResource Resource { get; }

    /// <summary>
    /// Gets the application services.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Gets the logger of the initialized resource (shown in the dashboard).
    /// </summary>
    public ILogger Logger { get; }

    /// <summary>
    /// Gets the cancellation token.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Sets the value of an output of the initialized resource and persists it in the AppHost user secrets
    /// (key <c>Parameters:{resource}-{output}</c>), so it is known on the next start.
    /// Outputs bound to a parameter with <c>WithOutput</c> are not changed. Undeclared outputs are treated as secrets.
    /// </summary>
    /// <param name="name">The name of the output.</param>
    /// <param name="value">The value.</param>
    public void SetOutput(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);

        ZitadelOutputReference output = ZitadelOutputsAnnotation.Find(Resource, name) ?? ZitadelOutputsAnnotation.GetOrAdd(Resource, name, isSecret: true);
        if (output.Parameter is not null)
        {
            Logger.LogWarning("Output '{Output}' of '{Resource}' is bound to parameter '{Parameter}', the value set by the initialization is ignored.",
                name, Resource.Name, output.Parameter.Name);
            return;
        }

        output.Set(value);

        if (_userSecretsManager is not { IsAvailable: true })
        {
            Logger.LogWarning("User secrets are not available for the AppHost, output '{Output}' of '{Resource}' is not persisted.", name, Resource.Name);
            return;
        }

        if (_configuration?[output.ConfigurationKey] != value && !_userSecretsManager.TrySetSecret(output.ConfigurationKey, value))
        {
            Logger.LogWarning("Could not persist output '{Output}' of '{Resource}' in the user secrets.", name, Resource.Name);
        }
    }

    /// <summary>
    /// Gets the value of an output if it is already known, without waiting: the value set during this initialization,
    /// the bound parameter, or the value persisted from a previous start (configuration key <c>Parameters:{resource}-{output}</c>).
    /// </summary>
    /// <param name="name">The name of the output.</param>
    /// <returns>The known value, or <c>null</c>.</returns>
    public async Task<string?> GetOutputValueAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        ZitadelOutputReference? output = ZitadelOutputsAnnotation.Find(Resource, name);
        if (output?.Parameter is not null)
        {
            return await output.Parameter.GetValueAsync(CancellationToken).ConfigureAwait(false);
        }

        if (output is not null && output.TryGetResolvedValue(out string? value))
        {
            return value;
        }

        return GetPersistedValue(Resource, name);
    }

    internal string? GetPersistedValue(IResource resource, string name)
    {
        string? value = _configuration?[$"Parameters:{resource.Name}-{name}"];
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
