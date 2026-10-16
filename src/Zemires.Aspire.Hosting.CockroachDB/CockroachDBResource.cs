namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A CockroachDB container resource. Its connection string is <c>Host=...;Port=...;Username=...;Password=...</c> (PostgreSQL wire protocol).
/// </summary>
/// <param name="name">The resource name.</param>
/// <param name="userName">The user name; <c>cockroach</c> when <see langword="null"/>.</param>
/// <param name="password">The password.</param>
public class CockroachDBResource(string name, ParameterResource? userName, ParameterResource password) : ContainerResource(name), IResourceWithConnectionString
{
    internal const string PrimaryEndpointName = "tcp";
    internal const string ConsoleEndpointName = "http";
    private const string DefaultUserName = "cockroach";

    private readonly Dictionary<string, string> _databases = new(StringComparer.OrdinalIgnoreCase);
    private EndpointReference? _primaryEndpoint;
    private EndpointReference? _consoleEndpoint;

    /// <summary>
    /// Gets the SQL endpoint.
    /// </summary>
    public EndpointReference PrimaryEndpoint => _primaryEndpoint ??= new(this, PrimaryEndpointName);

    /// <summary>
    /// Gets the DB console (HTTP) endpoint.
    /// </summary>
    public EndpointReference ConsoleEndpoint => _consoleEndpoint ??= new(this, ConsoleEndpointName);

    /// <summary>
    /// Gets the host of <see cref="PrimaryEndpoint"/>.
    /// </summary>
    public EndpointReferenceExpression Host => PrimaryEndpoint.Property(EndpointProperty.Host);

    /// <summary>
    /// Gets the port of <see cref="PrimaryEndpoint"/>.
    /// </summary>
    public EndpointReferenceExpression Port => PrimaryEndpoint.Property(EndpointProperty.Port);

    /// <summary>
    /// Gets or sets the user name parameter, or <see langword="null"/> for the default <c>cockroach</c>.
    /// </summary>
    public ParameterResource? UserNameParameter { get; set; } = userName;

    /// <summary>
    /// Gets the user name.
    /// </summary>
    public ReferenceExpression UserNameReference =>
        UserNameParameter is not null
            ? ReferenceExpression.Create($"{UserNameParameter}")
            : ReferenceExpression.Create($"{DefaultUserName}");

    /// <summary>
    /// Gets or sets the password parameter.
    /// </summary>
    public ParameterResource PasswordParameter { get; set; } = password;

    /// <summary>
    /// Gets the connection string <c>Host=...;Port=...;Username=...;Password=...</c>.
    /// </summary>
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"Host={Host};Port={Port};Username={UserNameReference};Password={PasswordParameter}");

    /// <summary>
    /// Resolves the connection string.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The connection string <c>Host=...;Port=...;Username=...;Password=...</c>.</returns>
    public ValueTask<string?> GetConnectionStringAsync(CancellationToken cancellationToken = default) =>
        ConnectionStringExpression.GetValueAsync(cancellationToken);

    /// <summary>
    /// Gets the databases added with <c>AddDatabase</c> (resource name to database name).
    /// </summary>
    public IReadOnlyDictionary<string, string> Databases => _databases;

    /// <summary>
    /// Gets the URI <c>postgresql://{user}:{password}@{host}:{port}</c>.
    /// </summary>
    public ReferenceExpression UriExpression => BuildUri();

    /// <summary>
    /// Gets the JDBC connection string <c>jdbc:postgresql://{host}:{port}</c>, without credentials (see the <c>Username</c> and <c>Password</c> connection properties).
    /// </summary>
    public ReferenceExpression JdbcConnectionString => BuildJdbcConnectionString();

    internal void AddDatabase(string name, string databaseName) => _databases.TryAdd(name, databaseName);

    internal ReferenceExpression BuildUri(string? databaseName = null)
    {
        var builder = new ReferenceExpressionBuilder();
        builder.AppendLiteral("postgresql://");
        if (UserNameParameter is not null)
        {
            builder.Append($"{UserNameParameter:uri}:{PasswordParameter:uri}@{Host}:{Port}");
        }
        else
        {
            builder.Append($"{DefaultUserName:uri}:{PasswordParameter:uri}@{Host}:{Port}");
        }

        if (databaseName is not null)
        {
            builder.AppendLiteral("/");
            builder.Append($"{databaseName:uri}");
        }

        return builder.Build();
    }

    internal ReferenceExpression BuildJdbcConnectionString(string? databaseName = null)
    {
        var builder = new ReferenceExpressionBuilder();
        builder.AppendLiteral("jdbc:postgresql://");
        builder.Append($"{Host}:{Port}");

        if (databaseName is not null)
        {
            builder.Append($"/{databaseName:uri}");
        }

        return builder.Build();
    }

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties() =>
    [
        new("Host", ReferenceExpression.Create($"{Host}")),
        new("Port", ReferenceExpression.Create($"{Port}")),
        new("Username", UserNameReference),
        new("Password", ReferenceExpression.Create($"{PasswordParameter}")),
        new("Uri", UriExpression),
        new("JdbcConnectionString", JdbcConnectionString),
    ];
}
