using InertiaNet.Core;

namespace InertiaNet.Ssr;

/// <summary>
/// Dispatches a page object to the SSR server and returns the rendered HTML.
/// Returns <c>null</c> on failure (the response then falls back to client-side rendering).
/// </summary>
public interface ISsrGateway
{
    /// <summary>
    /// Renders the given Inertia page server-side.
    /// Returns <c>null</c> if SSR is disabled, the path is excluded, or rendering fails.
    /// </summary>
    Task<SsrResponse?> DispatchAsync(InertiaPage page, CancellationToken ct = default);
}
