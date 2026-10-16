using Aspire.Hosting.ApplicationModel;

#pragma warning disable IDE0130
namespace Aspire.Hosting;
#pragma warning restore IDE0130

/// <summary>
/// A reference to an output of a Terraform template, written to the manifest as <c>{resource.outputs.name}</c>.
/// </summary>
/// <param name="name">The output name.</param>
/// <param name="resource">The resource exposing the output.</param>
public sealed class TerraformOutputReference(string name, ITerraformResourceWithOutputs resource)
    : IManifestExpressionProvider, IValueProvider, IValueWithReferences, IEquatable<TerraformOutputReference>
{
    /// <summary>
    /// Gets the output name.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets the resource exposing the output.
    /// </summary>
    public ITerraformResourceWithOutputs Resource { get; } = resource;

    IEnumerable<object> IValueWithReferences.References => [Resource];

    /// <summary>
    /// Gets the output value.
    /// </summary>
    /// <exception cref="InvalidOperationException">The resource has no such output.</exception>
    public string? Value => TerraformOutputs.GetOutputValue(Resource, Name);

    /// <inheritdoc/>
    public string ValueExpression => $"{{{Resource.Name}.outputs.{Name}}}";

    /// <inheritdoc/>
    public ValueTask<string?> GetValueAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Value);

    /// <inheritdoc/>
    public bool Equals(TerraformOutputReference? other) =>
        other is not null && other.Resource == Resource && other.Name == Name;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as TerraformOutputReference);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Resource, Name);
}
