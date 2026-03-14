namespace InertiaNet.Interfaces;

/// <summary>
/// Marks a prop as resolvable once — sent to the client a single time and
/// remembered on the frontend. Excluded on subsequent Inertia visits.
/// </summary>
public interface IOnceable
{
    /// <summary>Returns true if this prop should only be resolved once.</summary>
    bool ShouldResolveOnce();

    /// <summary>Returns true if this prop should be force-refreshed despite being cached.</summary>
    bool ShouldBeRefreshed();

    /// <summary>
    /// Optional custom cache key. When null, the prop name is used as the key.
    /// </summary>
    string? GetKey();

    /// <summary>
    /// Optional expiry timestamp (milliseconds since Unix epoch).
    /// When null, the cached value never expires.
    /// </summary>
    long? ExpiresAt();
}
