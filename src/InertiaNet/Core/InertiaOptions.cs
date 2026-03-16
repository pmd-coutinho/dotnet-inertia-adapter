using Microsoft.AspNetCore.Http;

namespace InertiaNet.Core;

/// <summary>
/// Configuration options for InertiaNet.
/// </summary>
public class InertiaOptions
{
    /// <summary>
    /// The root Razor view used for the initial (non-Inertia) page load.
    /// Defaults to "App" (Views/Shared/App.cshtml).
    /// </summary>
    public string RootView { get; set; } = "App";

    /// <summary>
    /// A delegate that returns the current asset version string.
    /// Called on every Inertia request to compare with the client-sent
    /// X-Inertia-Version header. Return null to disable version checking.
    /// </summary>
    public Func<string?>? Version { get; set; }

    /// <summary>
    /// Enable history encryption globally for all responses.
    /// Can be overridden per-response via <c>IInertiaService.EncryptHistory()</c>.
    /// Defaults to false.
    /// </summary>
    public bool EncryptHistory { get; set; } = false;

    /// <summary>
    /// Include a <c>sharedProps</c> metadata array in Inertia responses,
    /// listing the top-level keys registered via <c>Share()</c>.
    /// Enables frontend optimisations for instant visits.
    /// Defaults to true (mirrors Inertia v3 default).
    /// </summary>
    public bool ExposeSharedPropKeys { get; set; } = true;

    /// <summary>
    /// Whether to return all validation errors per field (true),
    /// or only the first error per field (false, default — matches Inertia convention).
    /// </summary>
    public bool WithAllErrors { get; set; } = false;

    /// <summary>
    /// Default Cache-Control max-age (in seconds) for prefetch responses.
    /// The client may override this via the X-Inertia-Prefetch header.
    /// </summary>
    public int PrefetchCacheMaxAge { get; set; } = 10;

    /// <summary>
    /// SSR (server-side rendering) configuration.
    /// </summary>
    public SsrOptions Ssr { get; set; } = new();

    /// <summary>
    /// Page component validation configuration.
    /// </summary>
    public PagesOptions Pages { get; set; } = new();

    /// <summary>
    /// Custom exception handler delegate for Inertia responses (v3).
    /// Called when an unhandled exception occurs during Inertia request processing.
    /// Return an <see cref="InertiaResult"/> to render a custom error page,
    /// or return null to rethrow the original exception.
    /// </summary>
    /// <remarks>
    /// Example:
    /// <code>
    /// options.HandleExceptionsUsing = (exception, context) =>
    /// {
    ///     var inertia = context.RequestServices.GetRequiredService&lt;IInertiaService&gt;();
    ///     return inertia.Render("Error", new { message = exception.Message });
    /// };
    /// </code>
    /// </remarks>
    public Func<Exception, HttpContext, InertiaResult?>? HandleExceptionsUsing { get; set; }
}

/// <summary>
/// Server-side rendering configuration.
/// </summary>
public class SsrOptions
{
    /// <summary>Whether SSR is enabled. Defaults to false.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// URL of the Node.js SSR server.
    /// Defaults to "http://127.0.0.1:13714".
    /// </summary>
    public string Url { get; set; } = "http://127.0.0.1:13714";

    /// <summary>
    /// When true, SSR render failures throw an exception instead of
    /// silently falling back to client-side rendering.
    /// Defaults to false.
    /// </summary>
    public bool ThrowOnError { get; set; } = false;

    /// <summary>
    /// URL path patterns to exclude from SSR.
    /// Supports wildcards, e.g. "/admin/*".
    /// </summary>
    public string[] ExcludePaths { get; set; } = [];
}

/// <summary>
/// Page component file validation configuration.
/// </summary>
public class PagesOptions
{
    /// <summary>
    /// When true, InertiaNet verifies that the requested component file
    /// exists on disk before rendering. Useful to catch typos during development.
    /// Defaults to false (enable in test environments).
    /// </summary>
    public bool EnsurePagesExist { get; set; } = false;

    /// <summary>
    /// Directories to search for page component files.
    /// </summary>
    public string[] Paths { get; set; } = [];

    /// <summary>
    /// Paths to Vite/Mix manifest files to check for component existence.
    /// Defaults to empty — populate with e.g. "wwwroot/build/manifest.json".
    /// </summary>
    public string[] ManifestPaths { get; set; } = [];

    /// <summary>
    /// Allowed file extensions for page components.
    /// </summary>
    public string[] Extensions { get; set; } = ["js", "jsx", "ts", "tsx", "vue", "svelte"];
}
