using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Zemires.Aspire.Hosting.Directus;

namespace Aspire.Hosting;

/// <summary>
/// Provides extension methods for adding Directus resources to the application model.
/// </summary>
public static class DirectusBuilderExtensions
{
    private const int DirectusPort = 8055;

    /// <summary>
    /// Adds a Directus container resource to the application model.
    /// The default image is <inheritdoc cref="DirectusContainerImageTags.Image"/> and the tag is <inheritdoc cref="DirectusContainerImageTags.Tag"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/>.</param>
    /// <param name="name">The name of the resource. This name will be used as the connection string name when referenced in a dependency.</param>
    /// <param name="userName">The parameter used to provide the admin email. If <see langword="null"/> the default is used.</param>
    /// <param name="password">The parameter used to provide the admin password. If <see langword="null"/> a random password will be generated.</param>
    /// <param name="adminToken">The parameter used to provide the static admin API token. If <see langword="null"/> a random token will be generated.</param>
    /// <param name="secret">The parameter used to provide the Directus <c>SECRET</c>. If <see langword="null"/> a random secret will be generated.</param>
    /// <param name="port">The host port to bind the underlying container to.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    /// <remarks>
    /// <example>
    /// Add an Directus container to the application model and reference it in a .NET project.
    /// <code lang="csharp">
    /// var builder = DistributedApplication.CreateBuilder(args);
    ///
    /// var Directus = builder.AddDirectus("Directus");
    ///  
    /// builder.Build().Run(); 
    /// </code>
    /// </example>
    /// </remarks>
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
        ArgumentNullException.ThrowIfNull(name);

