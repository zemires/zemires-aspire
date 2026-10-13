using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zemires.Aspire.Hosting.Zitadel;
using Zitadel.Api;
using Zitadel.Management.V1;
using Zitadel.Project.V1;

// ReSharper disable once CheckNamespace
namespace Aspire.Hosting;

/// <summary>
/// Extension methods for adding and configuring Zitadel identity and access management resources in an Aspire application.
/// </summary>
public static class ZitadelBuilderExtensions
{
    private const int DefaultContainerPort = 8080;
    private const int DefaultContainerPortLoginClient = 3000;
    private const string DatabaseToken = "Database=";

    /// <summary>
    /// Adds a Zitadel container resource to the <see cref="IDistributedApplicationBuilder"/>.
    /// </summary>
    /// <ats-summary>Adds a Zitadel container resource</ats-summary>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder" /> to add the Zitadel container to.</param>
    /// <param name="name">The name of the resource. This name will be used as the connection string name when referenced in a dependency.</param>
    /// <param name="port">The host port used when launching the container. If <c>null</c> a random port will be assigned</param>
    /// <param name="username">An optional parameter to set a username for the admin account, if <c>null</c> will auto generate one.</param>
    /// <param name="password">An optional parameter to set a password for the admin account, if <c>null</c> will auto generate one.</param>
    /// <param name="masterKey">An optional parameter to set the masterkey, if <c>null</c> will auto generate one.</param>
    [AspireExport]
    public static IResourceBuilder<ZitadelResource> AddZitadel(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        int? port = null,
        IResourceBuilder<ParameterResource>? username = null,
        IResourceBuilder<ParameterResource>? password = null,
        IResourceBuilder<ParameterResource>? masterKey = null
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);

        var usernameParameter = username?.Resource ?? new ParameterResource($"{name}-username", _ => "admin", false);
        var passwordParameter = password?.Resource ?? ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, $"{name}-password", minSpecial: 1);
        var masterKeyParameter = masterKey?.Resource ?? ParameterResourceBuilderExtensions.CreateGeneratedParameter(builder, $"{name}-masterKey", true, new GenerateParameterDefault
        {
            MinLength = 32, // Zitadel requires 32, CreateDefaultPasswordParameter generates 22
            Lower = true,
            Upper = true,
            Numeric = true,
            Special = true,
            MinLower = 1,
            MinUpper = 1,
            MinNumeric = 1,
            MinSpecial = 1
        });

        var zitadel = new ZitadelResource(name, usernameParameter, passwordParameter, masterKeyParameter);

        var zitadelBuilder = builder.AddResource(zitadel)
            .WithImage(ZitadelContainerImageTags.Image)
            .WithImageTag(ZitadelContainerImageTags.Tag)
            .WithImageRegistry(ZitadelContainerImageTags.Registry)
            .WithArgs("start-from-init", "--masterkeyFromEnv")
            .WithHttpEndpoint(
                targetPort: DefaultContainerPort,
                port: port,
                name: ZitadelResource.PrimaryEndpointName
            )
            .WithHttpHealthCheck("/healthz")
            .WithEnvironment("ZITADEL_MASTERKEY", masterKeyParameter)
            .WithUrlForEndpoint(ZitadelResource.PrimaryEndpointName, e => e.DisplayText = "Zitadel Dashboard");

#pragma warning disable ASPIRECERTIFICATES001
        zitadelBuilder.WithHttpsCertificateConfiguration(ctx =>
        {
            ctx.EnvironmentVariables["ZITADEL_EXTERNALSECURE"] = "true";
            ctx.EnvironmentVariables["ZITADEL_TLS_ENABLED"] = "true";
            ctx.EnvironmentVariables["ZITADEL_TLS_CERTPATH"] = ctx.CertificatePath;
            ctx.EnvironmentVariables["ZITADEL_TLS_KEYPATH"] = ctx.KeyPath;
            return Task.CompletedTask;
        });
