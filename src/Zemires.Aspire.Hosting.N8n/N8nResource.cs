namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// An n8n container resource. Its connection string is the endpoint URL <c>{scheme}://{host}:{port}</c>.
/// </summary>
/// <param name="name">The resource name.</param>
/// <param name="encryptionKeyParameter">The n8n encryption key.</param>
public class N8nResource(string name, ParameterResource encryptionKeyParameter) : ContainerResource(name), IResourceWithConnectionString
{
    internal const string PrimaryEndpointName = "http";

    private EndpointReference? _primaryEndpoint;

    /// <summary>
    /// Gets the HTTP endpoint (UI and API).
    /// </summary>
    public EndpointReference PrimaryEndpoint => _primaryEndpoint ??= new(this, PrimaryEndpointName);

    /// <summary>
    /// Gets the host of <see cref="PrimaryEndpoint"/>.
    /// </summary>
    public EndpointReferenceExpression Host => PrimaryEndpoint.Property(EndpointProperty.Host);

    /// <summary>
    /// Gets the port of <see cref="PrimaryEndpoint"/>.
    /// </summary>
    public EndpointReferenceExpression Port => PrimaryEndpoint.Property(EndpointProperty.Port);

    /// <summary>
    /// Gets the endpoint URL <c>{scheme}://{host}:{port}</c>.
    /// </summary>
    public ReferenceExpression UriExpression => ReferenceExpression.Create($"{PrimaryEndpoint.Property(EndpointProperty.Scheme)}://{Host}:{Port}");

    /// <summary>
    /// Gets the connection string, equal to <see cref="UriExpression"/>.
    /// </summary>
    public ReferenceExpression ConnectionStringExpression => UriExpression;

    /// <summary>
    /// Gets the encryption key parameter.
    /// </summary>
    public ParameterResource EncryptionKeyParameter { get; } = encryptionKeyParameter;

    /// <summary>
    /// Gets the instance owner password, or <see langword="null"/> without <see cref="N8nBuilderExtensions.WithInstanceOwner"/>.
    /// </summary>
    public ParameterResource? InstanceOwnerPassword { get; internal set; }

    /// <summary>
    /// Gets the auth token shared with task runners, or <see langword="null"/> without <see cref="N8nBuilderExtensions.AddTaskRunner"/>.
    /// </summary>
    public ParameterResource? RunnersAuthTokenParameter { get; internal set; }

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties()
    {
        yield return new("Host", ReferenceExpression.Create($"{Host}"));
        yield return new("Port", ReferenceExpression.Create($"{Port}"));
        yield return new("Uri", UriExpression);
    }
}
