namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A resource that represents a Directus server.
/// </summary>
public class DirectusResource : ContainerResource, IResourceWithConnectionString
{
    internal const string PrimaryEndpointName = "http";
    private const string DefaultUserName = "directus@local.dev";

    /// <summary>
    /// Initializes a new <see cref="DirectusResource"/>.
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    /// <param name="userName">A parameter that contains the admin email, or <see langword="null"/> to use the default.</param>
    /// <param name="password">A parameter that contains the admin password.</param>
    /// <param name="adminToken">A parameter that contains the static admin API token.</param>
    public DirectusResource(string name, ParameterResource? userName, ParameterResource password, ParameterResource adminToken) : base(name)
    {
        UserNameParameter = userName;
        PasswordParameter = password;
        AdminTokenParameter = adminToken;
    }

    private EndpointReference? _primaryEndpoint;

    /// <summary>
    /// Gets the primary endpoint for the N8n. This endpoint is used for all API calls over HTTP.
    /// </summary>
    public EndpointReference PrimaryEndpoint => _primaryEndpoint ??= new(this, PrimaryEndpointName);

    /// <summary>
    /// Gets or sets the parameter that contains the Directus server user name.
    /// </summary>
    public ParameterResource? UserNameParameter { get; set; }

    /// <summary>
    /// Gets a reference to the user name for the Directus server.
    /// </summary>
    /// <remarks>
    /// Returns the user name parameter if specified, otherwise returns the default user name "postgres".
    /// </remarks>
    public ReferenceExpression UserNameReference =>
        UserNameParameter is not null ?
            ReferenceExpression.Create($"{UserNameParameter}") :
            ReferenceExpression.Create($"{DefaultUserName}");

    /// <summary>
    /// Gets or sets the parameter that contains the Directus server password.
    /// </summary>
    public ParameterResource PasswordParameter { get; set; }

    /// <summary>
    /// Gets or sets the parameter that contains the Directus server admin token.
    /// </summary>
    public ParameterResource AdminTokenParameter { get; set; }

    private ReferenceExpression ConnectionString =>
        ReferenceExpression.Create(
            $"BaseUrl={UriExpression};Token={AdminTokenParameter}");

    /// <summary>
    /// Gets the connection string expression for the Directus server.
    /// </summary>
    public ReferenceExpression ConnectionStringExpression
    {
        get
        {
            if (this.TryGetLastAnnotation<ConnectionStringRedirectAnnotation>(out var connectionStringAnnotation))
            {
                return connectionStringAnnotation.Resource.ConnectionStringExpression;
            }

            return ConnectionString;
        }
    }

    /// <summary>
    /// Gets the connection string for the Directus server.
    /// </summary>
    /// <param name="cancellationToken"> A <see cref="CancellationToken"/> to observe while waiting for the task to complete.</param>
    /// <returns>A connection string for the Directus server in the form "Host=host;Port=port;Username=postgres;Password=password".</returns>
    public ValueTask<string?> GetConnectionStringAsync(CancellationToken cancellationToken = default)
    {
        if (this.TryGetLastAnnotation<ConnectionStringRedirectAnnotation>(out var connectionStringAnnotation))
        {
            return connectionStringAnnotation.Resource.GetConnectionStringAsync(cancellationToken);
        }

        return ConnectionStringExpression.GetValueAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the host endpoint reference for this service.
    /// </summary>
    public EndpointReferenceExpression Host => PrimaryEndpoint.Property(EndpointProperty.Host);

    /// <summary>
    /// Gets the endpoint reference expression that identifies the port for this endpoint.
    /// </summary>
    public EndpointReferenceExpression Port => PrimaryEndpoint.Property(EndpointProperty.Port);


    /// <summary>
    /// Gets the connection URI expression for the Directus server.
    /// </summary>
    /// <remarks>
    /// Format: <c>{scheme}://{host}:{port}</c>.
    /// </remarks>
    public ReferenceExpression UriExpression => ReferenceExpression.Create($"{PrimaryEndpoint.Scheme}://{Host}:{Port}");

    ///// <summary>
    ///// Gets the connection URI expression for the Directus server.
    ///// </summary>
    ///// <remarks>
    ///// Format: <c>Directus://{user}:{password}@{host}:{port}</c>.
    ///// </remarks>
    //public ReferenceExpression UriExpression => BuildUri();

    //internal ReferenceExpression BuildUri(string? databaseName = null)
    //{
    //    var builder = new ReferenceExpressionBuilder();
    //    builder.AppendLiteral("Directus://");
    //    if (UserNameParameter is not null)
    //    {
    //        builder.Append($"{UserNameParameter:uri}:{PasswordParameter:uri}@{Host}:{Port}");
    //    }
    //    else
    //    {
    //        builder.Append($"{DefaultUserName:uri}:{PasswordParameter:uri}@{Host}:{Port}");
    //    }

    //    if (databaseName is not null)
    //    {
    //        builder.AppendLiteral("/");
    //        builder.Append($"{databaseName:uri}");
    //    }

    //    return builder.Build();
    //}

    //internal ReferenceExpression BuildJdbcConnectionString(string? databaseName = null)
    //{
    //    var builder = new ReferenceExpressionBuilder();
    //    builder.AppendLiteral("jdbc:Directus://");
    //    builder.Append($"{Host}:{Port}");

    //    if (databaseName is not null)
    //    {
    //        builder.Append($"/{databaseName:uri}");
    //    }

    //    return builder.Build();
    //}

    ///// <summary>
    ///// Gets the JDBC connection string for the CockroachDB server.
    ///// </summary>
    ///// <remarks>
    ///// <para>Format: <c>jdbc:Directus://{host}:{port}</c>.</para>
    ///// <para>User and password credentials are not included in the JDBC connection string. Use the <c>Username</c> and <c>Password</c> connection properties to access credentials.</para>
    ///// </remarks>
    //public ReferenceExpression JdbcConnectionString => BuildJdbcConnectionString();

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties() =>
    [
        new ("Host", ReferenceExpression.Create($"{Host}")),
        new ("Port", ReferenceExpression.Create($"{Port}")),
        new ("Uri", UriExpression),
        new ("Username", ReferenceExpression.Create($"{UserNameReference}")),
        new ("Password", ReferenceExpression.Create($"{PasswordParameter}")),
        new ("AdminToken", ReferenceExpression.Create($"{AdminTokenParameter}")),
    ];
}

