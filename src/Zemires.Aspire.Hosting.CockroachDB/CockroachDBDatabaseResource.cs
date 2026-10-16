using System.Data.Common;
using System.Diagnostics;

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A CockroachDB database, child of a <see cref="CockroachDBResource"/>.
/// </summary>
/// <param name="name">The resource name.</param>
/// <param name="databaseName">The database name.</param>
/// <param name="cockroachDBParentResource">The CockroachDB server.</param>
[DebuggerDisplay("Type = {GetType().Name,nq}, Name = {Name}, Database = {DatabaseName}")]
public class CockroachDBDatabaseResource(string name, string databaseName, CockroachDBResource cockroachDBParentResource)
    : Resource(name), IResourceWithParent<CockroachDBResource>, IResourceWithConnectionString
{
    /// <summary>
    /// Gets the CockroachDB server.
    /// </summary>
    public CockroachDBResource Parent { get; } = cockroachDBParentResource ?? throw new ArgumentNullException(nameof(cockroachDBParentResource));

    /// <summary>
    /// Gets the database name.
    /// </summary>
    public string DatabaseName { get; } = string.IsNullOrEmpty(databaseName) ? throw new ArgumentException("The database name must not be empty.", nameof(databaseName)) : databaseName;

    /// <summary>
    /// Gets the connection string of the server with <c>Database={DatabaseName}</c>.
    /// </summary>
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"{Parent};{new DbConnectionStringBuilder { ["Database"] = DatabaseName }.ToString()}");

    /// <summary>
    /// Gets the URI <c>postgresql://{user}:{password}@{host}:{port}/{database}</c>.
    /// </summary>
    public ReferenceExpression UriExpression => Parent.BuildUri(DatabaseName);

    /// <summary>
    /// Gets the JDBC connection string <c>jdbc:postgresql://{host}:{port}/{database}</c>, without credentials.
    /// </summary>
    public ReferenceExpression JdbcConnectionString => Parent.BuildJdbcConnectionString(DatabaseName);

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties() =>
        Parent.CombineProperties([
            new("DatabaseName", ReferenceExpression.Create($"{DatabaseName}")),
            new("Uri", UriExpression),
            new("JdbcConnectionString", JdbcConnectionString),
        ]);
}
