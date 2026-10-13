using Microsoft.Extensions.Logging;
using Zitadel.Api;
using Zitadel.App.V1;
using Zitadel.Management.V1;
using Zitadel.Org.V2;
using Zitadel.Project.V1;
using Zitadel.V1;

namespace ZitadelApp.AppHost;

/// <summary>
/// Custom initialization of the Zitadel instance and project of the playground.
/// </summary>
public static class CustomZitadelInitialization
{
    public const string ClientIdOutput = "webfrontend-client-id";
    public const string ClientSecretOutput = "webfrontend-client-secret";

    private const string AppName = "webfrontend-oidc";

    public static async Task Initialize(ZitadelInitializationContext context)
    {
        OrganizationService.OrganizationServiceClient orgService = Clients.OrganizationService(context.Options);
        ListOrganizationsResponse? organizations = await orgService.ListOrganizationsAsync(new ListOrganizationsRequest());
        Organization? organization = organizations.Result[0];
        context.Logger.LogInformation("Organization: {Name} {Id}", organization.Name, organization.Id);
    }

    public static async Task InitializeProject(ZitadelInitializationContext context)
    {
        ZitadelProjectResource project = (ZitadelProjectResource)context.Resource;
        ManagementService.ManagementServiceClient managementService = Clients.ManagementService(context.Options);

        // add OIDC app if not exists
        ListAppsResponse? appsResult = await managementService.ListAppsAsync(new ListAppsRequest
        {
            ProjectId = project.ProjectId, Queries = { new AppQuery { NameQuery = new AppNameQuery { Name = AppName, Method = TextQueryMethod.Equals } } }
        });

        if (appsResult.Result.Count == 0)
        {
            AddOIDCAppRequest oidcAppRequest = new()
            {
                AppType = OIDCAppType.Web,
                Name = AppName,
                ProjectId = project.ProjectId,
                AccessTokenType = OIDCTokenType.Jwt,
                AuthMethodType = OIDCAuthMethodType.Basic
            };
            oidcAppRequest.RedirectUris.Add("https://localhost:8502/signin-zitadel");
            oidcAppRequest.PostLogoutRedirectUris.Add("https://localhost:8502/signout-callback-oidc");
            AddOIDCAppResponse? oidcApp = await managementService.AddOIDCAppAsync(oidcAppRequest);

            context.SetOutput(ClientIdOutput, oidcApp.ClientId);
            context.SetOutput(ClientSecretOutput, oidcApp.ClientSecret);
        }
        else
        {
            App app = appsResult.Result[0];

            // the secret is only returned on creation: reuse the one known from a previous start (user secrets or parameter)
            // if it belongs to this client, otherwise generate a new one
            string? knownClientId = await context.GetOutputValueAsync(ClientIdOutput);
            string? knownSecret = await context.GetOutputValueAsync(ClientSecretOutput);

            context.SetOutput(ClientIdOutput, app.OidcConfig.ClientId);

            if (knownSecret is null || knownClientId != app.OidcConfig.ClientId)
            {
                RegenerateOIDCClientSecretResponse? regenerated = await managementService.RegenerateOIDCClientSecretAsync(
                    new RegenerateOIDCClientSecretRequest { ProjectId = project.ProjectId, AppId = app.Id });
                context.SetOutput(ClientSecretOutput, regenerated.ClientSecret);
                context.Logger.LogInformation("Client secret of {App} regenerated", AppName);
            }
        }

        // add AppAdmin role if not exists
        ListProjectRolesResponse? rolesResponse = await managementService.ListProjectRolesAsync(new ListProjectRolesRequest
        {
            ProjectId = project.ProjectId, Queries = { new RoleQuery { KeyQuery = new RoleKeyQuery { Key = "AppAdmin", Method = TextQueryMethod.Equals } } }
        });

        if (rolesResponse.Result.Count == 0)
        {
            await managementService.AddProjectRoleAsync(new AddProjectRoleRequest { ProjectId = project.ProjectId, RoleKey = "AppAdmin", DisplayName = "Application Admin" });
        }
    }
}
