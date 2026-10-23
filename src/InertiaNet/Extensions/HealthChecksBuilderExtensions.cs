using InertiaNet.Ssr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace InertiaNet.Extensions;

/// <summary>Optional SSR availability checks for ASP.NET health endpoints.</summary>
public static class HealthChecksBuilderExtensions
{
    /// <summary>
    /// Probes the configured Node SSR server's /health endpoint using the named SSR client
    /// and timeout. Disabled SSR is healthy. No application endpoint is mapped automatically.
    /// </summary>
    public static IHealthChecksBuilder AddInertiaSsr(this IHealthChecksBuilder builder,
        string name = "inertia-ssr", HealthStatus? failureStatus = null, IEnumerable<string>? tags = null,
        TimeSpan? timeout = null)
    {
        builder.Services.AddHttpClient(InertiaSsr.HttpClientName);
        builder.Services.AddOptions<Core.InertiaOptions>();
        return builder.AddCheck<InertiaSsrHealthCheck>(name, failureStatus, tags, timeout);
    }
}
