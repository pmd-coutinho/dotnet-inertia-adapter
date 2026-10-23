using System.Diagnostics;
using InertiaNet.Core;
using InertiaNet.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InertiaNet.Diagnostics;

/// <summary>A single prop's resolution kind, outcome, and elapsed time. Values are never captured.</summary>
/// <param name="Path">Full prop path.</param>
/// <param name="Kind">Value, callback, provider, or wrapper classification.</param>
/// <param name="Outcome">Resolved, excluded, deferred, optional, cached, rescued, failed, or cancelled.</param>
/// <param name="Duration">Elapsed resolution time, including nested props.</param>
/// <param name="IsSlow">Whether successful resolution exceeded the configured threshold.</param>
public sealed record InertiaPropDiagnostic(string Path, string Kind, string Outcome, TimeSpan Duration, bool IsSlow)
{
    /// <summary>The registered endpoint name, when available.</summary>
    public string? RouteName { get; init; }
    /// <summary>The deferred group, when this measurement represents a deferred prop.</summary>
    public string? DeferredGroup { get; init; }
    /// <summary>The once-prop cache key, including an explicit custom key.</summary>
    public string? OnceKey { get; init; }
}

/// <summary>Completed render diagnostics. Bytes count application output before response compression.</summary>
/// <param name="RouteName">Registered endpoint name, when available.</param>
/// <param name="Component">Rendered component.</param>
/// <param name="Format">JSON or HTML output.</param>
/// <param name="BytesWritten">Bytes successfully written during rendering.</param>
/// <param name="Duration">Serialization/SSR/view rendering duration, excluding prop resolution.</param>
/// <param name="Succeeded">Whether rendering completed successfully.</param>
public sealed record InertiaRenderDiagnostic(string? RouteName, string Component, string Format, long BytesWritten, TimeSpan Duration, bool Succeeded);

/// <summary>Access to the current request's opt-in prop diagnostics.</summary>
public static class PropDiagnosticsExtensions
{
    /// <summary>Returns a snapshot of completed measurements; empty when diagnostics are disabled.</summary>
    public static IReadOnlyList<InertiaPropDiagnostic> GetInertiaPropDiagnostics(this HttpContext context)
        => context.Items.TryGetValue(PropDiagnosticsTrace.ContextKey, out var trace) && trace is PropDiagnosticsTrace diagnostics
            ? diagnostics.Snapshot() : [];

    /// <summary>Returns completed render metrics, or null before rendering/when diagnostics are disabled.</summary>
    public static InertiaRenderDiagnostic? GetInertiaRenderDiagnostics(this HttpContext context)
        => context.Items.TryGetValue(PropDiagnosticsTrace.ContextKey, out var trace) && trace is PropDiagnosticsTrace diagnostics
            ? diagnostics.Render : null;
}

internal sealed class PropDiagnosticsTrace
{
    internal const string ContextKey = "InertiaNet.PropDiagnostics";
    private readonly PropDiagnosticsOptions _options;
    private readonly ILogger? _logger;
    private readonly List<InertiaPropDiagnostic?> _entries = [];
    private readonly string? _routeName;
    public InertiaRenderDiagnostic? Render { get; private set; }

    private PropDiagnosticsTrace(PropDiagnosticsOptions options, ILogger? logger, string? routeName) { _options = options; _logger = logger; _routeName = routeName; }

    public static PropDiagnosticsTrace? Create(HttpContext context, IServiceProvider services, PropDiagnosticsOptions? options)
    {
        if (options?.Enabled != true) return null;
        var environment = services.GetService(typeof(IHostEnvironment)) as IHostEnvironment;
        if (options.DevelopmentOnly && environment?.IsDevelopment() != true) return null;
        var route = context.GetEndpoint()?.Metadata;
        var trace = new PropDiagnosticsTrace(options, services.GetService(typeof(ILogger<PropDiagnosticsTrace>)) as ILogger,
            route?.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? route?.GetMetadata<IRouteNameMetadata>()?.RouteName);
        context.Items[ContextKey] = trace;
        return trace;
    }

    public IReadOnlyList<InertiaPropDiagnostic> Snapshot() => _entries.OfType<InertiaPropDiagnostic>().ToArray();

    public async Task MeasureRenderAsync(HttpContext context, string component, string format, Func<Task> write)
    {
        var original = context.Response.Body;
        using var counting = new OutputCountingStream(original);
        var started = Stopwatch.GetTimestamp();
        var succeeded = false;
        context.Response.Body = counting;
        try { await write(); succeeded = true; }
        finally
        {
            context.Response.Body = original;
            Render = new(_routeName, component, format, counting.BytesWritten, Stopwatch.GetElapsedTime(started), succeeded);
            _logger?.LogDebug("Inertia render '{Component}' [{Format}] wrote {OutputBytes} bytes in {DurationMs:F3} ms on route '{RouteName}' (success: {Succeeded}).",
                component, format, Render.BytesWritten, Render.Duration.TotalMilliseconds, _routeName, succeeded);
        }
    }

    public Measurement? Begin(string path, string kind, object? prop = null)
    {
        if (_entries.Count >= _options.MaxEntries) return null;
        var slot = _entries.Count;
        _entries.Add(null); // Reserve parents before nested measurements fill the limit.
        var measurement = new Measurement(this, slot, path, kind);
        measurement.Metadata(prop);
        return measurement;
    }

    internal sealed class Measurement(PropDiagnosticsTrace trace, int slot, string path, string kind) : IDisposable
    {
        private readonly long _started = Stopwatch.GetTimestamp();
        private string _kind = kind;
        private string _outcome = "failed";
        private string? _deferredGroup, _onceKey;
        public void Kind(string value) => _kind = value;
        public void Metadata(object? prop)
        {
            _deferredGroup = prop is IDeferrable deferred && deferred.ShouldDefer() ? deferred.Group() : null;
            _onceKey = prop is IOnceable once && once.ShouldResolveOnce() ? once.GetKey() ?? path : null;
        }
        public void Complete(string outcome) => _outcome = outcome;
        public void Dispose()
        {
            var duration = Stopwatch.GetElapsedTime(_started);
            var slow = _outcome == "resolved" && duration >= trace._options.SlowPropThreshold;
            trace._entries[slot] = new(path, _kind, _outcome, duration, slow)
                { RouteName = trace._routeName, DeferredGroup = _deferredGroup, OnceKey = _onceKey };
            trace._logger?.Log(slow ? LogLevel.Warning : LogLevel.Debug,
                "Inertia prop '{PropPath}' [{PropKind}] {Outcome} in {DurationMs:F3} ms (route: {RouteName}, group: {DeferredGroup}, once key: {OnceKey}).",
                path, _kind, _outcome, duration.TotalMilliseconds, trace._routeName, _deferredGroup, _onceKey);
        }
    }
}
