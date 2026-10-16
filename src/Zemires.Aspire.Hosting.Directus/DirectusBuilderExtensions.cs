using Aspire.Hosting.ApplicationModel;
using Zemires.Aspire.Hosting.Directus;

namespace Aspire.Hosting;

/// <summary>
/// Extension methods for adding Directus resources to the application model.
/// </summary>
public static class DirectusBuilderExtensions
{
    private const int DirectusPort = 8055;
    private const string DataPath = "/directus/data";

    /// <summary>
    /// Adds a Directus container resource (image <inheritdoc cref="DirectusContainerImageTags.Image"/>:<inheritdoc cref="DirectusContainerImageTags.Tag"/>).
    /// Configure the database with <see cref="WithSqlite3Database"/>, <see cref="WithPostgresDatabase"/> or <see cref="WithSqlServerDatabase"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/>.</param>
    /// <param name="name">The resource name, also used as connection string name.</param>
    /// <param name="userName">The admin email. <c>directus@local.dev</c> when <see langword="null"/>.</param>
    /// <param name="password">The admin password. Generated when <see langword="null"/>.</param>
    /// <param name="adminToken">The static admin API token. Generated when <see langword="null"/>.</param>
    /// <param name="secret">The Directus <c>SECRET</c>. Generated when <see langword="null"/>.</param>
    /// <param name="port">The host port of the HTTP endpoint.</param>
    /// <returns>The resource builder.</returns>
    /// <example>
    /// <code lang="csharp">
    /// var directus = builder.AddDirectus("directus").WithSqlite3Database().WithDataVolume();
    /// </code>
    /// </example>
    [AspireExport]
    public static IResourceBuilder<DirectusResource> AddDirectus(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        IResourceBuilder<ParameterResource>? userName = null,
        IResourceBuilder<ParameterResource>? password = null,
        IResourceBuilder<ParameterResource>? adminToken = null,
        IResourceBuilder<ParameterResource>? secret = null,
        int? port = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var passwordParameter = password?.Resource ?? ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, $"{name}-password");
        var adminTokenParameter = adminToken?.Resource ?? ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, $"{name}-admintoken");
        var secretParameter = secret?.Resource ?? ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, $"{name}-secret");

        var directus = new DirectusResource(name, userName?.Resource, passwordParameter, adminTokenParameter);

        return builder.AddResource(directus)
            .WithImage(DirectusContainerImageTags.Image)
            .WithImageTag(DirectusContainerImageTags.Tag)
            .WithImageRegistry(DirectusContainerImageTags.Registry)
            .WithHttpEndpoint(targetPort: DirectusPort, port: port, name: DirectusResource.PrimaryEndpointName, env: "PORT")
            .WithHttpHealthCheck("/server/health", 200, DirectusResource.PrimaryEndpointName)
            .WithIconName("DatabaseMultiple")
            .WithEnvironment("SECRET", secretParameter)
            .WithEnvironment("PUBLIC_URL", directus.GetEndpoint(DirectusResource.PrimaryEndpointName, builder.ExecutionContext.IsPublishMode ? KnownNetworkIdentifiers.PublicInternet : KnownNetworkIdentifiers.LocalhostNetwork))
            .WithEnvironment("STORAGE_LOCAL_ROOT", $"{DataPath}/storage")
            // callback, as WithUserName/WithPassword/WithAdminToken may replace the parameters later
            .WithEnvironment(context =>
            {
                context.EnvironmentVariables["ADMIN_EMAIL"] = directus.UserNameReference;
                context.EnvironmentVariables["ADMIN_PASSWORD"] = directus.PasswordParameter;
                context.EnvironmentVariables["ADMIN_TOKEN"] = directus.AdminTokenParameter;
            });
    }

    /// <summary>
    /// Sets the admin password.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="password">The admin password.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<DirectusResource> WithPassword(this IResourceBuilder<DirectusResource> builder, IResourceBuilder<ParameterResource> password)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(password);

        builder.Resource.PasswordParameter = password.Resource;
        return builder;
    }

    /// <summary>
    /// Sets the admin email.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="userName">The admin email.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<DirectusResource> WithUserName(this IResourceBuilder<DirectusResource> builder, IResourceBuilder<ParameterResource> userName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(userName);

        builder.Resource.UserNameParameter = userName.Resource;
        return builder;
    }

    /// <summary>
    /// Sets the static admin API token.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="adminToken">The admin token.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<DirectusResource> WithAdminToken(this IResourceBuilder<DirectusResource> builder, IResourceBuilder<ParameterResource> adminToken)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(adminToken);

        builder.Resource.AdminTokenParameter = adminToken.Resource;
        return builder;
    }

    /// <summary>
    /// Stores Directus data in an embedded SQLite database in the data folder.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<DirectusResource> WithSqlite3Database(this IResourceBuilder<DirectusResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithEnvironment("DB_CLIENT", "sqlite3")
            .WithEnvironment("DB_FILENAME", $"{DataPath}/directus.db");
    }

    /// <summary>
    /// Adds a named volume for the Directus data folder.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">The volume name. Generated when <see langword="null"/>.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<DirectusResource> WithDataVolume(this IResourceBuilder<DirectusResource> builder, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithVolume(name ?? VolumeNameGenerator.Generate(builder, "data"), DataPath);
    }

    /// <summary>
    /// Adds a bind mount for the Directus data folder.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="source">The host directory to mount.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<DirectusResource> WithDataBindMount(this IResourceBuilder<DirectusResource> builder, string source)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(source);

        return builder.WithBindMount(source, DataPath);
    }

    /// <summary>
    /// Stores Directus data in a PostgreSQL database. Reads <c>Host</c>, <c>Port</c>, <c>DatabaseName</c>, <c>Username</c> and
    /// <c>Password</c> from the connection properties of <paramref name="database"/> and waits for it.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="database">The PostgreSQL database.</param>
    /// <param name="useTls">Whether to require TLS (<c>DB_SSLMODE=require</c>).</param>
    /// <returns>The resource builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="database"/> has no connection string.</exception>
    [AspireExport]
    public static IResourceBuilder<DirectusResource> WithPostgresDatabase(this IResourceBuilder<DirectusResource> builder, IResourceBuilder<IResource> database, bool useTls = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(database);

        return builder.WithDatabase("pg", database)
            .WithEnvironment("DB_SSLMODE", useTls ? "require" : "none");
    }

    /// <summary>
    /// Stores Directus data in a SQL Server database. Reads <c>Host</c>, <c>Port</c>, <c>DatabaseName</c>, <c>Username</c> and
    /// <c>Password</c> from the connection properties of <paramref name="database"/> and waits for it.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="database">The SQL Server database.</param>
    /// <param name="useTls">Whether to force encryption (<c>DB_OPTIONS__ENCRYPT=true</c>); otherwise the driver default applies.</param>
    /// <returns>The resource builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="database"/> has no connection string.</exception>
    [AspireExport]
    public static IResourceBuilder<DirectusResource> WithSqlServerDatabase(this IResourceBuilder<DirectusResource> builder, IResourceBuilder<IResource> database, bool useTls = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(database);

        builder.WithDatabase("mssql", database);

        return useTls ? builder.WithEnvironment("DB_OPTIONS__ENCRYPT", "true") : builder;
    }

    private static IResourceBuilder<DirectusResource> WithDatabase(this IResourceBuilder<DirectusResource> builder, string client, IResourceBuilder<IResource> database)
    {
        var db = database.Resource as IResourceWithConnectionString
            ?? throw new ArgumentException($"Resource '{database.Resource.Name}' has no connection string and cannot be used as database for Directus.", nameof(database));

        return builder.WithEnvironment("DB_CLIENT", client)
            .WithEnvironment("DB_HOST", db.GetConnectionProperty("Host"))
            .WithEnvironment("DB_PORT", db.GetConnectionProperty("Port"))
            .WithEnvironment("DB_DATABASE", db.GetConnectionProperty("DatabaseName"))
            .WithEnvironment("DB_USER", db.GetConnectionProperty("Username"))
            .WithEnvironment("DB_PASSWORD", db.GetConnectionProperty("Password"))
            .WithReferenceRelationship(database)
            .WaitFor(database);
    }
}
