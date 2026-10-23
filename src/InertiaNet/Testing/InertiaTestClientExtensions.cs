using InertiaNet.Support;

namespace InertiaNet.Testing;

/// <summary>Partial request headers used when reloading an asserted page.</summary>
public sealed class InertiaReloadOptions
{
    /// <summary>Prop paths to include; empty means no inclusion filter.</summary>
    public IReadOnlyList<string> Only { get; init; } = [];
    /// <summary>Prop paths to exclude.</summary>
    public IReadOnlyList<string> Except { get; init; } = [];
    /// <summary>Merge prop paths whose existing values should be reset.</summary>
    public IReadOnlyList<string> Reset { get; init; } = [];
    /// <summary>Once keys already available on the simulated client.</summary>
    public IReadOnlyList<string> ExceptOnceProps { get; init; } = [];
}

/// <summary>HTTP helpers that exercise server reloads and deferred groups through real requests.</summary>
public static class InertiaTestClientExtensions
{
    /// <summary>Sends an Inertia GET and asserts its response. Client default headers are not modified.</summary>
    public static Task<AssertableInertiaPage> GetInertiaAsync(this HttpClient client, string uri, CancellationToken ct = default)
        => SendAsync(client, uri, null, null, null, ct);

    /// <summary>
    /// Reloads the page's URL with its component/version and selected partial headers.
    /// Returns the server response without simulating client-side prop merging.
    /// </summary>
    public static Task<AssertableInertiaPage> ReloadInertiaAsync(this HttpClient client, AssertableInertiaPage page,
        InertiaReloadOptions? options = null, CancellationToken ct = default)
        => SendAsync(client, page.Page.Url, page.Page.Component, page.Page.Version, options, ct);

    /// <summary>
    /// Loads selected deferred groups (all by default), one request per group, and returns
    /// their individual server pages. Unknown group names fail before any request is sent.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, AssertableInertiaPage>> LoadDeferredPropsAsync(this HttpClient client,
        AssertableInertiaPage page, IEnumerable<string>? groups = null, CancellationToken ct = default)
    {
        var deferred = page.Page.DeferredProps;
        var selected = (groups ?? deferred?.Keys ?? []).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var group in selected)
            if (deferred?.ContainsKey(group) != true) throw new AssertionException($"Deferred prop group '{group}' not found.");
        var results = new Dictionary<string, AssertableInertiaPage>(StringComparer.Ordinal);
        foreach (var group in selected)
        {
            ct.ThrowIfCancellationRequested();
            var response = await client.ReloadInertiaAsync(page, new() { Only = deferred![group] }, ct);
            response.HasComponent(page.Page.Component);
            results[group] = response;
        }
        return results;
    }

    private static async Task<AssertableInertiaPage> SendAsync(HttpClient client, string uri, string? component,
        string? version, InertiaReloadOptions? options, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add(HeaderNames.Inertia, "true");
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        if (version is not null) request.Headers.Add(HeaderNames.Version, version);
        if (options is not null)
        {
            if (options.Only.Count > 0 || options.Except.Count > 0 || options.Reset.Count > 0)
                request.Headers.Add(HeaderNames.PartialComponent, component);
            AddPaths(request, HeaderNames.PartialOnly, options.Only);
            AddPaths(request, HeaderNames.PartialExcept, options.Except);
            AddPaths(request, HeaderNames.Reset, options.Reset);
            AddPaths(request, HeaderNames.ExceptOnceProps, options.ExceptOnceProps);
        }
        using var response = await client.SendAsync(request, ct);
        return await response.AssertInertiaAsync(ct);
    }

    private static void AddPaths(HttpRequestMessage request, string header, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        if (paths.Any(path => string.IsNullOrWhiteSpace(path) || path.IndexOfAny([',', '\r', '\n']) >= 0))
            throw new ArgumentException($"'{header}' requires nonempty prop paths without commas or newlines.");
        request.Headers.Add(header, string.Join(',', paths));
    }
}
