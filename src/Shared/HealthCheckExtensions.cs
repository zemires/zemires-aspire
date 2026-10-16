using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Zemires.Aspire;

// Shared source, linked into the client integrations.
internal static class HealthCheckExtensions
{
    /// <summary>
    /// Adds <paramref name="registration"/> unless a health check with the same name was already added to <paramref name="builder"/>.
    /// </summary>
    public static void TryAddHealthCheck(this IHostApplicationBuilder builder, HealthCheckRegistration registration)
    {
        var key = $"Aspire.HealthChecks.{registration.Name}";
        if (!builder.Properties.ContainsKey(key))
        {
            builder.Properties[key] = true;
            builder.Services.AddHealthChecks().Add(registration);
        }
    }

    /// <summary>
    /// Converts a timeout in milliseconds to a <see cref="TimeSpan"/>, <see langword="null"/> when not positive.
    /// </summary>
    public static TimeSpan? ToTimeout(this int? milliseconds) =>
        milliseconds > 0 ? TimeSpan.FromMilliseconds(milliseconds.Value) : null;
}
