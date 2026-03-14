namespace InertiaNet.Core;

/// <summary>
/// Configuration options for the Vite integration.
/// </summary>
public class ViteOptions
{
    /// <summary>
    /// The web root directory that Vite writes its output into.
    /// Defaults to <c>"wwwroot"</c>.
    /// </summary>
    public string PublicDirectory { get; set; } = "wwwroot";

    /// <summary>
    /// The sub-directory inside <see cref="PublicDirectory"/> where Vite places
    /// the production build artefacts. Defaults to <c>"build"</c>.
    /// </summary>
    public string BuildDirectory { get; set; } = "build";

    /// <summary>
    /// The name of the Vite manifest file placed inside <see cref="BuildDirectory"/>.
    /// Defaults to <c>"manifest.json"</c>.
    /// </summary>
    public string ManifestFilename { get; set; } = "manifest.json";

    /// <summary>
    /// The name of the HMR hot-file written by <c>laravel-vite-plugin</c> (or compatible
    /// plugins) at the root of <see cref="PublicDirectory"/> while the dev server is running.
    /// Defaults to <c>"hot"</c>.
    /// </summary>
    public string HotFile { get; set; } = "hot";
}
