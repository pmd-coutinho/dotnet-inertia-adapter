using InertiaNet.Support;
using Microsoft.AspNetCore.Http;

namespace InertiaNet.Extensions;

/// <summary>
/// Extension methods on <see cref="HttpRequest"/> for the Inertia protocol.
/// </summary>
public static class HttpRequestExtensions
{
    /// <summary>Returns true when the request carries the <c>X-Inertia: true</c> header.</summary>
    public static bool IsInertia(this HttpRequest request)
        => request.Headers.ContainsKey(HeaderNames.Inertia);

    /// <summary>
    /// Returns the component name from the <c>X-Inertia-Partial-Component</c> header,
    /// or null when absent.
    /// </summary>
    public static string? GetPartialComponent(this HttpRequest request)
        => request.Headers.TryGetValue(HeaderNames.PartialComponent, out var v) ? (string?)v : null;

    /// <summary>
    /// Returns the set of prop keys requested in a partial reload
    /// (<c>X-Inertia-Partial-Data</c>), or an empty array when absent.
    /// </summary>
    public static string[] GetPartialOnly(this HttpRequest request)
        => request.Headers.TryGetValue(HeaderNames.PartialOnly, out var v)
            ? ((string?)v)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []
            : [];

    /// <summary>
    /// Returns the set of prop keys to exclude from a partial reload
    /// (<c>X-Inertia-Partial-Except</c>), or an empty array when absent.
    /// </summary>
    public static string[] GetPartialExcept(this HttpRequest request)
        => request.Headers.TryGetValue(HeaderNames.PartialExcept, out var v)
            ? ((string?)v)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []
            : [];

    /// <summary>
    /// Returns the version string sent by the client via <c>X-Inertia-Version</c>,
    /// or null when absent.
    /// </summary>
    public static string? GetInertiaVersion(this HttpRequest request)
        => request.Headers.TryGetValue(HeaderNames.Version, out var v) ? (string?)v : null;

    /// <summary>
    /// Returns the error bag name from <c>X-Inertia-Error-Bag</c>, or null when absent.
    /// </summary>
    public static string? GetErrorBag(this HttpRequest request)
        => request.Headers.TryGetValue(HeaderNames.ErrorBag, out var v) ? (string?)v : null;

    /// <summary>
    /// Returns the reset keys from <c>X-Inertia-Reset</c>, or an empty array when absent.
    /// </summary>
    public static string[] GetResetKeys(this HttpRequest request)
        => request.Headers.TryGetValue(HeaderNames.Reset, out var v)
            ? ((string?)v)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []
            : [];

    /// <summary>
    /// Returns the once-prop keys already known to the client from
    /// <c>X-Inertia-Except-Once-Props</c>, or an empty array when absent.
    /// </summary>
    public static string[] GetExceptOnceProps(this HttpRequest request)
        => request.Headers.TryGetValue(HeaderNames.ExceptOnceProps, out var v)
            ? ((string?)v)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []
            : [];
}
