using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A resource that represents a CockroachDB database. This is a child resource of a <see cref="CockroachDBResource"/>.
/// </summary>
/// <param name="name">The name of the resource.</param>
/// <param name="databaseName">The database name.</param>
/// <param name="cockroachDBParentResource">The CockroachDB parent resource associated with this database.</param>
/// <ats-summary>A resource that represents a CockroachDB database. This is a child resource of a <ats-see cref="!:type:CockroachDBResource" />.</ats-summary>
[DebuggerDisplay("Type = {GetType().Name,nq}, Name = {Name}, Database = {DatabaseName}")]
public class CockroachDBDatabaseResource(string name, string databaseName, CockroachDBResource cockroachDBParentResource)
    : Resource(name), IResourceWithParent<CockroachDBResource>, IResourceWithConnectionString
{
    /// <summary>
    /// Gets the parent CockroachDB container resource.
    /// </summary>
    public CockroachDBResource Parent { get; } = cockroachDBParentResource ?? throw new ArgumentNullException(nameof(cockroachDBParentResource));

    /// <summary>
    /// Gets the connection string expression for the CockroachDB database.
    /// </summary>
    public ReferenceExpression ConnectionStringExpression
    {
        get
        {
            var connectionStringBuilder = new DbConnectionStringBuilder
            {
                ["Database"] = DatabaseName
            };

            return ReferenceExpression.Create($"{Parent};{connectionStringBuilder.ToString()}");
        }
    }
    /// <summary>
    /// Gets the database name.
    /// </summary>
    public string DatabaseName { get; } = ThrowIfNullOrEmpty(databaseName);

    private static string ThrowIfNullOrEmpty([NotNull] string? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(argument, paramName);
        return argument;
    }

    /// <summary>
    /// Gets the connection URI expression for the PostgreSQL database.
    /// </summary>
    /// <remarks>
    /// Format: <c>postgresql://{user}:{password}@{host}:{port}/{database}</c>.
    /// </remarks>
    public ReferenceExpression UriExpression => Parent.BuildUri(DatabaseName);

    /// <summary>
    /// Gets the JDBC connection string for the PostgreSQL database.
    /// </summary>
    /// <remarks>
    /// <para>Format: <c>jdbc:postgresql://{host}:{port}/{database}</c>.</para>
    /// <para>User and password credentials are not included in the JDBC connection string. Use the <see cref="IResourceWithConnectionString.GetConnectionProperties"/> method to access the <c>Username</c> and <c>Password</c> properties.</para>
    /// </remarks>
    public ReferenceExpression JdbcConnectionString => Parent.BuildJdbcConnectionString(DatabaseName);

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties() =>
        Parent.CombineProperties([
            new("DatabaseName", ReferenceExpression.Create($"{DatabaseName}")),
            new("Uri", UriExpression),
            new("JdbcConnectionString", JdbcConnectionString),
        ]);
}

