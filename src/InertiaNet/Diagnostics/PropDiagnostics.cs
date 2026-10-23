using System.Diagnostics;
using InertiaNet.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InertiaNet.Diagnostics;

/// <summary>A single prop's resolution kind, outcome, and elapsed time. Values are never captured.</summary>
/// <param name="Path">Full prop path.</param>
/// <param name="Kind">Value, callback, provider, or wrapper classification.</param>
/// <param name="Outcome">Resolved, excluded, deferred, optional, cached, rescued, failed, or cancelled.</param>
/// <param name="Duration">Elapsed resolution time, including nested props.</param>
/// <param name="IsSlow">Whether successful resolution exceeded the configured threshold.</param>
public sealed record InertiaPropDiagnostic(string Path, string Kind, string Outcome, TimeSpan Duration, bool IsSlow);

/// <summary>Access to the current request's opt-in prop diagnostics.</summary>
public static class PropDiagnosticsExtensions
{
    /// <summary>Returns a snapshot of completed measurements; empty when diagnostics are disabled.</summary>
    public static IReadOnlyList<InertiaPropDiagnostic> GetInertiaPropDiagnostics(this HttpContext context)
        => context.Items.TryGetValue(PropDiagnosticsTrace.ContextKey, out var trace) && trace is PropDiagnosticsTrace diagnostics
            ? diagnostics.Snapshot() : [];
}

internal sealed class PropDiagnosticsTrace
{
    internal const string ContextKey = "InertiaNet.PropDiagnostics";
    private readonly PropDiagnosticsOptions _options;
    private readonly ILogger? _logger;
    private readonly List<InertiaPropDiagnostic?> _entries = [];

    private PropDiagnosticsTrace(PropDiagnosticsOptions options, ILogger? logger) { _options = options; _logger = logger; }

    public static PropDiagnosticsTrace? Create(HttpContext context, IServiceProvider services, PropDiagnosticsOptions? options)
    {
        if (options?.Enabled != true) return null;
        var environment = services.GetService(typeof(IHostEnvironment)) as IHostEnvironment;
        if (options.DevelopmentOnly && environment?.IsDevelopment() != true) return null;
        var trace = new PropDiagnosticsTrace(options, services.GetService(typeof(ILogger<PropDiagnosticsTrace>)) as ILogger);
        context.Items[ContextKey] = trace;
        return trace;
    }

    public IReadOnlyList<InertiaPropDiagnostic> Snapshot() => _entries.OfType<InertiaPropDiagnostic>().ToArray();

    public Measurement? Begin(string path, string kind)
    {
        if (_entries.Count >= _options.MaxEntries) return null;
        var slot = _entries.Count;
        _entries.Add(null); // Reserve parents before nested measurements fill the limit.
        return new(this, slot, path, kind);
    }

    internal sealed class Measurement(PropDiagnosticsTrace trace, int slot, string path, string kind) : IDisposable
    {
        private readonly long _started = Stopwatch.GetTimestamp();
        private string _kind = kind;
        private string _outcome = "failed";
        public void Kind(string value) => _kind = value;
        public void Complete(string outcome) => _outcome = outcome;
        public void Dispose()
        {
            var duration = Stopwatch.GetElapsedTime(_started);
            var slow = _outcome == "resolved" && duration >= trace._options.SlowPropThreshold;
            trace._entries[slot] = new(path, _kind, _outcome, duration, slow);
            trace._logger?.Log(slow ? LogLevel.Warning : LogLevel.Debug,
                "Inertia prop '{PropPath}' [{PropKind}] {Outcome} in {DurationMs:F3} ms.", path, _kind, _outcome, duration.TotalMilliseconds);
        }
    }
}
