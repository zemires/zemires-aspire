using Aspire.Hosting.ApplicationModel;
using Zemires.Aspire.Hosting.CockroachDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Xml.Linq;

namespace Aspire.Hosting;

/// <summary>
/// Provides extension methods for adding CockroachDB resources to the application model.
/// </summary>
public static class CockroachDBBuilderExtensions
{
    private const int CockroachDBPort = 26257;

    /// <summary>
    /// Adds a CockroachDB container resource to the application model.
    /// The default image is <inheritdoc cref="CockroachDBContainerImageTags.Image"/> and the tag is <inheritdoc cref="CockroachDBContainerImageTags.Tag"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/>.</param>
    /// <param name="name">The name of the resource. This name will be used as the connection string name when referenced in a dependency.</param>
    /// <param name="userName">The parameter used to provide the user name. If <see langword="null"/> the default user is used.</param>
    /// <param name="password">The parameter used to provide the password. If <see langword="null"/> a random password will be generated.</param>
    /// <param name="port">The host port to bind the SQL endpoint to.</param>
    /// <param name="consolePort">The host port to bind the DB console (HTTP) endpoint to.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    /// <remarks>
    /// <example>
    /// Add a CockroachDB container with a database to the application model.
    /// <code lang="csharp">
    /// var builder = DistributedApplication.CreateBuilder(args);
    ///
    /// var db = builder.AddCockroachDB("cockroach").AddDatabase("db");
    ///
    /// builder.Build().Run();
    /// </code>
    /// </example>
    /// </remarks>
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
        ArgumentNullException.ThrowIfNull(name);