        var passwordParameter = password?.Resource ?? ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, $"{name}-password");
        var adminTokenParameter = adminToken?.Resource ?? ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, $"{name}-admintoken");
        var secretParameter = secret?.Resource ?? ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, $"{name}-secret");

        var directus = new DirectusResource(name, userName?.Resource, passwordParameter, adminTokenParameter);

        string? connectionString = null;

        builder.Eventing.Subscribe<ConnectionStringAvailableEvent>(directus, async (@event, ct) =>
        {
            connectionString = await directus.GetConnectionStringAsync(ct).ConfigureAwait(false);

            if (connectionString == null)
            {
                throw new DistributedApplicationException($"ConnectionStringAvailableEvent was published for the '{directus.Name}' resource but the connection string was null.");
            }
        });

        var healthCheckKey = $"{directus.Name}_{DirectusResource.PrimaryEndpointName}_/server/health_200_check";
        builder.Services.AddHttpClient(healthCheckKey, (client) =>
        {
            var adminToken = directus.AdminTokenParameter.GetValueAsync(CancellationToken.None).GetAwaiter().GetResult();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
        });

        var directusBuilder = builder.AddResource(directus)
            .WithAnnotation(new ContainerImageAnnotation { Image = DirectusContainerImageTags.Image, Tag = DirectusContainerImageTags.Tag, Registry = DirectusContainerImageTags.Registry })
            .WithHttpEndpoint(targetPort: DirectusPort, port: port, name: DirectusResource.PrimaryEndpointName, env: "PORT")
            .WithHttpHealthCheck("/server/health", 200, DirectusResource.PrimaryEndpointName)
            .WithIconName("DatabaseMultiple")
            .WithEnvironment("SECRET", secretParameter)
            .WithEnvironment("PUBLIC_URL", directus.GetEndpoint(DirectusResource.PrimaryEndpointName, builder.ExecutionContext.IsPublishMode ? KnownNetworkIdentifiers.PublicInternet : KnownNetworkIdentifiers.LocalhostNetwork))
            .WithEnvironment("STORAGE_LOCAL_ROOT", "/directus/data/storage")
            .WithEnvironment(context =>
            {
                context.EnvironmentVariables["ADMIN_EMAIL"] = directus.UserNameReference;
                context.EnvironmentVariables["ADMIN_PASSWORD"] = directus.PasswordParameter;
                context.EnvironmentVariables["ADMIN_TOKEN"] = directus.AdminTokenParameter;
            });
        //.WithEnvironment("WEBHOOK_URL", Directus.GetEndpoint(DirectusResource.PrimaryEndpointName, builder.ExecutionContext.IsPublishMode ? KnownNetworkIdentifiers.PublicInternet : KnownNetworkIdentifiers.LocalhostNetwork));

        //#pragma warning disable ASPIRECERTIFICATES001
        //        DirectusBuilder.WithHttpsCertificateConfiguration(ctx =>
        //        {
        //            ctx.EnvironmentVariables["Directus_PROTOCOL"] = "https";
        //            ctx.EnvironmentVariables["Directus_SSL_KEY"] = ctx.KeyPath;
        //            ctx.EnvironmentVariables["Directus_SSL_CERT"] = ctx.CertificatePath;
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
        //                if (!DirectusBuilder.Resource.TryGetLastAnnotation<HttpsCertificateAnnotation>(out var annotation))
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
        //                    DirectusBuilder.WithEndpoint(DirectusResource.PrimaryEndpointName, ep => ep.UriScheme = "https");
        //                }

        //                return Task.CompletedTask;
        //            });
        //#pragma warning restore ASPIRECERTIFICATES001
        //        }

        return directusBuilder;
    }

    ///// <summary>
    ///// Adds a named volume for the data folder to a Directus container resource.
    ///// </summary>
    ///// <param name="builder">The resource builder.</param>
    ///// <param name="name">The name of the volume. Defaults to an auto-generated name based on the application and resource names.</param>
    ///// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    ///// <remarks>
    ///// <example>
    ///// Add an Directus container to the application model and reference it in a .NET project. Additionally, in this
    ///// example a data volume is added to the container to allow data to be persisted across container restarts.
    ///// <code lang="csharp">
    ///// var builder = DistributedApplication.CreateBuilder(args);
    /////
    ///// var Directus = builder.AddDirectus("Directus")
    ///// .WithDataVolume();
    ///// var api = builder.AddProject&lt;Projects.Api&gt;("api")
    /////   .WithReference(Directus);
    /////  
    ///// builder.Build().Run(); 
    ///// </code>
    ///// </example>
    ///// </remarks>
    //public static IResourceBuilder<DirectusResource> WithDataVolume(this IResourceBuilder<DirectusResource> builder, string? name = null)
    //{
    //    ArgumentNullException.ThrowIfNull(builder);

    //    return builder.WithVolume(name ?? VolumeNameGenerator.Generate(builder, "data"), "/cockroach/cockroach-data");
    //}

    ///// <summary>
    ///// Adds a bind mount for the data folder to a DirectusResource container resource.
    ///// </summary>
    ///// <param name="builder"></param>
    ///// <param name="source"></param>
    ///// <returns></returns>
    //public static IResourceBuilder<DirectusResource> WithDataBindMount(this IResourceBuilder<DirectusResource> builder, string source)
    //{
    //    ArgumentNullException.ThrowIfNull(builder);
    //    ArgumentNullException.ThrowIfNull(source);

    //    return builder.WithBindMount(source, "/cockroach/cockroach-data");
    //}

    ///// <summary>
    ///// Defines the SQL script used to create the database.
    ///// </summary>
    ///// <param name="builder">The builder for the <see cref="DirectusDatabaseResource"/>.</param>
    ///// <param name="script">The SQL script used to create the database.</param>
    ///// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    ///// <ats-returns>The resource builder.</ats-returns>
    ///// <remarks>
    ///// The script can only contain SQL statements applying to the default database like CREATE DATABASE. Custom statements like table creation
    ///// and data insertion are not supported since they require a distinct connection to the newly created database.
    ///// <value>Default script is <code>CREATE DATABASE "&lt;QUOTED_DATABASE_NAME&gt;"</code></value>
    ///// </remarks>
    //[AspireExport]
    //public static IResourceBuilder<DirectusDatabaseResource> WithCreationScript(this IResourceBuilder<DirectusDatabaseResource> builder, string script)
    //{
    //    ArgumentNullException.ThrowIfNull(builder);
    //    ArgumentNullException.ThrowIfNull(script);

    //    builder.WithAnnotation(new DirectusCreateDatabaseScriptAnnotation(script));

    //    return builder;
    //}

    /// <summary>
    /// Configures the password that the Directus resource is used.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="password">The parameter used to provide the password for the Directus resource.</param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    /// <ats-returns>The resource builder.</ats-returns>
    [AspireExport]
    public static IResourceBuilder<DirectusResource> WithPassword(this IResourceBuilder<DirectusResource> builder, IResourceBuilder<ParameterResource> password)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(password);

        builder.Resource.PasswordParameter = password.Resource;
        return builder;
    }

    /// <summary>
    /// Configures the user name that the Directus resource is used.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="userName">The parameter used to provide the user name for the Directus resource.</param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    /// <ats-returns>The resource builder.</ats-returns>
    [AspireExport]
    public static IResourceBuilder<DirectusResource> WithUserName(this IResourceBuilder<DirectusResource> builder, IResourceBuilder<ParameterResource> userName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(userName);

        builder.Resource.UserNameParameter = userName.Resource;
        return builder;
    }

    /// <summary>
    /// Configures the admin token that the Directus resource is used.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="adminToken">The parameter used to provide the admin token for the Directus resource.</param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    /// <ats-returns>The resource builder.</ats-returns>
    [AspireExport]
    public static IResourceBuilder<DirectusResource> WithAdminToken(this IResourceBuilder<DirectusResource> builder, IResourceBuilder<ParameterResource> adminToken)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(adminToken);

        builder.Resource.AdminTokenParameter = adminToken.Resource;
        return builder;
    }

    /// <summary>
    /// Configures the Directus resource to use an embedded SQLite database stored in the data folder.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    [AspireExport]
    public static IResourceBuilder<DirectusResource> WithSqlite3Database(this IResourceBuilder<DirectusResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithEnvironment("DB_CLIENT", "sqlite3")
            .WithEnvironment("DB_FILENAME", "/directus/data/directus.db");
    }

    /// <summary>
    /// Adds a named volume for the data folder to a Directus container resource.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">The name of the volume. Defaults to an auto-generated name based on the application and resource names.</param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    [AspireExport]
    public static IResourceBuilder<DirectusResource> WithDataVolume(this IResourceBuilder<DirectusResource> builder, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithVolume(name ?? VolumeNameGenerator.Generate(builder, "data"), "/directus/data");
    }

    /// <summary>
    /// Adds a bind mount for the data folder to a Directus container resource.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="source">The source directory on the host to mount into the container.</param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    [AspireExport]
    public static IResourceBuilder<DirectusResource> WithDataBindMount(this IResourceBuilder<DirectusResource> builder, string source)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(source);

        return builder.WithBindMount(source, "/directus/data");
    }

    /// <summary>
    /// Configures the Directus resource to use a PostgreSQL database.
    /// This method reads connection properties from the provided <paramref name="database"/>
    /// (which must implement <see cref="IResourceWithConnectionString"/>) and sets the
    /// environment variables required by the Directus image to connect to a Postgres backend.
    /// It also creates a reference relationship and waits for the database resource.
    /// </summary>
    /// <param name="builder">The Directus resource builder to configure.</param>
    /// <param name="database">A resource builder for the PostgreSQL database. Must expose connection string information.</param>
    /// <param name="useTls">Whether to require TLS for the database connection (<c>DB_SSLMODE=require</c>).</param>
    /// <returns>The same <see cref="IResourceBuilder{DirectusResource}"/> instance for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when the provided <paramref name="database"/> does not expose connection string information.</exception>
    [AspireExport]
    public static IResourceBuilder<DirectusResource> WithPostgresDatabase(this IResourceBuilder<DirectusResource> builder, IResourceBuilder<IResource> database, bool useTls = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(database);

        if (database.Resource is IResourceWithConnectionString resourceWithConnection)
        {
            builder.WithEnvironment("DB_CLIENT", "pg")
                .WithEnvironment("DB_HOST", $"{resourceWithConnection.GetConnectionProperty("Host")}")
                .WithEnvironment("DB_PORT", $"{resourceWithConnection.GetConnectionProperty("Port")}")
                .WithEnvironment("DB_DATABASE", $"{resourceWithConnection.GetConnectionProperty("DatabaseName")}")
                .WithEnvironment("DB_USER", $"{resourceWithConnection.GetConnectionProperty("Username")}")
                .WithEnvironment("DB_PASSWORD", $"{resourceWithConnection.GetConnectionProperty("Password")}")
                .WithEnvironment("DB_SSLMODE", useTls ? "require" : "none")
                .WithReferenceRelationship(database)
                .WaitFor(database);

            return builder;
        }
        else
        {
            throw new ArgumentException($"The provided resource '{database.Resource.Name}' does not contain connection string information and cannot be used as a database for Directus.", nameof(database));
        }
    }

    /// <summary>
    /// Configures the Directus resource to use a MSSQL database.
    /// This method reads connection properties from the provided <paramref name="database"/>
    /// (which must implement <see cref="IResourceWithConnectionString"/>) and sets the
    /// environment variables required by the Directus image to connect to a MSSQL backend.
    /// It also creates a reference relationship and waits for the database resource.
    /// </summary>
    /// <param name="builder">The Directus resource builder to configure.</param>
    /// <param name="database">A resource builder for the MSSQL database. Must expose connection string information.</param>
    /// <param name="useTls">Reserved for TLS configuration of the database connection; currently not applied for MSSQL.</param>
    /// <returns>The same <see cref="IResourceBuilder{DirectusResource}"/> instance for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when the provided <paramref name="database"/> does not expose connection string information.</exception>
    [AspireExport]
    public static IResourceBuilder<DirectusResource> WithSqlServerDatabase(this IResourceBuilder<DirectusResource> builder, IResourceBuilder<IResource> database, bool useTls = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(database);

        if (database.Resource is IResourceWithConnectionString resourceWithConnection)
        {
            builder.WithEnvironment("DB_CLIENT", "mssql")
                .WithEnvironment("DB_HOST", $"{resourceWithConnection.GetConnectionProperty("Host")}")
                .WithEnvironment("DB_PORT", $"{resourceWithConnection.GetConnectionProperty("Port")}")
                .WithEnvironment("DB_DATABASE", $"{resourceWithConnection.GetConnectionProperty("DatabaseName")}")
                .WithEnvironment("DB_USER", $"{resourceWithConnection.GetConnectionProperty("Username")}")
                .WithEnvironment("DB_PASSWORD", $"{resourceWithConnection.GetConnectionProperty("Password")}")
                .WithReferenceRelationship(database)
                .WaitFor(database);

            return builder;
        }
        else
        {
            throw new ArgumentException($"The provided resource '{database.Resource.Name}' does not contain connection string information and cannot be used as a database for Directus.", nameof(database));
        }
    }
}