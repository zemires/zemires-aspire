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
/// Extension methods for adding Zitadel resources to the application model.
/// </summary>
public static class ZitadelBuilderExtensions
{
    private const int DefaultContainerPort = 8080;
    private const int DefaultContainerPortLoginClient = 3000;
    private const string KeysPath = "/keys";
    private const string LoginClientPatExpirationDate = "2099-12-31T00:00:00Z";

    /// <summary>
    /// Adds a Zitadel container resource (<c>start-from-init</c>). Login v2 is disabled unless <see cref="AddZitadelLoginClient"/> is used.
    /// </summary>
    /// <ats-summary>Adds a Zitadel container resource</ats-summary>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/>.</param>
    /// <param name="name">The resource name, also used as connection string name.</param>
    /// <param name="port">The host port. Random when <see langword="null"/>.</param>
    /// <param name="username">The admin user name. <c>admin</c> when <see langword="null"/>.</param>
    /// <param name="password">The admin password. Generated when <see langword="null"/>.</param>
    /// <param name="masterKey">The 32 character master key. Generated when <see langword="null"/>.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<ZitadelResource> AddZitadel(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        int? port = null,
        IResourceBuilder<ParameterResource>? username = null,
        IResourceBuilder<ParameterResource>? password = null,
        IResourceBuilder<ParameterResource>? masterKey = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var passwordParameter = password?.Resource ?? ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, $"{name}-password", minSpecial: 1);
        var masterKeyParameter = masterKey?.Resource ?? ParameterResourceBuilderExtensions.CreateGeneratedParameter(builder, $"{name}-masterKey", true, new GenerateParameterDefault
        {
            MinLength = 32, // Zitadel requires 32 characters
            Lower = true,
            Upper = true,
            Numeric = true,
            Special = true,
            MinLower = 1,
            MinUpper = 1,
            MinNumeric = 1,
            MinSpecial = 1
        });

        var zitadel = new ZitadelResource(name, username?.Resource, passwordParameter, masterKeyParameter);

        var zitadelBuilder = builder.AddResource(zitadel)
            .WithImage(ZitadelContainerImageTags.Image)
            .WithImageTag(ZitadelContainerImageTags.Tag)
            .WithImageRegistry(ZitadelContainerImageTags.Registry)
            .WithArgs("start-from-init", "--masterkeyFromEnv")
            .WithHttpEndpoint(targetPort: DefaultContainerPort, port: port, name: ZitadelResource.PrimaryEndpointName)
            .WithUrlForEndpoint(ZitadelResource.PrimaryEndpointName, e => e.DisplayText = "Zitadel Dashboard")
            .WithHttpHealthCheck("/healthz")
            .WithEnvironment("ZITADEL_MASTERKEY", masterKeyParameter)
            .WithEnvironment(ctx =>
            {
                if (zitadel.TryGetEndpoints(out var endpoints) && endpoints.SingleOrDefault(e => e.Name == ZitadelResource.PrimaryEndpointName) is { } endpoint)
                {
                    ctx.EnvironmentVariables.TryAdd("ZITADEL_EXTERNALDOMAIN", endpoint.TargetHost);
                    if (endpoint.Port is not null)
                    {
                        ctx.EnvironmentVariables.TryAdd("ZITADEL_EXTERNALPORT", endpoint.Port);
                    }
                }
            })
            // Login v2 needs the separate login container, see AddZitadelLoginClient
            .WithEnvironment("ZITADEL_DEFAULTINSTANCE_FEATURES_LOGINV2_REQUIRED", "false")
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_HUMAN_USERNAME", zitadel.AdminReference)
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_HUMAN_PASSWORD", passwordParameter)
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_HUMAN_PASSWORDCHANGEREQUIRED", "false");

#pragma warning disable ASPIRECERTIFICATES001
        zitadelBuilder
            .WithHttpsCertificateConfiguration(ctx =>
            {
                ctx.EnvironmentVariables["ZITADEL_EXTERNALSECURE"] = "true";
                ctx.EnvironmentVariables["ZITADEL_TLS_ENABLED"] = "true";
                ctx.EnvironmentVariables["ZITADEL_TLS_CERTPATH"] = ctx.CertificatePath;
                ctx.EnvironmentVariables["ZITADEL_TLS_KEYPATH"] = ctx.KeyPath;
                return Task.CompletedTask;
            })
            // Zitadel binds a single port, so switch it to https
            .SubscribeHttpsEndpointsUpdate(_ => zitadelBuilder.WithEndpoint(ZitadelResource.PrimaryEndpointName, ep => ep.UriScheme = "https"));
