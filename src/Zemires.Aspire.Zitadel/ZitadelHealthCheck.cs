using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Zemires.Aspire.Zitadel;

internal sealed class ZitadelHealthCheck : IHealthCheck, IDisposable
{
    private readonly HttpClient _httpClient;

    public ZitadelHealthCheck(Uri endpoint)
    {
        _httpClient = new HttpClient { BaseAddress = endpoint };
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("/healthz", cancellationToken).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(context.Registration.FailureStatus, $"Zitadel returned status code {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: ex);
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
