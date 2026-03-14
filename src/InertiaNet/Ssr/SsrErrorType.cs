namespace InertiaNet.Ssr;

/// <summary>
/// Categorises the type of SSR failure, matching the SsrErrorType enum
/// from the Inertia Laravel adapter v3.
/// </summary>
public enum SsrErrorType
{
    /// <summary>The SSR script attempted to use a browser-only API (e.g. window, document).</summary>
    BrowserApi,

    /// <summary>The SSR server could not resolve the requested component.</summary>
    ComponentResolution,

    /// <summary>The component rendered but threw an error during rendering.</summary>
    Render,

    /// <summary>Could not connect to the SSR server.</summary>
    Connection,

    /// <summary>An unknown or unclassified error occurred.</summary>
    Unknown,
}
