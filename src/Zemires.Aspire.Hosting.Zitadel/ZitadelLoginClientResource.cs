namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// The Zitadel login v2 UI container resource.
/// </summary>
/// <param name="name">The resource name.</param>
/// <param name="accessToken">The service user token used to call the Zitadel API.</param>
public class ZitadelLoginClientResource(string name, ParameterResource? accessToken)
    : ContainerResource(name), IResourceWithServiceDiscovery
{
    internal const string BasePath = "/ui/v2/login";
    internal const string PrimaryEndpointName = "http";

    private EndpointReference? _primaryEndpoint;

    /// <summary>
    /// Gets the HTTP endpoint.
    /// </summary>
    public EndpointReference PrimaryEndpoint => _primaryEndpoint ??= new EndpointReference(this, PrimaryEndpointName);

    /// <summary>
    /// Gets the login UI base URL <c>{endpoint}/ui/v2/login</c>.
    /// </summary>
    public ReferenceExpression BaseEndpoint => ReferenceExpression.Create($"{PrimaryEndpoint}{BasePath}");

    /// <summary>
    /// Gets the OIDC login URL.
    /// </summary>
    public ReferenceExpression OidcLoginEndpoint => ReferenceExpression.Create($"{BaseEndpoint}/login?authRequest=");

    /// <summary>
    /// Gets the OIDC logout URL.
    /// </summary>
    public ReferenceExpression OidcLogoutEndpoint => ReferenceExpression.Create($"{BaseEndpoint}/logout?post_logout_redirect=");

    /// <summary>
    /// Gets the SAML login URL.
    /// </summary>
    public ReferenceExpression SamlLoginEndpoint => ReferenceExpression.Create($"{BaseEndpoint}/login?samlRequest=");

    /// <summary>
    /// Gets the service user token used to call the Zitadel API.
    /// </summary>
    public ParameterResource? ServiceAccessToken { get; } = accessToken;
}