        var passwordParameter = password?.Resource ?? ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, $"{name}-password");

        var cockroachDb = new CockroachDBResource(name, userName?.Resource, passwordParameter);

        string? connectionString = null;

        builder.Eventing.Subscribe<ConnectionStringAvailableEvent>(cockroachDb, async (@event, ct) =>
        {
            connectionString = await cockroachDb.GetConnectionStringAsync(ct).ConfigureAwait(false);

            if (connectionString == null)
            {
                throw new DistributedApplicationException($"ConnectionStringAvailableEvent was published for the '{cockroachDb.Name}' resource but the connection string was null.");
            }
        });

        builder.Eventing.Subscribe<ResourceReadyEvent>(cockroachDb, async (@event, ct) =>
        {
            if (connectionString is null)
            {
                throw new DistributedApplicationException($"ResourceReadyEvent was published for the '{cockroachDb.Name}' resource but the connection string was null.");
            }

            // Non-database scoped connection string
            using var npgsqlConnection = new NpgsqlConnection(connectionString + ";Database=postgres;");

            await npgsqlConnection.OpenAsync(ct).ConfigureAwait(false);

            if (npgsqlConnection.State != System.Data.ConnectionState.Open)
            {
                throw new InvalidOperationException($"Could not open connection to '{cockroachDb.Name}'");
            }

            foreach (var name in cockroachDb.Databases.Keys)
            {
                if (builder.Resources.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)) is CockroachDBDatabaseResource cockroachDBDatabase)
                {
                    await CreateDatabaseAsync(npgsqlConnection, cockroachDBDatabase, @event.Services, ct).ConfigureAwait(false);
                }
            }
        });

        var healthCheckKey = $"{name}_check";
        builder.Services.AddHealthChecks().AddNpgSql(sp => connectionString ?? throw new InvalidOperationException("Connection string is unavailable"), name: healthCheckKey, configure: (connection) =>
        {
            // HACK: The Npgsql client defaults to using the username in the connection string if the database is not specified. Here
            //       we override this default behavior because we are working with a non-database scoped connection string. The Aspirified
            //       package doesn't have to deal with this because it uses a datasource from DI which doesn't have this issue:
            //
            //       https://github.com/npgsql/npgsql/blob/c3b31c393de66a4b03fba0d45708d46a2acb06d2/src/Npgsql/NpgsqlConnection.cs#L445
            //
            connection.ConnectionString += ";Database=defaultDb;";
        });

        var cockroachDbBuilder = builder.AddResource(cockroachDb)
            .WithAnnotation(new ContainerImageAnnotation { Image = CockroachDBContainerImageTags.Image, Tag = CockroachDBContainerImageTags.Tag, Registry = CockroachDBContainerImageTags.Registry })
            .WithEndpoint(targetPort: CockroachDBPort, port: port, name: CockroachDBResource.PrimaryEndpointName, env: "COCKROACH_PORT")
            .WithHttpEndpoint(targetPort: 8080, port: consolePort, name: CockroachDBResource.ConsoleEndpointName)
            //.WithHttpHealthCheck("/healthz", 200, CockroachDBResource.PrimaryEndpointName)
            .WithIconName("DatabaseMultiple")
            .WithArgs("start-single-node", "--insecure")
            .WithEnvironment(context =>
            {
                context.EnvironmentVariables["COCKROACH_USER"] = cockroachDb.UserNameReference;
                context.EnvironmentVariables["COCKROACH_PASSWORD"] = cockroachDb.PasswordParameter;
            });
        //.WithEnvironment("WEBHOOK_URL", cockroachDb.GetEndpoint(CockroachDBResource.PrimaryEndpointName, builder.ExecutionContext.IsPublishMode ? KnownNetworkIdentifiers.PublicInternet : KnownNetworkIdentifiers.LocalhostNetwork));

        //#pragma warning disable ASPIRECERTIFICATES001
        //        cockroachDbBuilder.WithHttpsCertificateConfiguration(ctx =>
        //        {
        //            ctx.EnvironmentVariables["N8N_PROTOCOL"] = "https";
        //            ctx.EnvironmentVariables["N8N_SSL_KEY"] = ctx.KeyPath;
        //            ctx.EnvironmentVariables["N8N_SSL_CERT"] = ctx.CertificatePath;
        //            ctx.EnvironmentVariables["NODE_EXTRA_CA_CERTS"] = ctx.CertificatePath;
        //            return Task.CompletedTask;
        //        });
        //#pragma warning restore ASPIRECERTIFICATES001

        //        if (builder.ExecutionContext.IsRunMode)
        //        {
        //#pragma warning disable ASPIRECERTIFICATES001
        //            builder.Eventing.Subscribe<BeforeStartEvent>((@event, cancellationToken) =>
        //            {
        //                var developerCertificateService = @event.Services.GetRequiredService<IDeveloperCertificateService>();

        //                bool addHttps = false;
        //                if (!cockroachDbBuilder.Resource.TryGetLastAnnotation<HttpsCertificateAnnotation>(out var annotation))
        //                {
        //                    if (developerCertificateService.UseForHttps)
        //                    {
        //                        addHttps = true;
        //                    }
        //                }
        //                else if (annotation.UseDeveloperCertificate.GetValueOrDefault(developerCertificateService.UseForHttps) || annotation.Certificate is not null)
        //                {
        //                    addHttps = true;
        //                }

        //                if (addHttps)
        //                {
        //                    // If a TLS certificate is configured, override the endpoint to use HTTPS instead of HTTP
        //                    cockroachDbBuilder.WithEndpoint(CockroachDBResource.PrimaryEndpointName, ep => ep.UriScheme = "https");
        //                }

        //                return Task.CompletedTask;
        //            });
        //#pragma warning restore ASPIRECERTIFICATES001
        //        }

        return cockroachDbBuilder;
    }

    /// <summary>
    /// Adds a named volume for the data folder to a CockroachDB container resource.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">The name of the volume. Defaults to an auto-generated name based on the application and resource names.</param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    /// <remarks>
    /// <example>
    /// Add a CockroachDB container to the application model with a data volume, so data is persisted across container restarts.
    /// <code lang="csharp">
    /// var builder = DistributedApplication.CreateBuilder(args);
    ///
    /// var cockroach = builder.AddCockroachDB("cockroach")
    ///   .WithDataVolume();
    ///
    /// builder.Build().Run();
    /// </code>
    /// </example>
    /// </remarks>
    [AspireExport]
    public static IResourceBuilder<CockroachDBResource> WithDataVolume(this IResourceBuilder<CockroachDBResource> builder, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithVolume(name ?? VolumeNameGenerator.Generate(builder, "data"), "/cockroach/cockroach-data");
    }

    /// <summary>
    /// Adds a bind mount for the data folder to a CockroachDB container resource.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="source">The source directory on the host to mount into the container.</param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    [AspireExport]
    public static IResourceBuilder<CockroachDBResource> WithDataBindMount(this IResourceBuilder<CockroachDBResource> builder, string source)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(source);

        return builder.WithBindMount(source, "/cockroach/cockroach-data");
    }

    /// <summary>
    /// Defines the SQL script used to create the database.
    /// </summary>
    /// <param name="builder">The builder for the <see cref="CockroachDBDatabaseResource"/>.</param>
    /// <param name="script">The SQL script used to create the database.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    /// <ats-returns>The resource builder.</ats-returns>
    /// <remarks>
    /// The script can only contain SQL statements applying to the default database like CREATE DATABASE. Custom statements like table creation
    /// and data insertion are not supported since they require a distinct connection to the newly created database.
    /// <value>Default script is <code>CREATE DATABASE "&lt;QUOTED_DATABASE_NAME&gt;"</code></value>
    /// </remarks>
    [AspireExport]
    public static IResourceBuilder<CockroachDBDatabaseResource> WithCreationScript(this IResourceBuilder<CockroachDBDatabaseResource> builder, string script)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(script);

        builder.WithAnnotation(new CockroachDBCreateDatabaseScriptAnnotation(script));

        return builder;
    }

    /// <summary>
    /// Configures the password that the CockroachDB resource is used.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="password">The parameter used to provide the password for the CockroachDB resource.</param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    /// <ats-returns>The resource builder.</ats-returns>
    [AspireExport]
    public static IResourceBuilder<CockroachDBResource> WithPassword(this IResourceBuilder<CockroachDBResource> builder, IResourceBuilder<ParameterResource> password)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(password);

        builder.Resource.PasswordParameter = password.Resource;
        return builder;
    }

    /// <summary>
    /// Configures the user name that the CockroachDB resource is used.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="userName">The parameter used to provide the user name for the CockroachDB resource.</param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    /// <ats-returns>The resource builder.</ats-returns>
    [AspireExport]
    public static IResourceBuilder<CockroachDBResource> WithUserName(this IResourceBuilder<CockroachDBResource> builder, IResourceBuilder<ParameterResource> userName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(userName);

        builder.Resource.UserNameParameter = userName.Resource;
        return builder;
    }

    /// <summary>
    /// Configures the host port that the CockroachDB resource is exposed on instead of using randomly assigned port.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="port">The port to bind on the host. If <see langword="null"/> is used random port will be assigned.</param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    /// <ats-returns>The resource builder.</ats-returns>
    [AspireExport("withPostgresHostPort", MethodName = "withHostPort")]
    public static IResourceBuilder<CockroachDBResource> WithHostPort(this IResourceBuilder<CockroachDBResource> builder, int? port)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithEndpoint(CockroachDBResource.PrimaryEndpointName, endpoint =>
        {
            endpoint.Port = port;
        });
    }

    /// <summary>
    /// Adds a CockroachDB database to the application model.
    /// </summary>
    /// <param name="builder">The CockroachDB server resource builder.</param>
    /// <param name="name">The name of the resource. This name will be used as the connection string name when referenced in a dependency.</param>
    /// <param name="databaseName">The name of the database. If not provided, this defaults to the same value as <paramref name="name"/>.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    /// <ats-returns>The resource builder.</ats-returns>
    /// <remarks>
    /// <para>
    /// This resource includes built-in health checks. When this resource is referenced as a dependency
    /// using the <see cref="ResourceBuilderExtensions.WaitFor{T}(IResourceBuilder{T}, IResourceBuilder{IResource})"/>
    /// extension method then the dependent resource will wait until the Postgres database is available.
    /// </para>
    /// <para>
    /// Note that calling <see cref="AddDatabase(IResourceBuilder{CockroachDBResource}, string, string?)"/>
    /// will result in the database being created on the Postgres server when the server becomes ready.
    /// The database creation happens automatically as part of the resource lifecycle.
    /// </para>
    /// </remarks>
    /// <ats-remarks />
    [AspireExport]
    public static IResourceBuilder<CockroachDBDatabaseResource> AddDatabase(this IResourceBuilder<CockroachDBResource> builder, [ResourceName] string name, string? databaseName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        // Use the resource name as the database name if it's not provided
        databaseName ??= name;

        var cockroachDBDatabase = new CockroachDBDatabaseResource(name, databaseName, builder.Resource);

        builder.Resource.AddDatabase(cockroachDBDatabase.Name, cockroachDBDatabase.DatabaseName);

        string? connectionString = null;

        builder.ApplicationBuilder.Eventing.Subscribe<ConnectionStringAvailableEvent>(cockroachDBDatabase, async (@event, ct) =>
        {
            connectionString = await cockroachDBDatabase.ConnectionStringExpression.GetValueAsync(ct).ConfigureAwait(false);

            if (connectionString == null)
            {
                throw new DistributedApplicationException($"ConnectionStringAvailableEvent was published for the '{name}' resource but the connection string was null.");
            }
        });

        var healthCheckKey = $"{name}_check";
        builder.ApplicationBuilder.Services.AddHealthChecks().AddNpgSql(sp => connectionString ?? throw new InvalidOperationException("Connection string is unavailable"), name: healthCheckKey);

        return builder.ApplicationBuilder
            .AddResource(cockroachDBDatabase)
            .WithHealthCheck(healthCheckKey);
    }

    //public static IResourceBuilder<CockroachDBDatabaseResource> AddDefaultDatabase(this IResourceBuilder<CockroachDBResource> builder, [ResourceName] string name, string? databaseName = "defaultdb")
    //{
    //    ArgumentNullException.ThrowIfNull(builder);
    //    ArgumentException.ThrowIfNullOrEmpty(name);

    //    databaseName ??= name;

    //    // single-node only
    //    builder.WithEnvironment("COCKROACH_DATABASE", databaseName);

    //    var cockroachDBDatabase = new CockroachDBDatabaseResource(name, databaseName, builder.Resource);

    //    string? connectionString = null;

    //    builder.ApplicationBuilder.Eventing.Subscribe<ConnectionStringAvailableEvent>(cockroachDBDatabase, async (@event, ct) =>
    //    {
    //        connectionString = await cockroachDBDatabase.ConnectionStringExpression.GetValueAsync(ct).ConfigureAwait(false);

    //        if (connectionString == null)
    //        {
    //            throw new DistributedApplicationException($"ConnectionStringAvailableEvent was published for the '{name}' resource but the connection string was null.");
    //        }
    //    });

    //    var healthCheckKey = $"{name}_check";
    //    builder.ApplicationBuilder.Services.AddHealthChecks().AddNpgSql(sp => connectionString ?? throw new InvalidOperationException("Connection string is unavailable"), name: healthCheckKey);

    //    return builder.ApplicationBuilder
    //        .AddResource(cockroachDBDatabase)
    //        .WithHealthCheck(healthCheckKey);
    //}

    private static async Task CreateDatabaseAsync(NpgsqlConnection npgsqlConnection, CockroachDBDatabaseResource npgsqlDatabase, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var scriptAnnotation = npgsqlDatabase.Annotations.OfType<CockroachDBCreateDatabaseScriptAnnotation>().LastOrDefault();

        var logger = serviceProvider.GetRequiredService<ResourceLoggerService>().GetLogger(npgsqlDatabase.Parent);
        logger.LogDebug("Creating database '{DatabaseName}'", npgsqlDatabase.DatabaseName);

        try
        {
            var quotedDatabaseIdentifier = new NpgsqlCommandBuilder().QuoteIdentifier(npgsqlDatabase.DatabaseName);
            using var command = npgsqlConnection.CreateCommand();
            var commandText = scriptAnnotation?.Script ?? $"CREATE DATABASE {quotedDatabaseIdentifier}";
            command.CommandText = commandText;

            if (scriptAnnotation?.Script is not null)
            {
                logger.LogInformation("Executing custom creation script for database '{DatabaseName}'", npgsqlDatabase.DatabaseName);
            }

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (scriptAnnotation?.Script is not null)
            {
                // ADO.NET returns -1 for DDL statements (CREATE DATABASE, etc.) because they don't affect data rows.
                // Only include the rows-affected count when it carries meaningful information.
                if (rowsAffected >= 0)
                {
                    logger.LogInformation("Completed custom creation script for database '{DatabaseName}' ({RowsAffected} rows affected)", npgsqlDatabase.DatabaseName, rowsAffected);
                }
                else
                {
                    logger.LogInformation("Completed custom creation script for database '{DatabaseName}'", npgsqlDatabase.DatabaseName);
                }
            }

            logger.LogDebug("Database '{DatabaseName}' created successfully", npgsqlDatabase.DatabaseName);
        }
        catch (PostgresException p) when (p.SqlState == "42P04")
        {
            // Ignore the error if the database already exists.
            logger.LogDebug("Database '{DatabaseName}' already exists", npgsqlDatabase.DatabaseName);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to create database '{DatabaseName}'", npgsqlDatabase.DatabaseName);
        }
    }

}