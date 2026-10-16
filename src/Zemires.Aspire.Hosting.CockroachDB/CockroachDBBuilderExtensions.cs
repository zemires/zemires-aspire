using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Zemires.Aspire.Hosting.CockroachDB;

namespace Aspire.Hosting;

/// <summary>
/// Extension methods for adding CockroachDB resources to the application model.
/// </summary>
public static class CockroachDBBuilderExtensions
{
    private const int CockroachDBPort = 26257;
    private const int ConsolePort = 8080;
    private const string DataPath = "/cockroach/cockroach-data";
    private const string DefaultDatabase = "defaultdb";

    /// <summary>
    /// Adds a single-node CockroachDB container resource (image <inheritdoc cref="CockroachDBContainerImageTags.Image"/>:<inheritdoc cref="CockroachDBContainerImageTags.Tag"/>).
    /// </summary>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/>.</param>
    /// <param name="name">The resource name, also used as connection string name.</param>
    /// <param name="userName">The user name. <c>cockroach</c> when <see langword="null"/>.</param>
    /// <param name="password">The password. Generated when <see langword="null"/>.</param>
    /// <param name="port">The host port of the SQL endpoint.</param>
    /// <param name="consolePort">The host port of the DB console (HTTP).</param>
    /// <returns>The resource builder.</returns>
    /// <example>
    /// <code lang="csharp">
    /// var db = builder.AddCockroachDB("cockroach").AddDatabase("db");
    /// </code>
    /// </example>
    [AspireExport]
    public static IResourceBuilder<CockroachDBResource> AddCockroachDB(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        IResourceBuilder<ParameterResource>? userName = null,
        IResourceBuilder<ParameterResource>? password = null,
        int? port = null,
        int? consolePort = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var passwordParameter = password?.Resource ?? ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, $"{name}-password");

        var cockroachDb = new CockroachDBResource(name, userName?.Resource, passwordParameter);

        string? connectionString = null;

        builder.Eventing.Subscribe<ConnectionStringAvailableEvent>(cockroachDb, async (@event, ct) =>
        {
            connectionString = await cockroachDb.ConnectionStringExpression.GetValueAsync(ct).ConfigureAwait(false)
                ?? throw new DistributedApplicationException($"ConnectionStringAvailableEvent was published for the '{cockroachDb.Name}' resource but the connection string was null.");
        });

        builder.Eventing.Subscribe<ResourceReadyEvent>(cockroachDb, async (@event, ct) =>
        {
            if (connectionString is null)
            {
                throw new DistributedApplicationException($"ResourceReadyEvent was published for the '{cockroachDb.Name}' resource but the connection string was null.");
            }

            await using var connection = new NpgsqlConnection($"{connectionString};Database={DefaultDatabase}");
            await connection.OpenAsync(ct).ConfigureAwait(false);

            foreach (var database in builder.Resources.OfType<CockroachDBDatabaseResource>().Where(d => d.Parent == cockroachDb))
            {
                await CreateDatabaseAsync(connection, database, @event.Services, ct).ConfigureAwait(false);
            }
        });

        // The server connection string has no database; Npgsql would default to the user name.
        var healthCheckKey = $"{name}_check";
        builder.Services.AddHealthChecks().AddNpgSql(
            _ => $"{connectionString ?? throw new InvalidOperationException("Connection string is unavailable")};Database={DefaultDatabase}",
            name: healthCheckKey);

