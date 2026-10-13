using Microsoft.Extensions.Diagnostics.HealthChecks;
using Qute.Directus;

namespace Zemires.Aspire.Directus;

internal class DirectusHealthCheck : IHealthCheck
{
    private DirectusClient _directusClient;

    public DirectusHealthCheck(DirectusClient directusClient)
    {
        _directusClient = directusClient;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var healthy = await _directusClient.Server.GetHealthAsync(cancellationToken);

            return healthy.Status == "Ok"
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(context.Registration.FailureStatus);
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: ex);
        }
    }
}
