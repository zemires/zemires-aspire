namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A Directus container resource. Its connection string is <c>BaseUrl={url};Token={admin token}</c>.
/// </summary>
/// <param name="name">The resource name.</param>
/// <param name="userName">The admin email; <c>directus@local.dev</c> when <see langword="null"/>.</param>
/// <param name="password">The admin password.</param>
/// <param name="adminToken">The static admin API token.</param>
public class DirectusResource(string name, ParameterResource? userName, ParameterResource password, ParameterResource adminToken)
    : ContainerResource(name), IResourceWithConnectionString
{
    internal const string PrimaryEndpointName = "http";
    private const string DefaultUserName = "directus@local.dev";

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
    /// Gets or sets the admin email parameter, or <see langword="null"/> for the default <c>directus@local.dev</c>.
    /// </summary>
    public ParameterResource? UserNameParameter { get; set; } = userName;

    /// <summary>
    /// Gets the admin email.
    /// </summary>
    public ReferenceExpression UserNameReference =>
        UserNameParameter is not null
            ? ReferenceExpression.Create($"{UserNameParameter}")
            : ReferenceExpression.Create($"{DefaultUserName}");

    /// <summary>
    /// Gets or sets the admin password parameter.
    /// </summary>
    public ParameterResource PasswordParameter { get; set; } = password;

    /// <summary>
    /// Gets or sets the static admin API token parameter.
    /// </summary>
    public ParameterResource AdminTokenParameter { get; set; } = adminToken;

    /// <summary>
    /// Gets the connection string <c>BaseUrl={url};Token={admin token}</c>.
    /// </summary>
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"BaseUrl={UriExpression};Token={AdminTokenParameter}");

    /// <summary>
    /// Resolves the connection string.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The connection string <c>BaseUrl={url};Token={admin token}</c>.</returns>
    public ValueTask<string?> GetConnectionStringAsync(CancellationToken cancellationToken = default) =>
        ConnectionStringExpression.GetValueAsync(cancellationToken);

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties() =>
    [
        new("Host", ReferenceExpression.Create($"{Host}")),
        new("Port", ReferenceExpression.Create($"{Port}")),
        new("Uri", UriExpression),
        new("Username", UserNameReference),
        new("Password", ReferenceExpression.Create($"{PasswordParameter}")),
        new("AdminToken", ReferenceExpression.Create($"{AdminTokenParameter}")),
    ];
}
