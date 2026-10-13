using Microsoft.Extensions.Diagnostics.HealthChecks;
using Zemires.N8n.Api;

namespace Zemires.Aspire.N8n;

internal class N8nHealthCheck : IHealthCheck
{
    private N8nClient _n8nClient;

    public N8nHealthCheck(N8nClient n8nClient)
    {
        _n8nClient = n8nClient;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var isHealthy = await _n8nClient.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

            return isHealthy
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(context.Registration.FailureStatus);
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: ex);
        }
    }
}
