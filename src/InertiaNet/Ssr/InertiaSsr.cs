using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace InertiaNet.Ssr;

/// <summary>Public names for configuring SSR HTTP requests and subscribing to SSR telemetry.</summary>
public static class InertiaSsr
{
    /// <summary>The named HttpClient used for rendering and health probes.</summary>
    public const string HttpClientName = "InertiaNet.SSR";

    /// <summary>The ActivitySource name for SSR render spans.</summary>
    public const string ActivitySourceName = "InertiaNet.SSR";

    /// <summary>The Meter name for SSR render counts and duration.</summary>
    public const string MeterName = "InertiaNet.SSR";

    internal static readonly ActivitySource Activities = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);
    internal static readonly Counter<long> Renders = Meter.CreateCounter<long>("inertianet.ssr.renders", "{render}");
    internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("inertianet.ssr.duration", "s");

    internal static string ErrorName(SsrErrorType type) => type switch
    {
        SsrErrorType.BrowserApi => "browser-api",
        SsrErrorType.ComponentResolution => "component-resolution",
        SsrErrorType.Render => "render",
        SsrErrorType.Connection => "connection",
        SsrErrorType.Timeout => "timeout",
        _ => "unknown",
    };
}
