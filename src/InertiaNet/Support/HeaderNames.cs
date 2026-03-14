namespace InertiaNet.Support;

/// <summary>
/// HTTP header name constants for the Inertia.js v3 protocol.
/// </summary>
public static class HeaderNames
{
    /// <summary>Marks the request/response as an Inertia request. Value: "true".</summary>
    public const string Inertia = "X-Inertia";

    /// <summary>Scopes validation errors to a named error bag.</summary>
    public const string ErrorBag = "X-Inertia-Error-Bag";

    /// <summary>URL for an external redirect (returns 409 Conflict).</summary>
    public const string Location = "X-Inertia-Location";

    /// <summary>URL for a fragment-bearing redirect (v3, returns 409 Conflict).</summary>
    public const string Redirect = "X-Inertia-Redirect";

    /// <summary>Current asset version sent by the server.</summary>
    public const string Version = "X-Inertia-Version";

    /// <summary>Component name for partial reloads.</summary>
    public const string PartialComponent = "X-Inertia-Partial-Component";

    /// <summary>Comma-separated prop keys to include in a partial reload.</summary>
    public const string PartialOnly = "X-Inertia-Partial-Data";

    /// <summary>Comma-separated prop keys to exclude in a partial reload.</summary>
    public const string PartialExcept = "X-Inertia-Partial-Except";

    /// <summary>Comma-separated prop keys whose merge state should be reset.</summary>
    public const string Reset = "X-Inertia-Reset";

    /// <summary>"prepend" or "append" — direction for infinite scroll merge.</summary>
    public const string InfiniteScrollMergeIntent = "X-Inertia-Infinite-Scroll-Merge-Intent";

    /// <summary>Comma-separated once-prop keys already loaded by the client.</summary>
    public const string ExceptOnceProps = "X-Inertia-Except-Once-Props";

    /// <summary>
    /// Set to "prefetch" by the client when making a prefetch request.
    /// Adapters should avoid side effects (session writes, etc.) when this header is present.
    /// </summary>
    public const string Purpose = "Purpose";
}
