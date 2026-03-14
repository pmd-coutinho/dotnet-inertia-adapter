namespace InertiaNet.Core;

/// <summary>
/// Immutable data transfer object representing the full Inertia page object
/// sent to the frontend (either as JSON in an XHR response, or embedded in
/// the initial HTML page as a &lt;script type="application/json"&gt; element).
/// </summary>
public sealed class InertiaPage
{
    /// <summary>The frontend component name, e.g. "Users/Index".</summary>
    public required string Component { get; init; }

    /// <summary>Resolved props to send to the component.</summary>
    public required Dictionary<string, object?> Props { get; init; }

    /// <summary>The current request URL (relative, with query string).</summary>
    public required string Url { get; init; }

    /// <summary>The current asset version string (from manifest hash).</summary>
    public string? Version { get; init; }

    /// <summary>
    /// Top-level keys of props registered via Share().
    /// Only included when <c>InertiaOptions.ExposeSharedPropKeys</c> is true.
    /// Enables the frontend to carry shared props forward during instant visits.
    /// </summary>
    public IReadOnlyList<string>? SharedProps { get; init; }

    /// <summary>Paths of props that should be merged (appended) with existing client data.</summary>
    public IReadOnlyList<string>? MergeProps { get; init; }

    /// <summary>Paths of props that should be prepended to existing client data.</summary>
    public IReadOnlyList<string>? PrependProps { get; init; }

    /// <summary>Paths of props that should be deep-merged with existing client data.</summary>
    public IReadOnlyList<string>? DeepMergeProps { get; init; }

    /// <summary>
    /// Keys used for deduplication when merging.
    /// Map of prop path -> match key expression.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? MatchPropsOn { get; init; }

    /// <summary>
    /// Groups of deferred prop paths loaded asynchronously after the initial render.
    /// Map of group name -> list of prop paths.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? DeferredProps { get; init; }

    /// <summary>
    /// Pagination metadata for scroll (infinite scroll) props.
    /// Map of prop path -> scroll metadata object.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? ScrollProps { get; init; }

    /// <summary>
    /// Once-prop keys with optional expiry timestamps (ms since epoch).
    /// Map of prop key -> expiry object (null means no expiry).
    /// </summary>
    public IReadOnlyDictionary<string, object?>? OnceProps { get; init; }

    /// <summary>Flash data not persisted in browser history state.</summary>
    public IReadOnlyDictionary<string, object?>? Flash { get; init; }

    /// <summary>
    /// When true, signals the client to clear the browser history state.
    /// Only included in the response when true (v3 optimization).
    /// </summary>
    public bool? ClearHistory { get; init; }

    /// <summary>
    /// When true, signals the client to encrypt browser history state.
    /// Only included in the response when true (v3 optimization).
    /// </summary>
    public bool? EncryptHistory { get; init; }

    /// <summary>
    /// When true, signals the client to preserve the URL fragment across a redirect.
    /// Only included in the response when true (v3-only).
    /// </summary>
    public bool? PreserveFragment { get; init; }
}
