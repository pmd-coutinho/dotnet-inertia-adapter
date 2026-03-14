using InertiaNet.Interfaces;

namespace InertiaNet.Props;

/// <summary>
/// A prop that is excluded from the initial page load and only included when
/// explicitly requested via a partial reload. Equivalent to Laravel's
/// <c>Inertia::optional()</c>.
/// <para>
/// Can also be configured as a once-prop (resolved once, then cached client-side).
/// </para>
/// </summary>
public sealed class OptionalProp : IIgnoreFirstLoad, IOnceable
{
    private readonly Func<IServiceProvider, CancellationToken, Task<object?>> _callback;

    // IOnceable state
    private bool _once;
    private bool _refresh;
    private long? _expiresAt;
    private string? _key;

    public OptionalProp(Func<IServiceProvider, CancellationToken, Task<object?>> callback)
    {
        _callback = callback;
    }

    public async Task<object?> ResolveAsync(IServiceProvider services, CancellationToken ct = default)
        => await _callback(services, ct);

    // --- IOnceable ---
    public bool ShouldResolveOnce() => _once;
    public bool ShouldBeRefreshed() => _refresh;
    public string? GetKey() => _key;
    public long? ExpiresAt() => _expiresAt;

    /// <summary>Mark this prop as resolved-once, with an optional custom key and TTL.</summary>
    public OptionalProp Once(bool once = true, string? key = null, DateTimeOffset? until = null)
    {
        _once = once;
        _key = key;
        _expiresAt = until.HasValue ? until.Value.ToUnixTimeMilliseconds() : null;
        return this;
    }

    /// <summary>Force the once-cache to be refreshed on next request.</summary>
    public OptionalProp Fresh(bool refresh = true) { _refresh = refresh; return this; }

    /// <summary>Set a custom cache key for this once-prop.</summary>
    public OptionalProp As(string key) { _key = key; return this; }

    /// <summary>Set an expiry time for the once-cache.</summary>
    public OptionalProp Until(DateTimeOffset expiresAt) { _expiresAt = expiresAt.ToUnixTimeMilliseconds(); return this; }
    public OptionalProp Until(TimeSpan ttl) { _expiresAt = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeMilliseconds(); return this; }
}
