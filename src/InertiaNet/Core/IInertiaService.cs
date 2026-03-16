using InertiaNet.Interfaces;
using InertiaNet.Props;
using Microsoft.AspNetCore.Http;

namespace InertiaNet.Core;

/// <summary>
/// The main Inertia service. Scoped to each HTTP request.
/// Accumulates shared props, per-request flags, flash data, and builds
/// <see cref="InertiaResult"/> instances for rendering.
/// </summary>
public interface IInertiaService
{
    // ── Shared props ────────────────────────────────────────────────────────

    /// <summary>Share a single prop value for the duration of this request.</summary>
    IInertiaService Share(string key, object? value);

    /// <summary>Share multiple props at once.</summary>
    IInertiaService Share(IDictionary<string, object?> data);

    /// <summary>Share all props provided by an <see cref="IProvidesInertiaProperties"/> object.</summary>
    IInertiaService Share(IProvidesInertiaProperties provider);

    /// <summary>Returns all currently shared props.</summary>
    IReadOnlyDictionary<string, object?> GetSharedProps();

    /// <summary>
    /// Share a once-prop: resolved once, then remembered by the client across navigations.
    /// Equivalent to Laravel's <c>Inertia::shareOnce(key, callback)</c>.
    /// </summary>
    OnceProp ShareOnce(string key, Func<IServiceProvider, CancellationToken, Task<object?>> callback);

    /// <summary>
    /// Share a once-prop from a plain value. The value is always resolved (no lazy evaluation),
    /// but the client will cache and reuse it across navigations.
    /// </summary>
    OnceProp ShareOnce(string key, object? value);

    // ── Prop factories ──────────────────────────────────────────────────────

    /// <summary>Creates an <see cref="AlwaysProp"/> from a plain value.</summary>
    AlwaysProp Always(object? value);

    /// <summary>Creates an <see cref="AlwaysProp"/> from an async delegate.</summary>
    AlwaysProp Always(Func<IServiceProvider, CancellationToken, Task<object?>> callback);

    /// <summary>Creates an <see cref="OptionalProp"/> — excluded on initial load.</summary>
    OptionalProp Optional(Func<IServiceProvider, CancellationToken, Task<object?>> callback);

    /// <summary>Creates a <see cref="DeferProp"/> — loaded asynchronously after initial render.</summary>
    DeferProp Defer(Func<IServiceProvider, CancellationToken, Task<object?>> callback, string? group = null);

    /// <summary>Creates a <see cref="MergeProp"/> from a plain value — merged with existing client data.</summary>
    MergeProp Merge(object? value);

    /// <summary>Creates a <see cref="MergeProp"/> from an async delegate.</summary>
    MergeProp Merge(Func<IServiceProvider, CancellationToken, Task<object?>> callback);

    /// <summary>Creates a deep-<see cref="MergeProp"/> from a plain value.</summary>
    MergeProp DeepMerge(object? value);

    /// <summary>Creates a deep-<see cref="MergeProp"/> from an async delegate.</summary>
    MergeProp DeepMerge(Func<IServiceProvider, CancellationToken, Task<object?>> callback);

    /// <summary>Creates an <see cref="OnceProp"/> — resolved once, cached client-side.</summary>
    OnceProp Once(Func<IServiceProvider, CancellationToken, Task<object?>> callback);

    /// <summary>Creates a <see cref="ScrollProp"/> for infinite scroll pagination.</summary>
    ScrollProp Scroll(
        object? value,
        string wrapper = "data",
        IProvidesScrollMetadata? metadata = null);

    /// <summary>Creates a <see cref="ScrollProp"/> for infinite scroll pagination (async).</summary>
    ScrollProp Scroll(
        Func<IServiceProvider, CancellationToken, Task<object?>> callback,
        string wrapper = "data",
        Func<IServiceProvider, CancellationToken, Task<IProvidesScrollMetadata?>>? metadata = null);

    // ── Rendering ────────────────────────────────────────────────────────────

    /// <summary>Renders the given Inertia component with an anonymous object as props.</summary>
    InertiaResult Render(string component, object? props = null);

    /// <summary>Renders the given Inertia component with an explicit props dictionary.</summary>
    InertiaResult Render(string component, IDictionary<string, object?> props);

    // ── Navigation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns an external redirect response. For Inertia requests: 409 + X-Inertia-Location.
    /// For normal requests: standard 302 redirect.
    /// </summary>
    IResult Location(string url);

    /// <summary>Signals the client to clear browser history state.</summary>
    IInertiaService ClearHistory();

    /// <summary>Signals the client to preserve the URL fragment across a redirect (v3).</summary>
    IInertiaService PreserveFragment();

    /// <summary>Enables/disables history encryption for the current response.</summary>
    IInertiaService EncryptHistory(bool encrypt = true);

    // ── Flash data ───────────────────────────────────────────────────────────

    /// <summary>Adds a flash data entry not persisted in browser history state (v3).</summary>
    IInertiaService Flash(string key, object? value);

    /// <summary>Adds multiple flash data entries.</summary>
    IInertiaService Flash(IDictionary<string, object?> data);

    /// <summary>Returns all flash data accumulated for the current request.</summary>
    IReadOnlyDictionary<string, object?> GetFlashData();

    // ── SSR exclusions ───────────────────────────────────────────────────────

    /// <summary>Excludes specific URL paths from SSR (v3).</summary>
    IInertiaService WithoutSsr(params string[] paths);

    // ── Conditional props ─────────────────────────────────────────────────────

    /// <summary>Shares the prop only when <paramref name="condition"/> is true.</summary>
    IInertiaService When(bool condition, string key, object? value);

    /// <summary>Shares the prop (async callback) only when <paramref name="condition"/> is true.</summary>
    IInertiaService When(bool condition, string key, Func<IServiceProvider, CancellationToken, Task<object?>> callback);

    /// <summary>Shares the prop only when <paramref name="condition"/> is false.</summary>
    IInertiaService Unless(bool condition, string key, object? value);

    /// <summary>Shares the prop (async callback) only when <paramref name="condition"/> is false.</summary>
    IInertiaService Unless(bool condition, string key, Func<IServiceProvider, CancellationToken, Task<object?>> callback);

    // ── Per-request flags (read by InertiaResult) ────────────────────────────

    bool GetClearHistory();
    bool GetPreserveFragment();
    bool? GetEncryptHistory();
    IReadOnlyList<string> GetSsrExcludedPaths();
}
