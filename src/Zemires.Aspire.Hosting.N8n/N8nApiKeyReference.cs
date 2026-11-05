namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// The API key of an n8n resource for the public API (<c>X-N8N-API-KEY</c>), configured with <c>WithApiKey</c>.
/// A generated key is resolved lazily: consumers evaluating it before n8n is ready wait until it is created.
/// </summary>
/// <remarks>
/// Part of the connection string of the n8n resource (<c>Endpoint=...;ApiKey=...</c>). Use it like any other value with
/// <c>WithEnvironment(name, apiKey)</c> or inside a <see cref="ReferenceExpression"/>.
/// </remarks>
public sealed class N8nApiKeyReference : IValueProvider, IManifestExpressionProvider, IValueWithReferences
{
    internal const string OutputName = "api-key";

    private readonly object _lock = new();
    private TaskCompletionSource<string> _value = NewCompletionSource();

    internal N8nApiKeyReference(N8nResource resource, string label, ParameterResource? parameter)
    {
        Resource = resource;
        Label = label;
        Parameter = parameter;
    }

    /// <summary>
    /// Gets the n8n resource the key belongs to.
    /// </summary>
    public N8nResource Resource { get; }

    /// <summary>
    /// Gets the label of the generated key in n8n.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the parameter providing the key, or <see langword="null"/> when the key is generated.
    /// </summary>
    public ParameterResource? Parameter { get; }

    /// <summary>
    /// Gets the configuration key used to persist a generated key (user secrets), <c>Parameters:{resource}-api-key</c>.
    /// </summary>
    public string ConfigurationKey => $"Parameters:{Resource.Name}-{OutputName}";

    /// <summary>
    /// Gets a value indicating whether a generated key has been created or read.
    /// </summary>
    public bool IsResolved
    {
        get
        {
            lock (_lock)
            {
                return _value.Task.IsCompletedSuccessfully;
            }
        }
    }

    /// <inheritdoc />
    public string ValueExpression => Parameter?.ValueExpression ?? $"{{{Resource.Name}.outputs.{OutputName}}}";

    /// <inheritdoc />
    public IEnumerable<object> References => Parameter is not null ? [Parameter] : [Resource];

    /// <summary>
    /// Gets the key. Waits until a generated key is created.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The API key.</returns>
    public async ValueTask<string?> GetValueAsync(CancellationToken cancellationToken = default)
    {
        if (Parameter is not null)
        {
            return await Parameter.GetValueAsync(cancellationToken).ConfigureAwait(false);
        }

        Task<string> task;
        lock (_lock)
        {
            task = _value.Task;
        }

        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the key for <see cref="ValueProviderContext.Caller"/>. Without caller, or for the n8n resource itself, a generated
    /// key that does not exist yet is returned as <see langword="null"/> instead of waiting: Aspire resolves the connection
    /// string of n8n (for the dashboard) before it creates the container, so waiting there would never end.
    /// </summary>
    /// <param name="context">The context of the resolution.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The API key.</returns>
    public ValueTask<string?> GetValueAsync(ValueProviderContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Parameter is null && (context.Caller is null || context.Caller == Resource))
        {
            lock (_lock)
            {
                if (!_value.Task.IsCompletedSuccessfully)
                {
                    return ValueTask.FromResult<string?>(null);
                }
            }
        }

        return GetValueAsync(cancellationToken);
    }

    internal void Set(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        lock (_lock)
        {
            if (!_value.TrySetResult(value))
            {
                // restart of the n8n resource: replace the completed value
                _value = NewCompletionSource();
                _value.SetResult(value);
            }
        }
    }

    internal void Fail(Exception exception)
    {
        lock (_lock)
        {
            if (!_value.TrySetException(exception))
            {
                _value = NewCompletionSource();
                _value.SetException(exception);
            }
        }
    }

    private static TaskCompletionSource<string> NewCompletionSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
