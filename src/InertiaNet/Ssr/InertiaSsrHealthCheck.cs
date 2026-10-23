using InertiaNet.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace InertiaNet.Ssr;

internal sealed class InertiaSsrHealthCheck(IHttpClientFactory clients, IOptions<InertiaOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var settings = options.Value.Ssr;
        if (!settings.Enabled) return HealthCheckResult.Healthy("Inertia SSR is disabled.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (settings.Timeout != Timeout.InfiniteTimeSpan) timeout.CancelAfter(settings.Timeout);
            using var client = clients.CreateClient(InertiaSsr.HttpClientName);
            // The dedicated Node server exposes /health; Vite's render bridge does not.
            using var response = await client.GetAsync(settings.Url.TrimEnd('/') + "/health", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return response.IsSuccessStatusCode ? HealthCheckResult.Healthy("Inertia SSR is available.")
                : new(context.Registration.FailureStatus, $"Inertia SSR returned HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return new(context.Registration.FailureStatus, "Inertia SSR is unavailable.", ex);
        }
    }
}
