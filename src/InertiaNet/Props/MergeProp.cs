using InertiaNet.Interfaces;

namespace InertiaNet.Props;

/// <summary>
/// A prop whose resolved value is merged with existing client-side data
/// rather than replacing it. Equivalent to Laravel's <c>Inertia::merge()</c>
/// and <c>Inertia::deepMerge()</c>.
/// </summary>
public sealed class MergeProp : IMergeable, IOnceable
{
    private readonly object? _value;
    private readonly Func<IServiceProvider, CancellationToken, Task<object?>>? _callback;

    // IMergeable
    private bool _merge = true;
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

    public MergeProp(object? value, bool deepMerge = false)
    {
        _value = value;
        _deepMerge = deepMerge;
        _appendAtRoot = true; // default: append at root
    }

    public MergeProp(Func<IServiceProvider, CancellationToken, Task<object?>> callback, bool deepMerge = false)
    {
        _callback = callback;
        _deepMerge = deepMerge;
        _appendAtRoot = true;
    }

    public async Task<object?> ResolveAsync(IServiceProvider services, CancellationToken ct = default)
    {
        if (_callback is not null)
            return await _callback(services, ct);
        return _value;
    }

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

    /// <summary>Enable deep merging (recursive, not just top-level).</summary>
    public MergeProp DeepMerge() { _deepMerge = true; return this; }

    /// <summary>Prepend (instead of append) at the root level.</summary>
    public MergeProp Prepend() { _prependAtRoot = true; _appendAtRoot = false; return this; }

    /// <summary>Append the resolved value at a specific nested path (disables root-level appending).</summary>
    public MergeProp Append(string path) { _appendAtPaths.Add(path); _appendAtRoot = false; return this; }

    /// <summary>Prepend the resolved value at a specific nested path (disables root-level prepending).</summary>
    public MergeProp PrependAt(string path) { _prependAtPaths.Add(path); _prependAtRoot = false; return this; }

    /// <summary>Set a key (or keys) used for deduplication when merging.</summary>
    public MergeProp MatchOn(string key) { _matchOn.Add(key); return this; }
    public MergeProp MatchOn(IEnumerable<string> keys) { _matchOn.AddRange(keys); return this; }

    public MergeProp Once(bool once = true) { _once = once; return this; }
    public MergeProp Fresh(bool refresh = true) { _refresh = refresh; return this; }
    public MergeProp As(string key) { _key = key; return this; }
    public MergeProp Until(DateTimeOffset expiresAt) { _expiresAt = expiresAt.ToUnixTimeMilliseconds(); return this; }
    public MergeProp Until(TimeSpan ttl) { _expiresAt = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeMilliseconds(); return this; }
}
