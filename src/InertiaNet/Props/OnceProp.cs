using InertiaNet.Interfaces;

namespace InertiaNet.Props;

/// <summary>
/// A prop that is resolved once, sent to the client, and then remembered
/// on the frontend. Excluded on subsequent Inertia visits until it expires
/// or is explicitly refreshed. Equivalent to Laravel's <c>Inertia::once()</c>.
/// </summary>
public sealed class OnceProp : IOnceable
{
    private readonly Func<IServiceProvider, CancellationToken, Task<object?>> _callback;

    private bool _refresh;
    private long? _expiresAt;
    private string? _key;

    public OnceProp(Func<IServiceProvider, CancellationToken, Task<object?>> callback)
    {
        _callback = callback;
    }

    public async Task<object?> ResolveAsync(IServiceProvider services, CancellationToken ct = default)
        => await _callback(services, ct);

    // --- IOnceable ---
    public bool ShouldResolveOnce() => true;
    public bool ShouldBeRefreshed() => _refresh;
    public string? GetKey() => _key;
    public long? ExpiresAt() => _expiresAt;

    // --- Fluent builder ---

    /// <summary>Force the once-cache to be refreshed on next request.</summary>
    public OnceProp Fresh(bool refresh = true) { _refresh = refresh; return this; }

    /// <summary>Set a custom cache key for this once-prop.</summary>
    public OnceProp As(string key) { _key = key; return this; }

    /// <summary>Set an expiry time for the once-cache.</summary>
    public OnceProp Until(DateTimeOffset expiresAt) { _expiresAt = expiresAt.ToUnixTimeMilliseconds(); return this; }
    public OnceProp Until(TimeSpan ttl) { _expiresAt = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeMilliseconds(); return this; }
}