#pragma warning restore ASPIRECERTIFICATES001

        builder.Eventing.Subscribe<ResourceReadyEvent>(zitadel, (@event, ct) => InitializeAsync(zitadel, @event.Services, ct));

        return zitadelBuilder;
    }

    /// <summary>
    /// Stores Zitadel data in a PostgreSQL database. Reads <c>Host</c>, <c>Port</c>, <c>DatabaseName</c>, <c>Username</c> and
    /// <c>Password</c> from the connection properties of <paramref name="database"/> (admin user) and creates the user <c>zitadel-user</c>.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="database">The PostgreSQL database, e.g. a Postgres container or Azure Postgres database.</param>
    /// <param name="userPassword">The password of <c>zitadel-user</c>. Generated when <see langword="null"/>.</param>
    /// <param name="sslModeEnabled">Whether to connect with SSL.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<ZitadelResource> WithDatabase(this IResourceBuilder<ZitadelResource> builder,
        IResourceBuilder<IResourceWithConnectionString> database, IResourceBuilder<ParameterResource>? userPassword = null, bool sslModeEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(database);

        var db = database.Resource;
        var userPasswordParameter = userPassword?.Resource
            ?? ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder.ApplicationBuilder, $"{db.Name}-user-password");
        var sslMode = sslModeEnabled ? "enable" : "disable";

        return builder
            .WithEnvironment("ZITADEL_DATABASE_POSTGRES_HOST", db.GetConnectionProperty("Host"))
            .WithEnvironment("ZITADEL_DATABASE_POSTGRES_PORT", db.GetConnectionProperty("Port"))
            .WithEnvironment("ZITADEL_DATABASE_POSTGRES_DATABASE", db.GetConnectionProperty("DatabaseName"))
            .WithEnvironment("ZITADEL_DATABASE_POSTGRES_USER_USERNAME", "zitadel-user")
            .WithEnvironment("ZITADEL_DATABASE_POSTGRES_USER_PASSWORD", userPasswordParameter)
            .WithEnvironment("ZITADEL_DATABASE_POSTGRES_USER_SSL_MODE", sslMode)
            .WithEnvironment("ZITADEL_DATABASE_POSTGRES_ADMIN_USERNAME", db.GetConnectionProperty("Username"))
            .WithEnvironment("ZITADEL_DATABASE_POSTGRES_ADMIN_PASSWORD", db.GetConnectionProperty("Password"))
            .WithEnvironment("ZITADEL_DATABASE_POSTGRES_ADMIN_SSL_MODE", sslMode)
            .WithReferenceRelationship(database)
            .WaitFor(database);
    }

    /// <summary>
    /// Uses an existing machine user key (base64 encoded JSON) for API access, e.g. for initialization.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="machineUserKey">The base64 encoded JSON key of the machine user.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport("withMachineUserKey", MethodName = "withMachineUserKey")]
    public static IResourceBuilder<ZitadelResource> WithMachineUser(this IResourceBuilder<ZitadelResource> builder, IResourceBuilder<ParameterResource> machineUserKey)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(machineUserKey);

        builder.Resource.MachineUserKeyParameter = machineUserKey.Resource;
        return builder;
    }

    /// <summary>
    /// Creates an <c>IAM_OWNER</c> machine user on first start and writes its key to <c>.zitadel/{name}-keys/{machineUserName}.json</c>
    /// in the AppHost directory. Initialization and <see cref="AddApiConnectionString"/> use this key.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="machineUserKeyParameter">An existing machine user key (base64 encoded JSON); then no user is created.</param>
    /// <param name="machineUserName">The machine user name.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<ZitadelResource> WithMachineUser(this IResourceBuilder<ZitadelResource> builder, ParameterResource? machineUserKeyParameter = null, string machineUserName = "admin")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(machineUserName);

        if (machineUserKeyParameter is not null)
        {
            builder.Resource.MachineUserKeyParameter = machineUserKeyParameter;
            return builder;
        }

        string path = builder.WithKeysMount();
        builder.Resource.MachineUserKeyPath = Path.Combine(path, $"{machineUserName}.json");

        return builder
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_MACHINEKEYPATH", $"{KeysPath}/{machineUserName}.json")
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_MACHINE_MACHINE_USERNAME", machineUserName)
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_MACHINE_MACHINE_NAME", "Automatically Initialized IAM_OWNER")
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_MACHINE_MACHINEKEY_TYPE", "1");
    }

    /// <summary>
    /// Serves HTTPS with the given certificate and key files (paths relative to the AppHost directory).
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="certificatePath">The certificate file.</param>
    /// <param name="keyPath">The private key file.</param>
    /// <param name="certificateDestinationPath">The container folder of the files.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<ZitadelResource> WithCertificate(this IResourceBuilder<ZitadelResource> builder, string certificatePath, string keyPath, string certificateDestinationPath = "/certificate")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(certificatePath);
        ArgumentException.ThrowIfNullOrEmpty(keyPath);

        string appHostDirectory = builder.ApplicationBuilder.AppHostDirectory;
        string certFullPath = Path.GetFullPath(certificatePath, appHostDirectory);
        string keyFullPath = Path.GetFullPath(keyPath, appHostDirectory);
        string certFileName = Path.GetFileName(certFullPath);
        string keyFileName = Path.GetFileName(keyFullPath);

        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            builder.WithContainerFiles(certificateDestinationPath, [
                new ContainerFile { Name = certFileName, Contents = File.ReadAllText(certFullPath) },
                new ContainerFile { Name = keyFileName, Contents = File.ReadAllText(keyFullPath) }
            ]);
        }
        else
        {
            builder.WithBindMount(Path.GetDirectoryName(certFullPath)!, certificateDestinationPath);
        }

        return builder.WithEnvironment("ZITADEL_TLS_CERTPATH", $"{certificateDestinationPath}/{certFileName}")
            .WithEnvironment("ZITADEL_TLS_KEYPATH", $"{certificateDestinationPath}/{keyFileName}");
    }

    /// <summary>
    /// Sets the external domain (<c>ZITADEL_EXTERNALDOMAIN</c>), replacing the endpoint host.
    /// </summary>
    /// <ats-summary>Configures the external domain for the Zitadel resource</ats-summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="externalDomain">The external domain, e.g. <c>auth.example.com</c>.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport("withExternalDomainDomain")]
    public static IResourceBuilder<ZitadelResource> WithExternalDomain(
        this IResourceBuilder<ZitadelResource> builder,
        string externalDomain)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalDomain);

        return builder.WithEnvironment("ZITADEL_EXTERNALDOMAIN", externalDomain);
    }

    /// <summary>
    /// Sets the external domain, port and scheme from an endpoint, e.g. of a reverse proxy.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="endpoint">The endpoint clients use to reach Zitadel.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport("withExternalDomainEndpoint")]
    public static IResourceBuilder<ZitadelResource> WithExternalDomain(this IResourceBuilder<ZitadelResource> builder, EndpointReference endpoint)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(endpoint);

        return builder
            // the scheme may switch to https at startup, so read it late
            .WithEnvironment(ctx => ctx.EnvironmentVariables["ZITADEL_EXTERNALSECURE"] = endpoint.Scheme == "https" ? "true" : "false")
            .WithEnvironment("ZITADEL_EXTERNALPORT", endpoint.Property(EndpointProperty.Port))
            .WithEnvironment("ZITADEL_EXTERNALDOMAIN", endpoint.Property(EndpointProperty.Host));
    }

    /// <summary>
    /// Sets the external domain, port and scheme.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="isSecure">Whether clients use HTTPS.</param>
    /// <param name="host">The external host name.</param>
    /// <param name="port">The external port.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport("withExternalDomainHost")]
    public static IResourceBuilder<ZitadelResource> WithExternalDomain(this IResourceBuilder<ZitadelResource> builder, bool isSecure, string host, int port)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(host);

        return builder.WithEnvironment("ZITADEL_EXTERNALSECURE", isSecure ? "true" : "false")
            .WithEnvironment("ZITADEL_EXTERNALPORT", port.ToString())
            .WithEnvironment("ZITADEL_EXTERNALDOMAIN", host);
    }

    /// <summary>
    /// Creates an <c>IAM_LOGIN_CLIENT</c> machine user on first start and writes its personal access token to
    /// <c>.zitadel/{name}-keys/{loginUser}.pat</c>. Required by <see cref="AddZitadelLoginClient"/>.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="loginUser">The login client user name.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<ZitadelResource> WithLoginClientKey(this IResourceBuilder<ZitadelResource> builder, string loginUser = "login-client")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(loginUser);

        builder.WithKeysMount();

        return builder
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_LOGINCLIENTPATPATH", $"{KeysPath}/{loginUser}.pat")
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_LOGINCLIENT_MACHINE_USERNAME", loginUser)
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_LOGINCLIENT_MACHINE_NAME", "Automatically Initialized IAM_LOGIN_CLIENT")
            .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_LOGINCLIENT_PAT_EXPIRATIONDATE", LoginClientPatExpirationDate);
    }

    /// <summary>
    /// Adds the Zitadel login v2 UI container and enables login v2 on the Zitadel resource.
    /// </summary>
    /// <param name="builder">The Zitadel resource builder.</param>
    /// <param name="name">The resource name.</param>
    /// <param name="port">The host port. Random when <see langword="null"/>.</param>
    /// <param name="loginUser">The login client user name of <see cref="WithLoginClientKey"/>.</param>
    /// <param name="serviceAccessToken">The service user token. Read from the PAT file of <see cref="WithLoginClientKey"/> when <see langword="null"/>.</param>
    /// <returns>The login client resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<ZitadelLoginClientResource> AddZitadelLoginClient(this IResourceBuilder<ZitadelResource> builder, string name, int? port = null,
        string loginUser = "login-client", IResourceBuilder<ParameterResource>? serviceAccessToken = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        string patFile = Path.Combine(GetLocalKeysPath(builder), $"{loginUser}.pat");
        ParameterResource serviceAccessTokenParameter = serviceAccessToken?.Resource
            ?? new ParameterResource($"{name}-service-user-token", _ => File.ReadAllText(patFile), secret: true);

        ZitadelLoginClientResource loginClient = new(name, serviceAccessTokenParameter);
        bool isRunMode = builder.ApplicationBuilder.ExecutionContext.IsRunMode;

        IResourceBuilder<ZitadelLoginClientResource> loginBuilder = builder.ApplicationBuilder.AddResource(loginClient)
            .WithImage(ZitadelContainerImageTags.LoginImage)
            .WithImageTag(ZitadelContainerImageTags.LoginTag)
            .WithImageRegistry(ZitadelContainerImageTags.Registry)
            .WithHttpEndpoint(port: port, targetPort: DefaultContainerPortLoginClient, name: ZitadelLoginClientResource.PrimaryEndpointName)
            .WithHttpHealthCheck($"{ZitadelLoginClientResource.BasePath}/healthy", 200)
            .WithEnvironment("ZITADEL_API_URL", builder.Resource.PrimaryEndpoint)
            .WithEnvironment("NEXT_PUBLIC_BASE_PATH", ZitadelLoginClientResource.BasePath)
            .WithEnvironment("ZITADEL_SERVICE_USER_TOKEN", serviceAccessTokenParameter)
            .WaitFor(builder);

        if (isRunMode)
        {
            // local development: trust the developer certificate and address Zitadel with its external domain
            loginBuilder.WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
                .WithEnvironment("CUSTOM_REQUEST_HEADERS", "Host:localhost");
        }

        builder.WithEnvironment("ZITADEL_DEFAULTINSTANCE_FEATURES_LOGINV2_REQUIRED", "true")
            .WithEnvironment(async context =>
            {
                var env = context.EnvironmentVariables;
                if (isRunMode)
                {
                    // resolve the host address, as the browser is redirected to these URLs
                    env["ZITADEL_DEFAULTINSTANCE_FEATURES_LOGINV2_BASEURI"] = await loginClient.BaseEndpoint.GetValueAsync(context.CancellationToken).ConfigureAwait(false) ?? "";
                    env["ZITADEL_OIDC_DEFAULTLOGINURLV2"] = await loginClient.OidcLoginEndpoint.GetValueAsync(context.CancellationToken).ConfigureAwait(false) ?? "";
                    env["ZITADEL_OIDC_DEFAULTLOGOUTURLV2"] = await loginClient.OidcLogoutEndpoint.GetValueAsync(context.CancellationToken).ConfigureAwait(false) ?? "";
                    env["ZITADEL_SAML_DEFAULTLOGINURLV2"] = await loginClient.SamlLoginEndpoint.GetValueAsync(context.CancellationToken).ConfigureAwait(false) ?? "";
                }
                else
                {
                    env["ZITADEL_DEFAULTINSTANCE_FEATURES_LOGINV2_BASEURI"] = loginClient.BaseEndpoint;
                    env["ZITADEL_OIDC_DEFAULTLOGINURLV2"] = loginClient.OidcLoginEndpoint;
                    env["ZITADEL_OIDC_DEFAULTLOGOUTURLV2"] = loginClient.OidcLogoutEndpoint;
                    env["ZITADEL_SAML_DEFAULTLOGINURLV2"] = loginClient.SamlLoginEndpoint;
                }
            });

        return loginBuilder;
    }

    /// <summary>
    /// Sets the host port of the Zitadel resource.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="port">The host port. Random when <see langword="null"/>.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport("withZitadelHostPort", MethodName = "withHostPort")]
    public static IResourceBuilder<ZitadelResource> WithHostPort(this IResourceBuilder<ZitadelResource> builder, int? port)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithEndpoint(ZitadelResource.PrimaryEndpointName, endpoint => endpoint.Port = port);
    }

    /// <summary>
    /// Sets the host port of the login client resource.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="port">The host port. Random when <see langword="null"/>.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport("withZitadelLoginClientHostPort", MethodName = "withHostPort")]
    public static IResourceBuilder<ZitadelLoginClientResource> WithHostPort(this IResourceBuilder<ZitadelLoginClientResource> builder, int? port)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithEndpoint(ZitadelLoginClientResource.PrimaryEndpointName, endpoint => endpoint.Port = port);
    }

    /// <summary>
    /// Runs <paramref name="initialization"/> when Zitadel is ready, authenticated as the machine user.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="initialization">Receives the API client options and the resource.</param>
    /// <returns>The resource builder.</returns>
    [AspireExportIgnore(Reason = "The initialization callback receives Zitadel API client options, which are not ATS-compatible.")]
    public static IResourceBuilder<ZitadelResource> WithInitialization(this IResourceBuilder<ZitadelResource> builder,
        Func<Clients.Options, IResource, Task> initialization)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithAnnotation(new ZitadelInitializationAnnotation(initialization));
    }

    /// <summary>
    /// Runs <paramref name="initialization"/> when the project has been created, authenticated as the machine user.
    /// </summary>
    /// <param name="builder">The project resource builder.</param>
    /// <param name="initialization">Receives the API client options and the project resource.</param>
    /// <returns>The project resource builder.</returns>
    [AspireExportIgnore(Reason = "The initialization callback receives Zitadel API client options, which are not ATS-compatible.")]
    public static IResourceBuilder<ZitadelProjectResource> WithInitialization(this IResourceBuilder<ZitadelProjectResource> builder,
        Func<Clients.Options, IResource, Task> initialization)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithAnnotation(new ZitadelInitializationAnnotation(initialization));
    }

    /// <summary>
    /// Runs <paramref name="initialization"/> when Zitadel is ready. The context provides the API client options and sets outputs
    /// (see <see cref="GetOutput(IResourceBuilder{ZitadelResource}, string)"/>).
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="initialization">Receives the initialization context.</param>
    /// <returns>The resource builder.</returns>
    [AspireExportIgnore(Reason = "The initialization callback receives Zitadel API client options, which are not ATS-compatible.")]
    public static IResourceBuilder<ZitadelResource> WithInitialization(this IResourceBuilder<ZitadelResource> builder,
        Func<ZitadelInitializationContext, Task> initialization)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithAnnotation(new ZitadelInitializationAnnotation(initialization));
    }

    /// <summary>
    /// Runs <paramref name="initialization"/> when the project has been created. The context provides the API client options and sets outputs
    /// (see <see cref="GetOutput(IResourceBuilder{ZitadelProjectResource}, string)"/>).
    /// </summary>
    /// <param name="builder">The project resource builder.</param>
    /// <param name="initialization">Receives the initialization context.</param>
    /// <returns>The project resource builder.</returns>
    [AspireExportIgnore(Reason = "The initialization callback receives Zitadel API client options, which are not ATS-compatible.")]
    public static IResourceBuilder<ZitadelProjectResource> WithInitialization(this IResourceBuilder<ZitadelProjectResource> builder,
        Func<ZitadelInitializationContext, Task> initialization)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithAnnotation(new ZitadelInitializationAnnotation(initialization));
    }

    /// <summary>
    /// Gets an output set by an initialization with <see cref="ZitadelInitializationContext.SetOutput"/>, resolved lazily when consumed.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">The output name.</param>
    /// <returns>A reference to the output value.</returns>
    [AspireExportIgnore(Reason = "ZitadelOutputReference is not ATS-compatible.")]
    public static ZitadelOutputReference GetOutput(this IResourceBuilder<ZitadelResource> builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return ZitadelOutputsAnnotation.GetOrAdd(builder.Resource, name, isSecret: false);
    }

    /// <summary>
    /// Gets a secret output set by an initialization with <see cref="ZitadelInitializationContext.SetOutput"/>.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">The output name.</param>
    /// <returns>A reference to the output value.</returns>
    [AspireExportIgnore(Reason = "ZitadelOutputReference is not ATS-compatible.")]
    public static ZitadelOutputReference GetSecretOutput(this IResourceBuilder<ZitadelResource> builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return ZitadelOutputsAnnotation.GetOrAdd(builder.Resource, name, isSecret: true);
    }

    /// <summary>
    /// Gets an output of the project set by an initialization with <see cref="ZitadelInitializationContext.SetOutput"/>
    /// (e.g. a client id), resolved lazily when consumed.
    /// </summary>
    /// <param name="builder">The project resource builder.</param>
    /// <param name="name">The output name.</param>
    /// <returns>A reference to the output value.</returns>
    [AspireExportIgnore(Reason = "ZitadelOutputReference is not ATS-compatible.")]
    public static ZitadelOutputReference GetOutput(this IResourceBuilder<ZitadelProjectResource> builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return ZitadelOutputsAnnotation.GetOrAdd(builder.Resource, name, isSecret: false);
    }

    /// <summary>
    /// Gets a secret output of the project set by an initialization with <see cref="ZitadelInitializationContext.SetOutput"/> (e.g. a client secret).
    /// </summary>
    /// <param name="builder">The project resource builder.</param>
    /// <param name="name">The output name.</param>
    /// <returns>A reference to the output value.</returns>
    [AspireExportIgnore(Reason = "ZitadelOutputReference is not ATS-compatible.")]
    public static ZitadelOutputReference GetSecretOutput(this IResourceBuilder<ZitadelProjectResource> builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return ZitadelOutputsAnnotation.GetOrAdd(builder.Resource, name, isSecret: true);
    }

    /// <summary>
    /// Binds an output to a parameter; the initialization does not set it then.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">The output name.</param>
    /// <param name="parameter">The parameter providing the value.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport("withZitadelOutput", MethodName = "withOutput")]
    public static IResourceBuilder<ZitadelResource> WithOutput(this IResourceBuilder<ZitadelResource> builder, string name, IResourceBuilder<ParameterResource> parameter)
    {
        ArgumentNullException.ThrowIfNull(builder);
        BindOutput(builder.Resource, name, parameter);
        return builder;
    }

    /// <summary>
    /// Binds a project output to a parameter, e.g. for an existing application; the initialization does not set it then.
    /// </summary>
    /// <param name="builder">The project resource builder.</param>
    /// <param name="name">The output name.</param>
    /// <param name="parameter">The parameter providing the value.</param>
    /// <returns>The project resource builder.</returns>
    [AspireExport("withZitadelProjectOutput", MethodName = "withOutput")]
    public static IResourceBuilder<ZitadelProjectResource> WithOutput(this IResourceBuilder<ZitadelProjectResource> builder, string name, IResourceBuilder<ParameterResource> parameter)
    {
        ArgumentNullException.ThrowIfNull(builder);
        BindOutput(builder.Resource, name, parameter);
        return builder;
    }

    /// <summary>
    /// Adds a Zitadel project, created (or reused by name) when Zitadel is ready.
    /// </summary>
    /// <param name="builder">The Zitadel resource builder.</param>
    /// <param name="name">The resource name.</param>
    /// <param name="projectName">The project name in Zitadel.</param>
    /// <returns>The project resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<ZitadelProjectResource> AddProject(this IResourceBuilder<ZitadelResource> builder, [ResourceName] string name, string projectName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(projectName);
        return builder.AddProject(name, new AddProjectRequest { Name = projectName });
    }

    /// <summary>
    /// Adds a Zitadel project from a full creation request, created (or reused by name) when Zitadel is ready.
    /// </summary>
    /// <param name="builder">The Zitadel resource builder.</param>
    /// <param name="name">The resource name.</param>
    /// <param name="projectRequest">The project creation request.</param>
    /// <returns>The project resource builder.</returns>
    [AspireExportIgnore(Reason = "AddProjectRequest is a Zitadel gRPC type that is not ATS-compatible; use the projectName overload.")]
    public static IResourceBuilder<ZitadelProjectResource> AddProject(this IResourceBuilder<ZitadelResource> builder, [ResourceName] string name, AddProjectRequest projectRequest)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(projectRequest);
        ArgumentException.ThrowIfNullOrEmpty(projectRequest.Name);

        ZitadelProjectResource project = new(name, projectRequest, builder.Resource);
        builder.Resource.AddProject(name, projectRequest.Name);

        return builder.ApplicationBuilder.AddResource(project)
            .WithInitialState(new CustomResourceSnapshot
            {
                Properties = [],
                ResourceType = "ZitadelProject",
                State = new ResourceStateSnapshot(KnownResourceStates.NotStarted, KnownResourceStateStyles.Info)
            })
            .WithParentRelationship(builder)
            .ExcludeFromManifest();
    }

    /// <summary>
    /// Sets the name of the first organization (<c>ZITADEL_FIRSTINSTANCE_ORG_NAME</c>).
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="organizationName">The organization name.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<ZitadelResource> WithOrganizationName(this IResourceBuilder<ZitadelResource> builder, string organizationName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(organizationName);

        builder.Resource.OrganizationName = organizationName;
        return builder.WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_NAME", organizationName);
    }

    /// <summary>
    /// Exports logs, traces and metrics to an OpenTelemetry collector. Zitadel always connects to port 443 of the endpoint host.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="endpoint">The collector endpoint; only its host is used.</param>
    /// <param name="exporterType">The exporter type, e.g. <c>grpc</c> or <c>http</c>.</param>
    /// <returns>The resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<ZitadelResource> WithOtlpExporter(this IResourceBuilder<ZitadelResource> builder, EndpointReference endpoint, string exporterType = "grpc")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrEmpty(exporterType);

        var host = endpoint.Property(EndpointProperty.Host);

        return builder
            .WithEnvironment("ZITADEL_INSTRUMENTATION_SERVICENAME", builder.Resource.Name)
            .WithEnvironment("ZITADEL_INSTRUMENTATION_LOG_LEVEL", "INFO")
            .WithEnvironment("ZITADEL_INSTRUMENTATION_LOG_EXPORTER_TYPE", exporterType)
            .WithEnvironment("ZITADEL_INSTRUMENTATION_LOG_EXPORTER_ENDPOINT", host)
            .WithEnvironment("ZITADEL_INSTRUMENTATION_TRACE_EXPORTER_TYPE", exporterType)
            .WithEnvironment("ZITADEL_INSTRUMENTATION_TRACE_EXPORTER_ENDPOINT", host)
            .WithEnvironment("ZITADEL_INSTRUMENTATION_TRACE_TRUSTREMOTESPANS", "true")
            .WithEnvironment("ZITADEL_INSTRUMENTATION_METRIC_EXPORTER_TYPE", exporterType)
            .WithEnvironment("ZITADEL_INSTRUMENTATION_METRIC_EXPORTER_ENDPOINT", host);
    }

    /// <summary>
    /// Adds a connection string resource holding <see cref="ZitadelResource.ApiConnectionString"/> (<c>Endpoint=...;ServiceAccountKey=...</c>)
    /// for <c>AddZitadelApi</c> of <c>Zemires.Aspire.Zitadel</c>. Consumers should wait for Zitadel, which writes the key on first start.
    /// </summary>
    /// <param name="builder">The Zitadel resource builder.</param>
    /// <param name="name">The resource name. <c>{zitadel}-api</c> when <see langword="null"/>.</param>
    /// <returns>The connection string resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<ConnectionStringResource> AddApiConnectionString(this IResourceBuilder<ZitadelResource> builder, [ResourceName] string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.ApplicationBuilder.AddConnectionString(name ?? $"{builder.Resource.Name}-api", builder.Resource.ApiConnectionString)
            .WithParentRelationship(builder);
    }

    private static void BindOutput(IResource resource, string name, IResourceBuilder<ParameterResource> parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ZitadelOutputReference output = ZitadelOutputsAnnotation.GetOrAdd(resource, name, parameter.Resource.Secret);
        output.Parameter = parameter.Resource;
    }

    private static async Task InitializeAsync(ZitadelResource zitadel, IServiceProvider services, CancellationToken ct)
    {
        ResourceNotificationService notificationService = services.GetRequiredService<ResourceNotificationService>();
        ResourceLoggerService loggerService = services.GetRequiredService<ResourceLoggerService>();
        ILogger logger = loggerService.GetLogger(zitadel);

        List<ZitadelProjectResource> projects = services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ZitadelProjectResource>()
            .Where(p => p.Parent == zitadel)
            .ToList();

        if (!zitadel.HasAnnotationOfType<ZitadelInitializationAnnotation>() && projects.Count == 0)
        {
            ResolveOutputsFromConfiguration(zitadel, services);
            return;
        }

        Clients.Options? options = null;
        try
        {
            options = await zitadel.CreateApiClientOptions(ct).ConfigureAwait(false);
            await RunInitializationAsync(new ZitadelInitializationContext(options, zitadel, services, logger, ct)).ConfigureAwait(false);
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

                await CreateZitadelProject(new ZitadelInitializationContext(options, project, services, projectLogger, ct), notificationService).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                projectLogger.LogError(ex, "Initialization of '{Resource}' failed.", project.Name);
                FailUnresolvedOutputs(project, ex);
                await notificationService.PublishUpdateAsync(project,
                    state => state with { State = new ResourceStateSnapshot(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error) }).ConfigureAwait(false);
            }
        }
    }

    private static async Task CreateZitadelProject(ZitadelInitializationContext context, ResourceNotificationService resourceNotificationService)
    {
        ZitadelProjectResource zitadelProject = (ZitadelProjectResource)context.Resource;

        await resourceNotificationService.PublishUpdateAsync(zitadelProject,
            state => state with { State = new ResourceStateSnapshot(KnownResourceStates.Starting, KnownResourceStateStyles.Info) }).ConfigureAwait(false);

        ManagementService.ManagementServiceClient managementService = Clients.ManagementService(context.Options);

        ListProjectsResponse projects = await managementService.ListProjectsAsync(new ListProjectsRequest());
        Project? project = projects.Result.FirstOrDefault(p => p.Name == zitadelProject.ProjectRequest.Name);

        string projectId = project?.Id ?? (await managementService.AddProjectAsync(zitadelProject.ProjectRequest)).Id;
        zitadelProject.ProjectId = projectId;

        await RunInitializationAsync(context).ConfigureAwait(false);

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
                Properties = [.. state.Properties.Where(p => !p.Name.StartsWith("zitadel.", StringComparison.Ordinal)), .. properties]
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the initialization callbacks and resolves the outputs. Outputs not set fall back to the value persisted by a
    /// previous start; outputs still unresolved fail.
    /// </summary>
    private static async Task RunInitializationAsync(ZitadelInitializationContext context)
    {
        foreach (ZitadelInitializationAnnotation annotation in context.Resource.Annotations.OfType<ZitadelInitializationAnnotation>())
        {
            await annotation.Initialization(context).ConfigureAwait(false);
        }

        List<string> missing = [];
        foreach (ZitadelOutputReference output in ZitadelOutputsAnnotation.GetAll(context.Resource))
        {
            if (output.Parameter is not null || output.IsResolved)
            {
                continue;
            }

            if (context.GetPersistedValue(context.Resource, output.Name) is { } persisted)
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

    /// <summary>
    /// Bind mounts the local keys folder to <c>/keys</c> once and returns its path.
    /// </summary>
    private static string WithKeysMount(this IResourceBuilder<ZitadelResource> builder)
    {
        string path = GetLocalKeysPath(builder);
        if (!builder.Resource.Annotations.OfType<ContainerMountAnnotation>().Any(m => m.Target == KeysPath))
        {
            builder.WithBindMount(path, KeysPath);
        }

        return path;
    }

    /// <summary>
    /// Gets <c>.zitadel/{name}-keys</c> in the AppHost directory, created in run mode.
    /// </summary>
    private static string GetLocalKeysPath(IResourceBuilder<ZitadelResource> builder)
    {
        string path = Path.Combine(builder.ApplicationBuilder.AppHostDirectory, ".zitadel", $"{builder.Resource.Name}-keys");
        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            Directory.CreateDirectory(path);
        }

        return path;
    }
}
