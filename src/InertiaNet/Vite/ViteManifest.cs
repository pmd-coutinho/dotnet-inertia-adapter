using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace InertiaNet.Vite;

/// <summary>One parsed manifest per path, invalidated by file metadata on every read.</summary>
internal static class ViteManifest
{
    private readonly record struct Stamp(long Length, DateTime LastWrite);
    private sealed record CachedManifest(Stamp Stamp, JsonElement Root);

    public static JsonElement Read(string path, IMemoryCache? cache = null)
    {
        path = Path.GetFullPath(path);
        var file = new FileInfo(path);
        if (!file.Exists) throw new InvalidOperationException($"Vite manifest not found at '{path}'. Run `npm run build` first.");
        var stamp = new Stamp(file.Length, file.LastWriteTimeUtc);
        var key = (typeof(ViteManifest), path);
        if (cache?.TryGetValue(key, out CachedManifest? existing) == true && existing!.Stamp == stamp) return existing.Root;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Vite manifest must be a JSON object.");
        var root = document.RootElement.Clone();
        file.Refresh();
        // Do not publish a cached version if a deployment changed the file while reading it.
        if (cache is not null && file.Exists && new Stamp(file.Length, file.LastWriteTimeUtc) == stamp)
            cache.Set(key, new CachedManifest(stamp, root), new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10), Size = 1 });
        return root;
    }
}
