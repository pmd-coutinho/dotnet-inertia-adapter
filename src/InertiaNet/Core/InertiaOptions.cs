using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace InertiaNet.Core;

/// <summary>
/// Configuration options for InertiaNet.
/// </summary>
public class InertiaOptions
{
    /// <summary>
    /// Selects requests handled by the Inertia middleware and validation filters.
    /// Null (the default) handles every path. Include initial HTML requests as well
    /// as requests carrying X-Inertia when restricting a mixed application.
    /// </summary>
    public Func<HttpContext, bool>? RequestPredicate { get; set; }

    /// <summary>
    /// Redirect unstarted empty Inertia 200/204 responses back to a page.
    /// Defaults to true. Set to false to preserve intentional empty responses.
    /// Precognition responses are always excluded.
    /// </summary>
    public bool RedirectEmptyResponses { get; set; } = true;

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

    /// <summary>Opt-in development diagnostics for prop resolution; no prop values are recorded.</summary>
    public PropDiagnosticsOptions PropDiagnostics { get; set; } = new();

    /// <summary>
    /// Custom JSON serializer options for Inertia page responses.
    /// InertiaNet preserves the protocol envelope field names regardless of these settings,
    /// but custom converters and other value-level serialization behaviors still apply.
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>Representation of Int64, UInt64 and BigInteger props/flash. Defaults to JSON numbers.</summary>
    public LargeIntegerSerialization LargeIntegerSerialization { get; set; }

    /// <summary>
     /// Custom exception handler delegate for Inertia responses (v3).
     /// Called when an unhandled exception occurs during Inertia request processing.
     /// Return an <see cref="IResult"/> or <see cref="Microsoft.AspNetCore.Mvc.IActionResult"/>
     /// to render a custom error response, or return null to rethrow the original exception.
     /// </summary>
    /// <remarks>
    /// Example:
     /// <code>
     /// options.HandleExceptionsUsing = (exception, context) =>
     /// {
     ///     return InertiaNet.Extensions.InertiaResults.Inertia("Errors/ServerError", new { message = exception.Message });
     /// };
     /// </code>
     /// </remarks>
    public Func<Exception, HttpContext, object?>? HandleExceptionsUsing { get; set; }
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
    /// Maximum time for an SSR request, including reading its response. Defaults to
    /// three seconds. Use <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to disable this limit.
    /// A shorter timeout configured on the named HttpClient still applies.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

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

/// <summary>Controls opt-in prop timing/classification diagnostics.</summary>
public class PropDiagnosticsOptions
{
    /// <summary>Enable collection and diagnostic logging. Defaults to false.</summary>
    public bool Enabled { get; set; }
    /// <summary>Restrict diagnostics to the Development environment. Defaults to true.</summary>
    public bool DevelopmentOnly { get; set; } = true;
    /// <summary>Maximum measurements retained per resolution, including nested paths. Defaults to 200.</summary>
    public int MaxEntries { get; set; } = 200;
    /// <summary>Threshold for warning-level slow-prop logging. Defaults to 100 milliseconds.</summary>
    public TimeSpan SlowPropThreshold { get; set; } = TimeSpan.FromMilliseconds(100);
}
