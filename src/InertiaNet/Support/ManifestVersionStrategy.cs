using System.IO.Hashing;
using System.Text;

namespace InertiaNet.Support;

/// <summary>
/// Computes an asset version string from a manifest file using xxHash128,
/// matching the algorithm used by the Inertia Laravel adapter.
/// </summary>
public static class ManifestVersionStrategy
{
    /// <summary>
    /// Returns an xxHash128 hex digest of the file at <paramref name="path"/>,
    /// or <c>null</c> if the file does not exist.
    /// </summary>
    public static string? Hash(string path)
    {
        if (!File.Exists(path))
            return null;

        var bytes = File.ReadAllBytes(path);
        var hash = XxHash128.Hash(bytes);

        // Convert the 16-byte hash to a 32-char lowercase hex string
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Returns an xxHash128 hex digest of a string value.
    /// Useful for hashing a URL or any other string-based version seed.
    /// </summary>
    public static string HashString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = XxHash128.Hash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Returns a version strategy delegate that hashes the Vite manifest
    /// at <c>wwwroot/build/manifest.json</c>, falling back to the Mix manifest
    /// at <c>wwwroot/mix-manifest.json</c>. Returns null if neither exists.
    /// </summary>
    public static Func<string?> FromViteOrMix(string webRootPath)
    {
        return () =>
        {
            var vite = Path.Combine(webRootPath, "build", "manifest.json");
            if (File.Exists(vite))
                return Hash(vite);

            var mix = Path.Combine(webRootPath, "mix-manifest.json");
            if (File.Exists(mix))
                return Hash(mix);

            return null;
        };
    }
}
