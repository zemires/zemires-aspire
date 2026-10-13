using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// Represents an annotation for defining a script to create a database in CockroachDB.
/// </summary>
internal sealed class CockroachDBCreateDatabaseScriptAnnotation : IResourceAnnotation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CockroachDBCreateDatabaseScriptAnnotation"/> class.
    /// </summary>
    /// <param name="script">The script used to create the database.</param>
    public CockroachDBCreateDatabaseScriptAnnotation(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        Script = script;
    }

    /// <summary>
    /// Gets the script used to create the database.
    /// </summary>
    public string Script { get; }
}
