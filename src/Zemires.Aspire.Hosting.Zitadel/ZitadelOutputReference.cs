// ReSharper disable once CheckNamespace
namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A value produced by the initialization of a Zitadel resource or project (e.g. the client id or secret of an application
/// created by an initialization callback). The value is resolved lazily: consumers evaluating it before the initialization
/// has completed wait until it is set, instead of receiving an empty value.
/// </summary>
/// <remarks>
/// Use it like any other value with <c>WithEnvironment(name, output)</c> or inside a <see cref="ReferenceExpression" />.
/// When bound to a parameter with <c>WithOutput</c>, the parameter value is used and the initialization does not need to set it.
/// </remarks>
public sealed class ZitadelOutputReference : IValueProvider, IManifestExpressionProvider, IValueWithReferences
{
    private readonly object _lock = new();
    private TaskCompletionSource<string> _value = NewCompletionSource();

    internal ZitadelOutputReference(IResource resource, string name, bool isSecret)
    {
        Resource = resource;
        Name = name;
        IsSecret = isSecret;
    }

    /// <summary>
    /// Gets the resource whose initialization produces this output.
    /// </summary>
    public IResource Resource { get; }

    /// <summary>
    /// Gets the name of the output.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets a value indicating whether the output is a secret.
    /// </summary>
    public bool IsSecret { get; internal set; }

    /// <summary>
    /// Gets the parameter providing the value, when bound with <c>WithOutput</c>.
    /// </summary>
    public ParameterResource? Parameter { get; internal set; }

    /// <summary>
    /// Gets the configuration key used to persist the value (user secrets) and to override it from configuration,
    /// in the format <c>Parameters:{resource}-{output}</c>.
    /// </summary>
    public string ConfigurationKey => $"Parameters:{Resource.Name}-{Name}";

    /// <summary>
    /// Gets a value indicating whether the value has been resolved by the initialization.
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
    public string ValueExpression => Parameter?.ValueExpression ?? $"{{{Resource.Name}.outputs.{Name}}}";

    /// <inheritdoc />
    public IEnumerable<object> References => Parameter is not null ? [Parameter] : [Resource];

    /// <summary>
    /// Gets the value. Waits until the initialization has set it, unless the output is bound to a parameter.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The value of the output.</returns>
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

    internal bool TryGetResolvedValue(out string? value)
    {
        lock (_lock)
        {
            if (_value.Task.IsCompletedSuccessfully)
            {
                value = _value.Task.Result;
                return true;
            }
        }

        value = null;
        return false;
    }

    internal void Set(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_lock)
        {
            if (!_value.TrySetResult(value))
            {
                // re-initialization (e.g. restart of the Zitadel resource): replace the completed value
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
