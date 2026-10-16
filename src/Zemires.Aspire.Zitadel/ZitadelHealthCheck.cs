using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Zemires.Aspire.Zitadel;

internal sealed class ZitadelHealthCheck(HttpClient httpClient, Uri endpoint) : IHealthCheck
{
    private readonly Uri _healthUri = new(endpoint, "/healthz");

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync(_healthUri, cancellationToken).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(context.Registration.FailureStatus, $"Zitadel returned status code {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: ex);
        }
    }
}
