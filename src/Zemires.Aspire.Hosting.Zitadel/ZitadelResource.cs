using Zemires.Aspire.Hosting.Zitadel;

// ReSharper disable once CheckNamespace
namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// Zitadel identity and access management container resource for Aspire applications.
/// </summary>
/// <param name="name">The unique name of the Zitadel resource.</param>
/// <param name="admin">Optional admin username parameter resource.</param>
/// <param name="adminPassword">Admin password parameter resource.</param>
/// <param name="masterKey">Master encryption key parameter resource.</param>
public class ZitadelResource(string name, ParameterResource? admin, ParameterResource adminPassword, ParameterResource masterKey)
    : ContainerResource(name), IResourceWithServiceDiscovery, IResourceWithConnectionString
{
    internal const string PrimaryEndpointName = "http";
    private const string DefaultAdmin = "root";

    private readonly Dictionary<string, string> _projects = new(StringComparer.InvariantCultureIgnoreCase);

    private EndpointReference? _primaryEndpoint;

    /// <summary>
    /// Gets the primary endpoint for the Zitadel instance.
    /// </summary>
    public EndpointReference PrimaryEndpoint => _primaryEndpoint ??= new EndpointReference(this, PrimaryEndpointName);

    /// <summary>
    /// Gets the connection string expression for the Zitadel instance in the format <c>{scheme}://{host}:{port}</c>.
    /// </summary>
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"{PrimaryEndpoint.Property(EndpointProperty.Scheme)}://{PrimaryEndpoint.Property(EndpointProperty.Host)}:{PrimaryEndpoint.Property(EndpointProperty.Port)}");

    /// <summary>
    /// Gets the connection string expression for API access with the machine user configured by <c>WithMachineUser</c>,
    /// in the format <c>Endpoint={scheme}://{host}:{port};ServiceAccountKey={base64 encoded machine user JSON key}</c>.
    /// Without a machine user only the endpoint is included.
    /// </summary>
    /// <remarks>
    /// Pass it to consumers with <c>zitadel.AddApiConnectionString()</c>
    /// and <c>WithReference</c>, and let them wait for the Zitadel resource, as the key file is written on its first start.
    /// </remarks>
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
    /// Gets the admin username parameter resource.
    /// </summary>
    public ParameterResource? AdminUsernameParameter { get; } = admin;

    /// <summary>
    /// Gets a reference expression for the admin username.
    /// </summary>
    internal ReferenceExpression AdminReference =>
        AdminUsernameParameter is not null
            ? ReferenceExpression.Create($"{AdminUsernameParameter!}")
            : ReferenceExpression.Create($"{DefaultAdmin}");

    /// <summary>
    /// Gets the admin password parameter resource.
    /// </summary>
    public ParameterResource AdminPasswordParameter { get; } = adminPassword ?? throw new ArgumentNullException(nameof(adminPassword));

    /// <summary>
    /// Gets the master encryption key parameter resource.
    /// </summary>
    public ParameterResource MasterKeyParameter { get; } = masterKey ?? throw new ArgumentNullException(nameof(masterKey));

    /// <summary>
    /// Gets or sets the file system path for machine user key files.
    /// </summary>
    public string? MachineUserKeyPath { get; set; }

    /// <summary>
    /// Gets the machine user key parameter resource (base64 encoded JSON key), when provided to <c>WithMachineUser</c>.
    /// </summary>
    public ParameterResource? MachineUserKeyParameter { get; internal set; }

    /// <summary>
    /// Gets the projects associated with this Zitadel instance.
    /// </summary>
    public IReadOnlyDictionary<string, string> Projects => _projects;

    /// <summary>
    /// Gets or sets the organization name for the Zitadel instance.
    /// </summary>
    public string OrganizationName { get; set; } = "ZITADEL";

    internal void AddProject(string name, string projectName)
    {
        _projects.TryAdd(name, projectName);
    }
}