namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// The SQL script creating a CockroachDB database, see <c>WithCreationScript</c>.
/// </summary>
/// <param name="script">The SQL script.</param>
internal sealed class CockroachDBCreateDatabaseScriptAnnotation(string script) : IResourceAnnotation
{
    /// <summary>
    /// Gets the SQL script.
    /// </summary>
    public string Script { get; } = script ?? throw new ArgumentNullException(nameof(script));
}