#pragma warning restore ASPIRECERTIFICATES001

        if (builder.ExecutionContext.IsRunMode)
        {
#pragma warning disable ASPIRECERTIFICATES001
            builder.Eventing.Subscribe<BeforeStartEvent>((@event, cancellationToken) =>
            {
                var developerCertificateService = @event.Services.GetRequiredService<IDeveloperCertificateService>();

                bool addHttps = false;
                if (!zitadelBuilder.Resource.TryGetLastAnnotation<HttpsCertificateAnnotation>(out var annotation))
                {
                    if (developerCertificateService.UseForHttps)
                    {
                        // If no certificate is configured, and the developer certificate service supports container trust,
                        // configure the resource to use the developer certificate for its key pair.
                        addHttps = true;
                    }
                }
                else if (annotation.UseDeveloperCertificate.GetValueOrDefault(developerCertificateService.UseForHttps) || annotation.Certificate is not null)
                {
                    addHttps = true;
                }

                if (addHttps)
                {
                    // If a TLS certificate is configured, override the endpoint to use HTTPS instead of HTTP
                    // Zitadel only binds to a single port
                    zitadelBuilder
                        .WithEndpoint(ZitadelResource.PrimaryEndpointName, ep => ep.UriScheme = "https");
                }

                return Task.CompletedTask;
            });
#pragma warning restore ASPIRECERTIFICATES001
        }

        builder.Eventing.Subscribe<ResourceReadyEvent>(zitadel, async (@event, ct) =>
        {
            IServiceProvider services = @event.Services;
            ResourceNotificationService notificationService = services.GetRequiredService<ResourceNotificationService>();
            ResourceLoggerService loggerService = services.GetRequiredService<ResourceLoggerService>();
            ILogger logger = loggerService.GetLogger(zitadel);

            List<ZitadelProjectResource> projects = zitadel.Projects.Keys
                .Select(name => builder.Resources.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.InvariantCultureIgnoreCase)))
                .OfType<ZitadelProjectResource>()
                .ToList();

            if (!zitadel.HasAnnotationOfType<ZitadelInitializationAnnotation>() && projects.Count == 0)
            {
                ResolveOutputsFromConfiguration(zitadel, services);
                return;
            }

            Clients.Options? options = null;
            try
            {
                options = await zitadel.CreateApiClientOptions(ct);
                await RunInitializationAsync(new ZitadelInitializationContext(options, zitadel, services, logger, ct));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Initialization of '{Resource}' failed.", zitadel.Name);
                FailUnresolvedOutputs(zitadel, ex);
            }

            foreach (ZitadelProjectResource project in projects)
            {
                ILogger projectLogger = loggerService.GetLogger(project);
                try
                {
                    if (options is null)
                    {
                        throw new InvalidOperationException($"Zitadel API client for '{zitadel.Name}' is not available, see the logs of '{zitadel.Name}'.");
                    }

                    await CreateZitadelProject(new ZitadelInitializationContext(options, project, services, projectLogger, ct), notificationService);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    projectLogger.LogError(ex, "Initialization of '{Resource}' failed.", project.Name);
                    FailUnresolvedOutputs(project, ex);
                    await notificationService.PublishUpdateAsync(project,
                        state => state with { State = new ResourceStateSnapshot(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error) });
                }
            }
        });

        zitadelBuilder.WithOtlpExporter(OtlpProtocol.Grpc)
            .WithEnvironment((ctx) => {
                //ctx.EnvironmentVariables["ZITADEL_OTEL_EXPORTER_OTLP_ENDPOINT"] = ctx.EnvironmentVariables["";

            });

        return zitadelBuilder
            .WithEnvironment(ctx =>
            {
                var resource = ctx.Resource;
                if (resource.TryGetEndpoints(out var endpoints))
                {
                    var endpoint = endpoints.Single(e => e.Name == ZitadelResource.PrimaryEndpointName);
                    ctx.EnvironmentVariables.TryAdd("ZITADEL_EXTERNALDOMAIN", endpoint.TargetHost);
                    var port = endpoint.Port;
                    if (port is not null)
                    {
                        ctx.EnvironmentVariables.TryAdd("ZITADEL_EXTERNALPORT", port);
                    }
                }
            })
            // Disable Login V2 for simpler setup (no separate login container needed)
            .WithEnvironment("ZITADEL_DEFAULTINSTANCE_FEATURES_LOGINV2_REQUIRED", "false")
            // Configure admin user
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_HUMAN_USERNAME", usernameParameter)
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_HUMAN_PASSWORD", passwordParameter)
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_HUMAN_PASSWORDCHANGEREQUIRED", "false");
    }

    /// <summary>
    /// Configures the Zitadel resource to use a PostgreSQL database for persistent storage of identity and access management data.
    /// This method extracts connection information from the provided database resource and configures Zitadel environment variables.
    /// </summary>
    /// <param name="builder">The Zitadel resource builder to configure with database settings.</param>
    /// <param name="database">The PostgreSQL database resource with connection string information.</param>
    /// <param name="userPassword">Optional parameter resource for the database user password. If not provided, a secure password will be generated.</param>
    /// <param name="sslModeEnabled">Whether to enable SSL mode for database connections. Default is false.</param>
    /// <returns>The Zitadel resource builder for method chaining.</returns>
    [AspireExport]
    public static IResourceBuilder<ZitadelResource> WithDatabase(this IResourceBuilder<ZitadelResource> builder,
        IResourceBuilder<IResourceWithConnectionString> database, IResourceBuilder<ParameterResource>? userPassword = null, bool sslModeEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(database);
        
        ParameterResource userPasswordParameter = userPassword?.Resource ??
                                                  ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder.ApplicationBuilder,
                                                      $"{database.Resource.Name}-user-password");

        builder.WithEnvironment(context =>
            {
                // extract the server parameters generically from IResourceWithConnectionString to support different postgres resources
                IResourceWithConnectionString serverResource;
                if (database.Resource is IResourceWithParent parentResource)
                {
                    serverResource = (IResourceWithConnectionString)parentResource.Parent;
                }
                else
                {
                    throw new InvalidOperationException("Parameter must be a database resource created from a parent server resource.");
                }

                string? part = database.Resource.ConnectionStringExpression.ValueExpression.Split(';').FirstOrDefault(p => p.StartsWith(DatabaseToken));
                if (part == null)
                {
                    throw new InvalidOperationException("could not parse ConnectionString for database name");
                }

                string databaseName = part.Substring(DatabaseToken.Length);

                // Assumption: ConnectionStrings are using ValueProvider in the format: "Host=host;Port=port;Username=username;Password=password;Database=database"
                if (serverResource.ConnectionStringExpression.ValueProviders.Count < 4)
                {
                    throw new InvalidOperationException("server of database has not proper connection string format");
                }

                if (context.ExecutionContext.IsRunMode && serverResource is ContainerResource serverContainerResource)
                {
                    // workaround to resolve container internal host and port, default resolving only returns external host and port
                    context.EnvironmentVariables["ZITADEL_DATABASE_POSTGRES_PORT"] = serverContainerResource.GetEndpoint("tcp").Property(EndpointProperty.TargetPort);
                    context.EnvironmentVariables["ZITADEL_DATABASE_POSTGRES_HOST"] = serverContainerResource.Name; // use name 
                }
                else
                {
                    context.EnvironmentVariables["ZITADEL_DATABASE_POSTGRES_HOST"] = serverResource.ConnectionStringExpression.ValueProviders[0];
                    context.EnvironmentVariables["ZITADEL_DATABASE_POSTGRES_PORT"] = serverResource.ConnectionStringExpression.ValueProviders[1];
                }

                context.EnvironmentVariables["ZITADEL_DATABASE_POSTGRES_DATABASE"] = databaseName;
                context.EnvironmentVariables["ZITADEL_DATABASE_POSTGRES_USER_USERNAME"] = "zitadel-user";
                context.EnvironmentVariables["ZITADEL_DATABASE_POSTGRES_USER_PASSWORD"] = userPasswordParameter;
                context.EnvironmentVariables["ZITADEL_DATABASE_POSTGRES_USER_SSL_MODE"] = sslModeEnabled ? "enable" : "disable";
                context.EnvironmentVariables["ZITADEL_DATABASE_POSTGRES_ADMIN_USERNAME"] = serverResource.ConnectionStringExpression.ValueProviders[2];
                context.EnvironmentVariables["ZITADEL_DATABASE_POSTGRES_ADMIN_PASSWORD"] = serverResource.ConnectionStringExpression.ValueProviders[3];
                context.EnvironmentVariables["ZITADEL_DATABASE_POSTGRES_ADMIN_SSL_MODE"] = sslModeEnabled ? "enable" : "disable";
            })
            .WithReference(database)
            .WaitFor(database);

        return builder;
    }

    /// <summary>
    /// Configures a machine user for programmatic access to the Zitadel instance with API key authentication.
    /// Creates a service account that can be used for server-to-server communication and API automation.
    /// </summary>
    /// <param name="builder">The Zitadel resource builder to configure with machine user settings.</param>
    /// <param name="machineUserKeyParameter">The machine user key as a parameter. It's a base64 encoded json key.</param>
    /// <param name="machineUserName">The machine user name for the service account. Default is "admin".</param>
    /// <returns>The Zitadel resource builder for method chaining.</returns>
    [AspireExport]
    public static IResourceBuilder<ZitadelResource> WithMachineUser(this IResourceBuilder<ZitadelResource> builder, ParameterResource? machineUserKeyParameter = null, string machineUserName = "admin")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(machineUserName);

        if (machineUserKeyParameter != null)
        {
            builder.Resource.MachineUserKeyParameter = machineUserKeyParameter;
            return builder;
        }

        string path = GetLocalKeysPath(builder.Resource.Name);
        builder.Resource.TryGetAnnotationsOfType<ContainerMountAnnotation>(out IEnumerable<ContainerMountAnnotation>? mountAnnotations);
        if (mountAnnotations == null || mountAnnotations.All(m => m.Target != "/keys"))
        {
            builder.WithBindMount(path, "/keys");
        }

        builder
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_MACHINEKEYPATH", $"/keys/{machineUserName}.json")
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_MACHINE_MACHINE_USERNAME", machineUserName)
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_MACHINE_MACHINE_NAME", "Automatically Initialized IAM_OWNER")
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_MACHINE_MACHINEKEY_TYPE", "1");

        builder.Resource.MachineUserKeyPath = Path.Combine(path, $"{machineUserName}.json");

        return builder;
    }

    /// <summary>
    /// Configures the Zitadel resource to use a custom SSL certificate for HTTPS connections.
    /// Allows specification of custom certificate and private key files for production scenarios.
    /// </summary>
    /// <param name="builder">The Zitadel resource builder to configure with certificate settings.</param>
    /// <param name="certificatePath">The file system path to the SSL certificate file.</param>
    /// <param name="keyPath">The file system path to the private key file corresponding to the certificate.</param>
    /// <param name="certificateDestinationPath">The container path where certificate files will be mounted. Default is "/certificate".</param>
    /// <returns>The Zitadel resource builder for method chaining.</returns>
    [AspireExport]
    public static IResourceBuilder<ZitadelResource> WithCertificate(this IResourceBuilder<ZitadelResource> builder, string certificatePath, string keyPath, string certificateDestinationPath = "/certificate")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(certificatePath);
        ArgumentException.ThrowIfNullOrEmpty(keyPath);

        string certFileName = Path.GetFileName(certificatePath);
        string certKeyFileName = Path.GetFileName(keyPath);

        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            builder.WithContainerFiles(certificateDestinationPath, [
                new ContainerFile { Name = certFileName, Contents = File.ReadAllText(certificatePath) },
                new ContainerFile { Name = certKeyFileName, Contents = File.ReadAllText(keyPath) }
            ]);
        }
        else
        {
            string sourceFullPath = Path.GetFullPath(Path.GetDirectoryName(certificatePath) ?? ".", builder.ApplicationBuilder.AppHostDirectory);
            builder.WithBindMount(sourceFullPath, certificateDestinationPath);
        }

        return builder.WithEnvironment("ZITADEL_TLS_CERTPATH", certificateDestinationPath + "/" + certFileName)
            .WithEnvironment("ZITADEL_TLS_KEYPATH", certificateDestinationPath + "/" + certKeyFileName);
    }

    /// <summary>
    /// Configures the external domain for the Zitadel resource. This overrides the default domain set in <see cref="AddZitadel"/>.
    /// </summary>
    /// <ats-summary>Configures the external domain for the Zitadel resource</ats-summary>
    /// <param name="builder">The Zitadel resource builder.</param>
    /// <param name="externalDomain">The external domain to use (e.g., "auth.example.com"). Cannot be null or empty.</param>
    /// <returns>The resource builder for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="externalDomain"/> is null or whitespace.</exception>
    [AspireExport("withExternalDomainDomain")]
    public static IResourceBuilder<ZitadelResource> WithExternalDomain(
        this IResourceBuilder<ZitadelResource> builder,
        string externalDomain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalDomain);

        return builder.WithEnvironment("ZITADEL_EXTERNALDOMAIN", externalDomain);
    }

    /// <summary>
    /// Configures the external domain settings for the Zitadel instance using an endpoint reference.
    /// Sets up how Zitadel identifies itself to external clients and handles redirects and callbacks.
    /// </summary>
    /// <param name="builder">The Zitadel resource builder to configure with external domain settings.</param>
    /// <param name="endpoint">The endpoint reference containing host, port, and scheme information for external access.</param>
    /// <returns>The Zitadel resource builder for method chaining.</returns>
    [AspireExport("withExternalDomainEndpoint")]
    public static IResourceBuilder<ZitadelResource> WithExternalDomain(this IResourceBuilder<ZitadelResource> builder, EndpointReference endpoint)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(endpoint);

        return builder.WithEnvironment("ZITADEL_EXTERNALSECURE", endpoint.Scheme == "https" ? "true" : "false")
            .WithEnvironment("ZITADEL_EXTERNALPORT", endpoint.Property(EndpointProperty.Port))
            .WithEnvironment("ZITADEL_EXTERNALDOMAIN", endpoint.Property(EndpointProperty.Host));
    }

    /// <summary>
    /// Configures the external domain settings for the Zitadel instance with explicit parameters.
    /// Allows direct specification of security, host, and port settings for external access configuration.
    /// </summary>
    /// <param name="builder">The Zitadel resource builder to configure with external domain settings.</param>
    /// <param name="isSecure">Whether the external domain uses HTTPS (true) or HTTP (false).</param>
    /// <param name="host">The external domain host name or IP address that clients will use to access Zitadel.</param>
    /// <param name="port">The port number that clients will use to access Zitadel externally.</param>
    /// <returns>The Zitadel resource builder for method chaining.</returns>
    [AspireExport("withExternalDomainHost")]
    public static IResourceBuilder<ZitadelResource> WithExternalDomain(this IResourceBuilder<ZitadelResource> builder, bool isSecure, string host, int port)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(host);

        return builder.WithEnvironment("ZITADEL_EXTERNALSECURE", isSecure.ToString)
            .WithEnvironment("ZITADEL_EXTERNALPORT", port.ToString())
            .WithEnvironment("ZITADEL_EXTERNALDOMAIN", host);
    }

    /// <summary>
    /// Configures a login client with personal access token for custom login UI integration.
    /// Creates the necessary configuration for implementing a custom login interface that integrates with Zitadel's authentication flows.
    /// </summary>
    /// <param name="builder">The Zitadel resource builder to configure with login client settings.</param>
    /// <param name="loginUser">The login client user name for the service account. Default is "login-client".</param>
    /// <returns>The Zitadel resource builder for method chaining.</returns>
    [AspireExport]
    public static IResourceBuilder<ZitadelResource> WithLoginClientKey(this IResourceBuilder<ZitadelResource> builder, string loginUser = "login-client")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(loginUser);

        builder.Resource.TryGetAnnotationsOfType<ContainerMountAnnotation>(out IEnumerable<ContainerMountAnnotation>? mountAnnotations);
        if (mountAnnotations == null || mountAnnotations.All(m => m.Target != "/keys"))
        {
            string path = GetLocalKeysPath(builder.Resource.Name);
            builder.WithBindMount(path, "/keys");
        }

        builder
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_LOGINCLIENTPATPATH", $"/keys/{loginUser}.pat")
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_LOGINCLIENT_MACHINE_USERNAME", loginUser)
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_LOGINCLIENT_MACHINE_NAME", "Automatically Initialized IAM_LOGIN_CLIENT")
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_LOGINCLIENT_PAT_EXPIRATIONDATE", DateTime.UtcNow.AddYears(5).ToString("yyyy-MM-ddTHH:mm:ssZ"));

        return builder;
    }

    /// <summary>
    /// Adds a Zitadel login client container for custom login UI functionality.
    /// Deploys a Next.js-based login interface that provides a customizable authentication experience for end users.
    /// </summary>
    /// <param name="builder">The Zitadel resource builder to add the login client to.</param>
    /// <param name="name">The name of the login client resource for service discovery and configuration.</param>
    /// <param name="port">Optional host port for the login client. If not specified, a random port will be assigned.</param>
    /// <param name="loginUser">The login user name for authentication with the Zitadel API. Default is "login-client".</param>
    /// <param name="serviceAccessToken">Optional parameter resource for the service access token. If not provided, it will be read from the generated PAT file.</param>
    /// <returns>A resource builder for the Zitadel login client resource that can be used for further configuration.</returns>
    [AspireExport]
    public static IResourceBuilder<ZitadelLoginClientResource> AddZitadelLoginClient(this IResourceBuilder<ZitadelResource> builder, string name, int? port = null,
        string loginUser = "login-client", IResourceBuilder<ParameterResource>? serviceAccessToken = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        
        string path = GetLocalKeysPath(builder.Resource.Name);
        ParameterResource serviceAccessTokenParameter = serviceAccessToken?.Resource ??
                                                        new ParameterResource(name, @default => File.ReadAllText(Path.Combine(path, $"{loginUser}.pat")), true);

        ZitadelLoginClientResource loginClientResource = new(name, serviceAccessTokenParameter);

        IResourceBuilder<ZitadelLoginClientResource> loginBuilder = builder.ApplicationBuilder.AddResource(loginClientResource)
            .WithImage(ZitadelContainerImageTags.LoginImage)
            .WithImageRegistry(ZitadelContainerImageTags.Registry)
            .WithImageTag(ZitadelContainerImageTags.LoginTag)
            .WithHttpEndpoint(port, DefaultContainerPortLoginClient)
            .WithEnvironment("ZITADEL_API_URL", builder.Resource.PrimaryEndpoint)
            .WithEnvironment("NEXT_PUBLIC_BASE_PATH", ZitadelLoginClientResource.BasePath)
            .WithEnvironment("ZITADEL_SERVICE_USER_TOKEN", serviceAccessTokenParameter)
            .WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
            .WithEnvironment("CUSTOM_REQUEST_HEADERS", "Host:localhost")
            .WithHttpHealthCheck(ZitadelLoginClientResource.BasePath + "/healthy", 200)
            .WaitFor(builder);

        builder.WithEnvironment("ZITADEL_DEFAULTINSTANCE_FEATURES_LOGINV2_REQUIRED", "true")
            .WithEnvironment(async context =>
            {
                // workaround to resolve external address of login client
                if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
                {
                    context.EnvironmentVariables["ZITADEL_DEFAULTINSTANCE_FEATURES_LOGINV2_BASEURI"] =
                        await loginBuilder.Resource.BaseEndpoint.GetValueAsync(context.CancellationToken) ?? "";
                    context.EnvironmentVariables["ZITADEL_OIDC_DEFAULTLOGINURLV2"] = await loginBuilder.Resource.OidcLoginEndpoint.GetValueAsync(context.CancellationToken) ?? "";
                    context.EnvironmentVariables["ZITADEL_OIDC_DEFAULTLOGOUTURLV2"] = await loginBuilder.Resource.OidcLogoutEndpoint.GetValueAsync(context.CancellationToken) ?? "";
                    context.EnvironmentVariables["ZITADEL_SAML_DEFAULTLOGINURLV2"] = await loginBuilder.Resource.SamlLoginEndpoint.GetValueAsync(context.CancellationToken) ?? "";
                }
                else
                {
                    context.EnvironmentVariables["ZITADEL_DEFAULTINSTANCE_FEATURES_LOGINV2_BASEURI"] = loginBuilder.Resource.BaseEndpoint;
                    context.EnvironmentVariables["ZITADEL_OIDC_DEFAULTLOGINURLV2"] = loginBuilder.Resource.OidcLoginEndpoint;
                    context.EnvironmentVariables["ZITADEL_OIDC_DEFAULTLOGOUTURLV2"] = loginBuilder.Resource.OidcLogoutEndpoint;
                    context.EnvironmentVariables["ZITADEL_SAML_DEFAULTLOGINURLV2"] = loginBuilder.Resource.SamlLoginEndpoint;
                }
            });

        return loginBuilder;
    }

    /// <summary>
    /// Configures a specific host port for the Zitadel resource instead of using a randomly assigned port.
    /// This is useful for development scenarios where you need predictable port assignments.
    /// </summary>
    /// <param name="builder">The Zitadel resource builder to configure with a specific port.</param>
    /// <param name="port">The port to bind on the host. If null, a random port will be assigned.</param>
    /// <returns>The Zitadel resource builder for method chaining.</returns>
    [AspireExport("withZitadelHostPort", MethodName = "withHostPort")]
    public static IResourceBuilder<ZitadelResource> WithHostPort(this IResourceBuilder<ZitadelResource> builder, int? port)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithEndpoint(builder.Resource.PrimaryEndpoint.EndpointName, endpoint =>
        {
            endpoint.Port = port;
        });
    }

    /// <summary>
    /// Configures a specific host port for the Zitadel login client resource instead of using a randomly assigned port.
    /// This is useful for development scenarios where you need predictable port assignments for the login UI.
    /// </summary>
    /// <param name="builder">The Zitadel login client resource builder to configure with a specific port.</param>
    /// <param name="port">The port to bind on the host. If null, a random port will be assigned.</param>
    /// <returns>The Zitadel login client resource builder for method chaining.</returns>
    [AspireExport("withZitadelLoginClientHostPort", MethodName = "withHostPort")]
    public static IResourceBuilder<ZitadelLoginClientResource> WithHostPort(this IResourceBuilder<ZitadelLoginClientResource> builder, int? port)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithEndpoint(ZitadelLoginClientResource.PrimaryEndpointName, endpoint =>
        {
            endpoint.Port = port;
        });
    }

    /// <summary>
    /// Configures an initialization function that executes when the Zitadel resource becomes ready.
    /// This allows for custom setup logic such as creating organizations, projects, or configuring initial settings.
    /// </summary>
    /// <param name="builder">The Zitadel resource builder to configure with initialization logic.</param>
    /// <param name="initialization">A function that takes Zitadel API client options and the resource, and performs initialization tasks.</param>
    /// <returns>The Zitadel resource builder for method chaining.</returns>
    [AspireExportIgnore(Reason = "The initialization callback receives Zitadel API client options, which are not ATS-compatible.")]
    public static IResourceBuilder<ZitadelResource> WithInitialization(this IResourceBuilder<ZitadelResource> builder,
        Func<Clients.Options, IResource, Task> initialization)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(initialization);
        return builder.WithAnnotation(new ZitadelInitializationAnnotation(initialization));
    }

    /// <summary>
    /// Configures an initialization function that executes when the Zitadel project resource becomes ready.
    /// This allows for custom project setup logic such as creating applications, roles, or configuring project-specific settings.
    /// </summary>
    /// <param name="builder">The Zitadel project resource builder to configure with initialization logic.</param>
    /// <param name="initialization">A function that takes Zitadel API client options and the project resource, and performs initialization tasks.</param>
    /// <returns>The Zitadel project resource builder for method chaining.</returns>
    [AspireExportIgnore(Reason = "The initialization callback receives Zitadel API client options, which are not ATS-compatible.")]
    public static IResourceBuilder<ZitadelProjectResource> WithInitialization(this IResourceBuilder<ZitadelProjectResource> builder,
        Func<Clients.Options, IResource, Task> initialization)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(initialization);
        return builder.WithAnnotation(new ZitadelInitializationAnnotation(initialization));
    }

    /// <summary>
    /// Configures an initialization function that executes when the Zitadel resource becomes ready.
    /// The context provides the API client options and lets the function set outputs (see <see cref="GetOutput(IResourceBuilder{ZitadelResource}, string)" />).
    /// </summary>
    /// <param name="builder">The Zitadel resource builder to configure with initialization logic.</param>
    /// <param name="initialization">A function receiving the initialization context.</param>
    /// <returns>The Zitadel resource builder for method chaining.</returns>
    [AspireExportIgnore(Reason = "The initialization callback receives Zitadel API client options, which are not ATS-compatible.")]
    public static IResourceBuilder<ZitadelResource> WithInitialization(this IResourceBuilder<ZitadelResource> builder,
        Func<ZitadelInitializationContext, Task> initialization)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(initialization);
        return builder.WithAnnotation(new ZitadelInitializationAnnotation(initialization));
    }

    /// <summary>
    /// Configures an initialization function that executes when the Zitadel project has been created.
    /// The context provides the API client options and lets the function set outputs (see <see cref="GetOutput(IResourceBuilder{ZitadelProjectResource}, string)" />).
    /// </summary>
    /// <param name="builder">The Zitadel project resource builder to configure with initialization logic.</param>
    /// <param name="initialization">A function receiving the initialization context.</param>
    /// <returns>The Zitadel project resource builder for method chaining.</returns>
    [AspireExportIgnore(Reason = "The initialization callback receives Zitadel API client options, which are not ATS-compatible.")]
    public static IResourceBuilder<ZitadelProjectResource> WithInitialization(this IResourceBuilder<ZitadelProjectResource> builder,
        Func<ZitadelInitializationContext, Task> initialization)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(initialization);
        return builder.WithAnnotation(new ZitadelInitializationAnnotation(initialization));
    }

    /// <summary>
    /// Gets an output of the Zitadel resource, set by an initialization function with <see cref="ZitadelInitializationContext.SetOutput" />.
    /// The value is resolved lazily when consumed, e.g. with <c>WithEnvironment(name, output)</c>.
    /// </summary>
    /// <param name="builder">The Zitadel resource builder.</param>
    /// <param name="name">The name of the output.</param>
    /// <returns>A reference to the output value.</returns>
    [AspireExportIgnore(Reason = "ZitadelOutputReference is not ATS-compatible.")]
    public static ZitadelOutputReference GetOutput(this IResourceBuilder<ZitadelResource> builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return ZitadelOutputsAnnotation.GetOrAdd(builder.Resource, name, isSecret: false);
    }

    /// <summary>
    /// Gets a secret output of the Zitadel resource, set by an initialization function with <see cref="ZitadelInitializationContext.SetOutput" />.
    /// </summary>
    /// <param name="builder">The Zitadel resource builder.</param>
    /// <param name="name">The name of the output.</param>
    /// <returns>A reference to the output value.</returns>
    [AspireExportIgnore(Reason = "ZitadelOutputReference is not ATS-compatible.")]
    public static ZitadelOutputReference GetSecretOutput(this IResourceBuilder<ZitadelResource> builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return ZitadelOutputsAnnotation.GetOrAdd(builder.Resource, name, isSecret: true);
    }

    /// <summary>
    /// Gets an output of the Zitadel project, set by an initialization function with <see cref="ZitadelInitializationContext.SetOutput" />,
    /// e.g. the client id of an application created by the initialization. The value is resolved lazily when consumed,
    /// e.g. with <c>WithEnvironment(name, output)</c>.
    /// </summary>
    /// <param name="builder">The Zitadel project resource builder.</param>
    /// <param name="name">The name of the output.</param>
    /// <returns>A reference to the output value.</returns>
    [AspireExportIgnore(Reason = "ZitadelOutputReference is not ATS-compatible.")]
    public static ZitadelOutputReference GetOutput(this IResourceBuilder<ZitadelProjectResource> builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return ZitadelOutputsAnnotation.GetOrAdd(builder.Resource, name, isSecret: false);
    }

    /// <summary>
    /// Gets a secret output of the Zitadel project, set by an initialization function with <see cref="ZitadelInitializationContext.SetOutput" />,
    /// e.g. the client secret of an application created by the initialization.
    /// </summary>
    /// <param name="builder">The Zitadel project resource builder.</param>
    /// <param name="name">The name of the output.</param>
    /// <returns>A reference to the output value.</returns>
    [AspireExportIgnore(Reason = "ZitadelOutputReference is not ATS-compatible.")]
    public static ZitadelOutputReference GetSecretOutput(this IResourceBuilder<ZitadelProjectResource> builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return ZitadelOutputsAnnotation.GetOrAdd(builder.Resource, name, isSecret: true);
    }

    /// <summary>
    /// Binds an output of the Zitadel resource to a parameter. The output takes the parameter value instead of a value set by the initialization.
    /// </summary>
    /// <param name="builder">The Zitadel resource builder.</param>
    /// <param name="name">The name of the output.</param>
    /// <param name="parameter">The parameter providing the value.</param>
    /// <returns>The Zitadel resource builder for method chaining.</returns>
    [AspireExport("withZitadelOutput", MethodName = "withOutput")]
    public static IResourceBuilder<ZitadelResource> WithOutput(this IResourceBuilder<ZitadelResource> builder, string name, IResourceBuilder<ParameterResource> parameter)
    {
        ArgumentNullException.ThrowIfNull(builder);
        BindOutput(builder.Resource, name, parameter);
        return builder;
    }

    /// <summary>
    /// Binds an output of the Zitadel project to a parameter, e.g. to use the credentials of an existing application.
    /// The output takes the parameter value instead of a value set by the initialization.
    /// </summary>
    /// <param name="builder">The Zitadel project resource builder.</param>
    /// <param name="name">The name of the output.</param>
    /// <param name="parameter">The parameter providing the value.</param>
    /// <returns>The Zitadel project resource builder for method chaining.</returns>
    [AspireExport("withZitadelProjectOutput", MethodName = "withOutput")]
    public static IResourceBuilder<ZitadelProjectResource> WithOutput(this IResourceBuilder<ZitadelProjectResource> builder, string name, IResourceBuilder<ParameterResource> parameter)
    {
        ArgumentNullException.ThrowIfNull(builder);
        BindOutput(builder.Resource, name, parameter);
        return builder;
    }

    private static void BindOutput(IResource resource, string name, IResourceBuilder<ParameterResource> parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ZitadelOutputReference output = ZitadelOutputsAnnotation.GetOrAdd(resource, name, parameter.Resource.Secret);
        output.Parameter = parameter.Resource;
    }

    /// <summary>
    /// Adds a Zitadel project resource for organizing applications and configurations within the Zitadel instance.
    /// Projects in Zitadel are containers for applications, roles, and other identity management resources.
    /// </summary>
    /// <param name="builder">The Zitadel resource builder to add the project to.</param>
    /// <param name="name">The resource name for service discovery and configuration.</param>
    /// <param name="projectName">The display name of the project within Zitadel.</param>
    /// <returns>A resource builder for the Zitadel project resource that can be used for further configuration.</returns>
    [AspireExport]
    public static IResourceBuilder<ZitadelProjectResource> AddProject(this IResourceBuilder<ZitadelResource> builder, [ResourceName] string name, string projectName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddProject(name, new AddProjectRequest { Name = projectName });
    }

    /// <summary>
    /// Adds a Zitadel project resource using a detailed project creation request.
    /// This overload allows for more advanced project configuration options through the AddProjectRequest parameter.
    /// </summary>
    /// <param name="builder">The Zitadel resource builder to add the project to.</param>
    /// <param name="name">The resource name for service discovery and configuration.</param>
    /// <param name="projectRequest">A detailed project creation request containing all project configuration options.</param>
    /// <returns>A resource builder for the Zitadel project resource that can be used for further configuration.</returns>
    [AspireExportIgnore(Reason = "AddProjectRequest is a Zitadel gRPC type that is not ATS-compatible; use the projectName overload.")]
    public static IResourceBuilder<ZitadelProjectResource> AddProject(this IResourceBuilder<ZitadelResource> builder, [ResourceName] string name, AddProjectRequest projectRequest)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(projectRequest);
        ArgumentNullException.ThrowIfNull(projectRequest.Name);

        ZitadelProjectResource zitadelProject = new(name, projectRequest, builder.Resource);
        builder.Resource.AddProject(name, projectRequest.Name);

        return builder.ApplicationBuilder.AddResource(zitadelProject)
            .WithInitialState(new CustomResourceSnapshot
            {
                Properties = [], ResourceType = "ZitadelProject", State = new ResourceStateSnapshot(KnownResourceStates.NotStarted, KnownResourceStateStyles.Info)
            })
            .ExcludeFromManifest();
    }

    /// <summary>
    /// Sets the organization name for the Zitadel instance.
    /// The organization is the top-level container for all identity and access management resources in Zitadel.
    /// </summary>
    /// <param name="builder">The Zitadel resource builder to configure with the organization name.</param>
    /// <param name="organizationName">The name of the organization to create or use in the Zitadel instance.</param>
    /// <returns>The Zitadel resource builder for method chaining.</returns>
    [AspireExport]
    public static IResourceBuilder<ZitadelResource> WithOrganizationName(this IResourceBuilder<ZitadelResource> builder, string organizationName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(organizationName);
        builder.Resource.OrganizationName = organizationName;
        return builder.WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_NAME", organizationName);
    }

    /// <summary>
    /// Configures the Zitadel resource to export telemetry data using OpenTelemetry. Zitadel only supports Endpoints on port 443.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="endpoint"></param>
    /// <param name="exporterType"></param>
    /// <returns></returns>
    [AspireExport]
    public static IResourceBuilder<ZitadelResource> WithOpenTelemetry(this IResourceBuilder<ZitadelResource> builder, EndpointReference endpoint, string exporterType = "grpc")
    {
        return builder.WithEnvironment((ctx) =>
        {
            ctx.EnvironmentVariables["ZITADEL_INSTRUMENTATION_SERVICENAME"] = builder.Resource.Name;
            ctx.EnvironmentVariables["ZITADEL_INSTRUMENTATION_LOG_LEVEL"] = "INFO";
            ctx.EnvironmentVariables["ZITADEL_INSTRUMENTATION_LOG_EXPORTER_TYPE"] = exporterType;
            ctx.EnvironmentVariables["ZITADEL_INSTRUMENTATION_LOG_EXPORTER_ENDPOINT"] = endpoint.Property(EndpointProperty.Host); // no port, zitadel has it hardcoded to 443
            ctx.EnvironmentVariables["ZITADEL_INSTRUMENTATION_TRACE_EXPORTER_TYPE"] = exporterType;
            ctx.EnvironmentVariables["ZITADEL_INSTRUMENTATION_TRACE_EXPORTER_ENDPOINT"] = endpoint.Property(EndpointProperty.Host); // no port, zitadel has it hardcoded to 443
            ctx.EnvironmentVariables["ZITADEL_INSTRUMENTATION_TRACE_TRUSTREMOTESPANS"] = "true";
            ctx.EnvironmentVariables["ZITADEL_INSTRUMENTATION_METRIC_EXPORTER_TYPE"] = exporterType;
            ctx.EnvironmentVariables["ZITADEL_INSTRUMENTATION_METRIC_EXPORTER_ENDPOINT"] = endpoint.Property(EndpointProperty.Host);
        });
    }

    /// <summary>
    /// Adds a connection string resource named <c>{zitadel}-{name}</c> holding <see cref="ZitadelResource.ApiConnectionString"/>
    /// (<c>Endpoint=...;ServiceAccountKey=...</c>) to call the Zitadel API with the machine user, e.g. via <c>AddZitadelApi</c> from <c>Zemires.Aspire.Zitadel</c>.
    /// </summary>
    /// <remarks>
    /// Consumers referencing it should wait for the Zitadel resource, as the machine user key is written on its first start.
    /// </remarks>
    /// <param name="builder">The Zitadel resource builder.</param>
    /// <param name="name">The suffix of the connection string resource name, appended to the Zitadel resource name. Defaults to <c>api</c>.</param>
    /// <returns>A resource builder for the connection string resource.</returns>
    [AspireExport]
    public static IResourceBuilder<ConnectionStringResource> AddApiConnectionString(this IResourceBuilder<ZitadelResource> builder, [ResourceName] string name = "api")
    {
        var connectionString = builder.ApplicationBuilder.AddConnectionString($"{builder.Resource.Name}-{name}", builder.Resource.ApiConnectionString);
        connectionString.WithParentRelationship(builder);
        return connectionString;
    }

    private static async Task CreateZitadelProject(ZitadelInitializationContext context, ResourceNotificationService resourceNotificationService)
    {
        ZitadelProjectResource zitadelProject = (ZitadelProjectResource)context.Resource;

        await resourceNotificationService.PublishUpdateAsync(zitadelProject,
            state => state with { State = new ResourceStateSnapshot(KnownResourceStates.Starting, KnownResourceStateStyles.Info) });

        ManagementService.ManagementServiceClient managementService = Clients.ManagementService(context.Options);

        ListProjectsResponse? projects = await managementService.ListProjectsAsync(new ListProjectsRequest());

        Project? project = projects.Result.FirstOrDefault(p => p.Name == zitadelProject.ProjectRequest.Name);

        string projectId;
        if (project == null)
        {
            AddProjectResponse? projectAddResponse = await managementService.AddProjectAsync(zitadelProject.ProjectRequest);
            projectId = projectAddResponse.Id;
        }
        else
        {
            projectId = project.Id;
        }

        zitadelProject.ProjectId = projectId;

        await RunInitializationAsync(context);

        List<ResourcePropertySnapshot> properties = [new("zitadel.projectId", projectId)];
        foreach (ZitadelOutputReference output in ZitadelOutputsAnnotation.GetAll(zitadelProject))
        {
            if (output.Parameter is null && output.TryGetResolvedValue(out string? value))
            {
                properties.Add(new ResourcePropertySnapshot($"zitadel.output.{output.Name}", value) { IsSensitive = output.IsSecret });
            }
        }

        await resourceNotificationService.PublishUpdateAsync(zitadelProject,
            state => state with
            {
                State = new ResourceStateSnapshot(KnownResourceStates.Running, KnownResourceStateStyles.Success),
                Properties = [..state.Properties.Where(p => !p.Name.StartsWith("zitadel.", StringComparison.Ordinal)), ..properties]
            });
    }

    /// <summary>
    /// Runs the initialization callbacks of the resource and resolves its outputs. Outputs not set by a callback fall back to
    /// the value persisted from a previous start; outputs still unresolved fail.
    /// </summary>
    private static async Task RunInitializationAsync(ZitadelInitializationContext context)
    {
        if (context.Resource.TryGetAnnotationsOfType(out IEnumerable<ZitadelInitializationAnnotation>? initAnnotations))
        {
            foreach (ZitadelInitializationAnnotation annotation in initAnnotations)
            {
                await annotation.Initialization.Invoke(context);
            }
        }

        List<string> missing = [];
        foreach (ZitadelOutputReference output in ZitadelOutputsAnnotation.GetAll(context.Resource))
        {
            if (output.Parameter is not null || output.IsResolved)
            {
                continue;
            }

            string? persisted = context.GetPersistedValue(context.Resource, output.Name);
            if (persisted is not null)
            {
                output.Set(persisted);
            }
            else
            {
                missing.Add(output.Name);
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Output(s) '{string.Join("', '", missing)}' of '{context.Resource.Name}' were not set by its initialization and are not available from configuration.");
        }
    }

    private static void ResolveOutputsFromConfiguration(IResource resource, IServiceProvider services)
    {
        IConfiguration configuration = services.GetRequiredService<IConfiguration>();
        foreach (ZitadelOutputReference output in ZitadelOutputsAnnotation.GetAll(resource))
        {
            if (output.Parameter is not null || output.IsResolved)
            {
                continue;
            }

            string? value = configuration[output.ConfigurationKey];
            if (string.IsNullOrEmpty(value))
            {
                output.Fail(new InvalidOperationException($"Output '{output.Name}' of '{resource.Name}' has no initialization and is not available from configuration."));
            }
            else
            {
                output.Set(value);
            }
        }
    }

    private static void FailUnresolvedOutputs(IResource resource, Exception exception)
    {
        foreach (ZitadelOutputReference output in ZitadelOutputsAnnotation.GetAll(resource))
        {
            if (output.Parameter is null && !output.IsResolved)
            {
                output.Fail(new InvalidOperationException($"Output '{output.Name}' of '{resource.Name}' is not available, initialization failed.", exception));
            }
        }
    }

    private static string GetLocalKeysPath(string name, bool ensureExists = true)
    {
        var path = Path.GetFullPath($"./.zitadel/{name}-keys");
        if (ensureExists && !Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return path;
    }
}