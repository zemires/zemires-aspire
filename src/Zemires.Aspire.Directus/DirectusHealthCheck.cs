using Microsoft.Extensions.Diagnostics.HealthChecks;
using Qute.Directus;

namespace Zemires.Aspire.Directus;

internal sealed class DirectusHealthCheck(DirectusClient directusClient) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var health = await directusClient.Server.GetHealthAsync(cancellationToken).ConfigureAwait(false);

            return string.Equals(health.Status, "ok", StringComparison.OrdinalIgnoreCase)
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(context.Registration.FailureStatus, $"Directus status is '{health.Status}'.");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: ex);
        }
    }
}
