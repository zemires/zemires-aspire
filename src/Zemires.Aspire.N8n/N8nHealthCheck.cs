using Microsoft.Extensions.Diagnostics.HealthChecks;
using Zemires.N8n.Api;

namespace Zemires.Aspire.N8n;

internal sealed class N8nHealthCheck(N8nClient n8nClient) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await n8nClient.IsHealthyAsync(cancellationToken).ConfigureAwait(false)
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(context.Registration.FailureStatus);
}