        return builder.AddResource(cockroachDb)
            .WithImage(CockroachDBContainerImageTags.Image)
            .WithImageTag(CockroachDBContainerImageTags.Tag)
            .WithImageRegistry(CockroachDBContainerImageTags.Registry)
            .WithEndpoint(targetPort: CockroachDBPort, port: port, name: CockroachDBResource.PrimaryEndpointName, env: "COCKROACH_PORT")
            .WithHttpEndpoint(targetPort: ConsolePort, port: consolePort, name: CockroachDBResource.ConsoleEndpointName)
            .WithHealthCheck(healthCheckKey)
            .WithIconName("DatabaseMultiple")
            .WithArgs("start-single-node", "--insecure")
            // callback, as WithUserName/WithPassword may replace the parameters later
            .WithEnvironment(context =>
            {
                context.EnvironmentVariables["COCKROACH_USER"] = cockroachDb.UserNameReference;
                context.EnvironmentVariables["COCKROACH_PASSWORD"] = cockroachDb.PasswordParameter;
            });
    }

    /// <summary>
    /// Adds a named volume for the CockroachDB data folder.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">The volume name. Generated when <see langword="null"/>.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<CockroachDBResource> WithDataVolume(this IResourceBuilder<CockroachDBResource> builder, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithVolume(name ?? VolumeNameGenerator.Generate(builder, "data"), DataPath);
    }

    /// <summary>
    /// Adds a bind mount for the CockroachDB data folder.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="source">The host directory to mount.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<CockroachDBResource> WithDataBindMount(this IResourceBuilder<CockroachDBResource> builder, string source)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(source);

        return builder.WithBindMount(source, DataPath);
    }

    /// <summary>
    /// Replaces the default <c>CREATE DATABASE "{name}"</c> script. The script runs on the default database, so it cannot create tables in the new database.
    /// </summary>
    /// <param name="builder">The database resource builder.</param>
    /// <param name="script">The SQL script.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<CockroachDBDatabaseResource> WithCreationScript(this IResourceBuilder<CockroachDBDatabaseResource> builder, string script)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(script);

        return builder.WithAnnotation(new CockroachDBCreateDatabaseScriptAnnotation(script));
    }

    /// <summary>
    /// Sets the password.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="password">The password.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<CockroachDBResource> WithPassword(this IResourceBuilder<CockroachDBResource> builder, IResourceBuilder<ParameterResource> password)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(password);

        builder.Resource.PasswordParameter = password.Resource;
        return builder;
    }

    /// <summary>
    /// Sets the user name.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="userName">The user name.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<CockroachDBResource> WithUserName(this IResourceBuilder<CockroachDBResource> builder, IResourceBuilder<ParameterResource> userName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(userName);

        builder.Resource.UserNameParameter = userName.Resource;
        return builder;
    }

    /// <summary>
    /// Sets the host port of the SQL endpoint.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="port">The host port. Random when <see langword="null"/>.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport("withCockroachDBHostPort", MethodName = "withHostPort")]
    public static IResourceBuilder<CockroachDBResource> WithHostPort(this IResourceBuilder<CockroachDBResource> builder, int? port)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithEndpoint(CockroachDBResource.PrimaryEndpointName, endpoint => endpoint.Port = port);
    }

    /// <summary>
    /// Adds a database, created when the server is ready. The database has a health check, so <c>WaitFor</c> waits until it is available.
    /// </summary>
    /// <param name="builder">The CockroachDB resource builder.</param>
    /// <param name="name">The resource name, also used as connection string name.</param>
    /// <param name="databaseName">The database name. <paramref name="name"/> when <see langword="null"/>.</param>
    /// <returns>The database resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<CockroachDBDatabaseResource> AddDatabase(this IResourceBuilder<CockroachDBResource> builder, [ResourceName] string name, string? databaseName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var database = new CockroachDBDatabaseResource(name, databaseName ?? name, builder.Resource);
        builder.Resource.AddDatabase(database.Name, database.DatabaseName);

        string? connectionString = null;

        builder.ApplicationBuilder.Eventing.Subscribe<ConnectionStringAvailableEvent>(database, async (@event, ct) =>
        {
            connectionString = await database.ConnectionStringExpression.GetValueAsync(ct).ConfigureAwait(false)
                ?? throw new DistributedApplicationException($"ConnectionStringAvailableEvent was published for the '{name}' resource but the connection string was null.");
        });

        var healthCheckKey = $"{name}_check";
        builder.ApplicationBuilder.Services.AddHealthChecks().AddNpgSql(_ => connectionString ?? throw new InvalidOperationException("Connection string is unavailable"), name: healthCheckKey);

        return builder.ApplicationBuilder
            .AddResource(database)
            .WithHealthCheck(healthCheckKey);
    }

    private static async Task CreateDatabaseAsync(NpgsqlConnection connection, CockroachDBDatabaseResource database, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        database.TryGetLastAnnotation<CockroachDBCreateDatabaseScriptAnnotation>(out var scriptAnnotation);

        var logger = serviceProvider.GetRequiredService<ResourceLoggerService>().GetLogger(database.Parent);
        logger.LogDebug("Creating database '{DatabaseName}'", database.DatabaseName);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = scriptAnnotation?.Script ?? $"CREATE DATABASE {new NpgsqlCommandBuilder().QuoteIdentifier(database.DatabaseName)}";

            if (scriptAnnotation is not null)
            {
                logger.LogInformation("Executing custom creation script for database '{DatabaseName}'", database.DatabaseName);
            }

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            logger.LogDebug("Database '{DatabaseName}' created", database.DatabaseName);
        }
        catch (PostgresException p) when (p.SqlState == "42P04")
        {
            logger.LogDebug("Database '{DatabaseName}' already exists", database.DatabaseName);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to create database '{DatabaseName}'", database.DatabaseName);
        }
    }
}
