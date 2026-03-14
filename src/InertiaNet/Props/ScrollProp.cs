using InertiaNet.Interfaces;
using Microsoft.AspNetCore.Http;

namespace InertiaNet.Props;

/// <summary>
/// A prop designed for infinite scroll / paginated data. Automatically merges
/// its resolved value with the client's existing list (append or prepend based
/// on scroll direction). Carries pagination metadata for the frontend.
/// Equivalent to Laravel's <c>Inertia::scroll()</c>.
/// </summary>
public sealed class ScrollProp : IDeferrable, IMergeable
{
    private readonly object? _value;
    private readonly Func<IServiceProvider, CancellationToken, Task<object?>>? _callback;
    private readonly string _wrapper;
    private readonly IProvidesScrollMetadata? _metadataProvider;
    private readonly Func<IServiceProvider, CancellationToken, Task<IProvidesScrollMetadata?>>? _metadataCallback;

    // IDeferrable
    private bool _deferred;
    private readonly string _group;

    // IMergeable — scroll always merges
    private bool _deepMerge;
    private bool _appendAtRoot;   // true = append (scroll down), false = prepend (scroll up)
    private bool _prependAtRoot;
    private readonly List<string> _appendAtPaths = [];
    private readonly List<string> _prependAtPaths = [];
    private readonly List<string> _matchOn = [];

    public ScrollProp(
        object? value,
        string wrapper = "data",
        IProvidesScrollMetadata? metadata = null,
        string? group = null)
    {
        _value = value;
        _wrapper = wrapper;
        _metadataProvider = metadata;
        _group = group ?? "default";
        // Root-level merge is never used for ScrollProp; ConfigureMergeIntent targets the wrapper path.
        _appendAtRoot = false;
    }

    public ScrollProp(
        Func<IServiceProvider, CancellationToken, Task<object?>> callback,
        string wrapper = "data",
        Func<IServiceProvider, CancellationToken, Task<IProvidesScrollMetadata?>>? metadataCallback = null,
        string? group = null)
    {
        _callback = callback;
        _wrapper = wrapper;
        _metadataCallback = metadataCallback;
        _group = group ?? "default";
        _appendAtRoot = false;
    }

    public async Task<object?> ResolveAsync(IServiceProvider services, CancellationToken ct = default)
    {
        if (_callback is not null)
            return await _callback(services, ct);
        return _value;
    }

    public async Task<IProvidesScrollMetadata?> ResolveMetadataAsync(IServiceProvider services, CancellationToken ct = default)
    {
        if (_metadataCallback is not null)
            return await _metadataCallback(services, ct);
        return _metadataProvider;
    }

    /// <summary>
    /// Reads the X-Inertia-Infinite-Scroll-Merge-Intent header to determine
    /// whether to append (scroll down) or prepend (scroll up).
    /// Targets the wrapper path (e.g. "data") exclusively — does not merge at root.
    /// </summary>
    public void ConfigureMergeIntent(HttpContext context)
    {
        var intent = context.Request.Headers[Support.HeaderNames.InfiniteScrollMergeIntent].ToString();
        if (intent == "prepend")
        {
            _prependAtRoot = false;
            _prependAtPaths.Add(_wrapper);
            _appendAtRoot = false;
        }
        else
        {
            _appendAtRoot = false;
            _appendAtPaths.Add(_wrapper);
        }
    }

    /// <summary>The wrapper key path within the resolved data (e.g. "data" for paginator results).</summary>
    public string Wrapper => _wrapper;

    // --- IDeferrable ---
    public bool ShouldDefer() => _deferred;
    public string Group() => _group;

    // --- IMergeable ---
    public bool ShouldMerge() => true;
    public bool ShouldDeepMerge() => _deepMerge;
    public IReadOnlyList<string> MatchesOn() => _matchOn;
    public bool AppendsAtRoot() => _appendAtRoot;
    public bool PrependsAtRoot() => _prependAtRoot;
    public IReadOnlyList<string> AppendsAtPaths() => _appendAtPaths;
    public IReadOnlyList<string> PrependsAtPaths() => _prependAtPaths;

    // --- Fluent builder ---

    /// <summary>Defer this scroll prop (load asynchronously after initial render).</summary>
    public ScrollProp Defer(string? group = null) { _deferred = true; return this; }

    /// <summary>Enable deep merging.</summary>
    public ScrollProp DeepMerge() { _deepMerge = true; return this; }

    /// <summary>Set deduplication key(s).</summary>
    public ScrollProp MatchOn(string key) { _matchOn.Add(key); return this; }
    public ScrollProp MatchOn(IEnumerable<string> keys) { _matchOn.AddRange(keys); return this; }
}
