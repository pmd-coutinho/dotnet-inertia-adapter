using InertiaNet.Interfaces;

namespace InertiaNet.Props;

/// <summary>
/// A prop that is excluded from the initial page load and fetched asynchronously
/// by the frontend after the first render. Equivalent to Laravel's
/// <c>Inertia::defer()</c>.
/// <para>
/// Can be combined with merge, deep-merge, once, and append/prepend behaviours.
/// </para>
/// </summary>
public sealed class DeferProp : IDeferrable, IIgnoreFirstLoad, IMergeable, IOnceable
{
    private readonly Func<IServiceProvider, CancellationToken, Task<object?>> _callback;

    // IDeferrable
    private readonly string _group;

    // IMergeable
    private bool _merge;
    private bool _deepMerge;
    private bool _appendAtRoot;
    private bool _prependAtRoot;
    private readonly List<string> _appendAtPaths = [];
    private readonly List<string> _prependAtPaths = [];
    private readonly List<string> _matchOn = [];

    // IOnceable
    private bool _once;
    private bool _refresh;
    private long? _expiresAt;
    private string? _key;

    public DeferProp(Func<IServiceProvider, CancellationToken, Task<object?>> callback, string? group = null)
    {
        _callback = callback;
        _group = group ?? "default";
    }

    public async Task<object?> ResolveAsync(IServiceProvider services, CancellationToken ct = default)
        => await _callback(services, ct);

    // --- IDeferrable ---
    public bool ShouldDefer() => true;
    public string Group() => _group;

    // --- IMergeable ---
    public bool ShouldMerge() => _merge;
    public bool ShouldDeepMerge() => _deepMerge;
    public IReadOnlyList<string> MatchesOn() => _matchOn;
    public bool AppendsAtRoot() => _appendAtRoot;
    public bool PrependsAtRoot() => _prependAtRoot;
    public IReadOnlyList<string> AppendsAtPaths() => _appendAtPaths;
    public IReadOnlyList<string> PrependsAtPaths() => _prependAtPaths;

    // --- IOnceable ---
    public bool ShouldResolveOnce() => _once;
    public bool ShouldBeRefreshed() => _refresh;
    public string? GetKey() => _key;
    public long? ExpiresAt() => _expiresAt;

    // --- Fluent builder ---

    /// <summary>Merge the resolved value with existing client-side data (append at root).</summary>
    public DeferProp Merge() { _merge = true; _appendAtRoot = true; return this; }

    /// <summary>Deep-merge the resolved value with existing client-side data.</summary>
    public DeferProp DeepMerge() { _merge = true; _deepMerge = true; _appendAtRoot = true; return this; }

    /// <summary>Append the resolved value at a specific nested path (disables root-level appending).</summary>
    public DeferProp Append(string path) { _merge = true; _appendAtPaths.Add(path); _appendAtRoot = false; return this; }

    /// <summary>Prepend the resolved value at a specific nested path (disables root-level prepending).</summary>
    public DeferProp Prepend(string path) { _merge = true; _prependAtPaths.Add(path); _prependAtRoot = false; return this; }

    /// <summary>Set a key (or keys) used for deduplication when merging.</summary>
    public DeferProp MatchOn(string key) { _matchOn.Add(key); return this; }
    public DeferProp MatchOn(IEnumerable<string> keys) { _matchOn.AddRange(keys); return this; }

    /// <summary>Mark this deferred prop as resolved-once.</summary>
    public DeferProp Once(bool once = true) { _once = once; return this; }
    public DeferProp Fresh(bool refresh = true) { _refresh = refresh; return this; }
    public DeferProp As(string key) { _key = key; return this; }
    public DeferProp Until(DateTimeOffset expiresAt) { _expiresAt = expiresAt.ToUnixTimeMilliseconds(); return this; }
    public DeferProp Until(TimeSpan ttl) { _expiresAt = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeMilliseconds(); return this; }
}
