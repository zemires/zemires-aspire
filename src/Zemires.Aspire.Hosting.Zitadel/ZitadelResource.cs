using Zemires.Aspire.Hosting.Zitadel;

// ReSharper disable once CheckNamespace
namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A Zitadel container resource. Its connection string is the endpoint URL <c>{scheme}://{host}:{port}</c>.
/// </summary>
/// <param name="name">The resource name.</param>
/// <param name="admin">The admin user name; <c>admin</c> when <see langword="null"/>.</param>
/// <param name="adminPassword">The admin password.</param>
/// <param name="masterKey">The master key.</param>
public class ZitadelResource(string name, ParameterResource? admin, ParameterResource adminPassword, ParameterResource masterKey)
    : ContainerResource(name), IResourceWithServiceDiscovery, IResourceWithConnectionString
{
    internal const string PrimaryEndpointName = "http";
    private const string DefaultAdmin = "admin";

    private readonly Dictionary<string, string> _projects = new(StringComparer.InvariantCultureIgnoreCase);

    private EndpointReference? _primaryEndpoint;

    /// <summary>
    /// Gets the HTTP endpoint (UI and API).
    /// </summary>
    public EndpointReference PrimaryEndpoint => _primaryEndpoint ??= new EndpointReference(this, PrimaryEndpointName);

    /// <summary>
    /// Gets the host of <see cref="PrimaryEndpoint"/>.
    /// </summary>
    public EndpointReferenceExpression Host => PrimaryEndpoint.Property(EndpointProperty.Host);

    /// <summary>
    /// Gets the port of <see cref="PrimaryEndpoint"/>.
    /// </summary>
    public EndpointReferenceExpression Port => PrimaryEndpoint.Property(EndpointProperty.Port);

    /// <summary>
    /// Gets the endpoint URL <c>{scheme}://{host}:{port}</c>.
    /// </summary>
    public ReferenceExpression UriExpression => ReferenceExpression.Create($"{PrimaryEndpoint.Property(EndpointProperty.Scheme)}://{Host}:{Port}");

    /// <summary>
    /// Gets the connection string, equal to <see cref="UriExpression"/>.
    /// </summary>
    public ReferenceExpression ConnectionStringExpression => UriExpression;

    /// <summary>
    /// Gets the API connection string <c>Endpoint={url};ServiceAccountKey={base64 machine user key}</c> for the machine user of
    /// <c>WithMachineUser</c>; only the endpoint without machine user. Pass it with <c>AddApiConnectionString()</c>.
    /// </summary>
    public ReferenceExpression ApiConnectionString
    {
        get
        {
            if (MachineUserKeyParameter is not null)
            {
                return ReferenceExpression.Create($"Endpoint={ConnectionStringExpression};ServiceAccountKey={MachineUserKeyParameter}");
            }

            if (!string.IsNullOrEmpty(MachineUserKeyPath))
            {
                return ReferenceExpression.Create($"Endpoint={ConnectionStringExpression};ServiceAccountKey={new ZitadelMachineKeyFileReference(this)}");
            }

            return ReferenceExpression.Create($"Endpoint={ConnectionStringExpression}");
        }
    }

    /// <summary>
    /// Gets the admin user name parameter, or <see langword="null"/> for the default <c>admin</c>.
    /// </summary>
    public ParameterResource? AdminUsernameParameter { get; } = admin;

    /// <summary>
    /// Gets the admin user name.
    /// </summary>
    internal ReferenceExpression AdminReference =>
        AdminUsernameParameter is not null
            ? ReferenceExpression.Create($"{AdminUsernameParameter}")
            : ReferenceExpression.Create($"{DefaultAdmin}");

    /// <summary>
    /// Gets the admin password parameter.
    /// </summary>
    public ParameterResource AdminPasswordParameter { get; } = adminPassword ?? throw new ArgumentNullException(nameof(adminPassword));

    /// <summary>
    /// Gets the master key parameter.
    /// </summary>
    public ParameterResource MasterKeyParameter { get; } = masterKey ?? throw new ArgumentNullException(nameof(masterKey));

    /// <summary>
    /// Gets or sets the local path of the machine user key file written by Zitadel.
    /// </summary>
    public string? MachineUserKeyPath { get; set; }

    /// <summary>
    /// Gets the machine user key (base64 encoded JSON) passed to <c>WithMachineUser</c>.
    /// </summary>
    public ParameterResource? MachineUserKeyParameter { get; internal set; }

    /// <summary>
    /// Gets the projects added with <c>AddProject</c> (resource name to project name).
    /// </summary>
    public IReadOnlyDictionary<string, string> Projects => _projects;

    /// <summary>
    /// Gets or sets the name of the first organization.
    /// </summary>
    public string OrganizationName { get; set; } = "ZITADEL";

    internal void AddProject(string name, string projectName) => _projects.TryAdd(name, projectName);

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties()
    {
        yield return new("Host", ReferenceExpression.Create($"{Host}"));
        yield return new("Port", ReferenceExpression.Create($"{Port}"));
        yield return new("Uri", UriExpression);
    }
}
