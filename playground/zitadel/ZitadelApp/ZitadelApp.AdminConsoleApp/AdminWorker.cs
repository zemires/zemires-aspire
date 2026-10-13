using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zitadel.Org.V2;
using Zitadel.User.V2;

namespace ZitadelApp.AdminConsoleApp;

/// <summary>
/// Lists the organizations and users of the Zitadel instance using the API clients registered by AddZitadelApi, then stops the application.
/// </summary>
internal sealed class AdminWorker(
    OrganizationService.OrganizationServiceClient organizations,
    UserService.UserServiceClient users,
    IHostApplicationLifetime lifetime,
    ILogger<AdminWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            ListOrganizationsResponse orgs = await organizations.ListOrganizationsAsync(new ListOrganizationsRequest(), cancellationToken: stoppingToken);
            foreach (Organization org in orgs.Result)
            {
                logger.LogInformation("Organization {OrganizationName} ({OrganizationId})", org.Name, org.Id);
            }

            ListUsersResponse userList = await users.ListUsersAsync(new ListUsersRequest(), cancellationToken: stoppingToken);
            foreach (User user in userList.Result)
            {
                logger.LogInformation("User {UserName} ({UserId}, {UserType})", user.Username, user.UserId, user.TypeCase);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Zitadel API call failed");
            Environment.ExitCode = 1;
        }
        finally
        {
            lifetime.StopApplication();
        }
    }
}
