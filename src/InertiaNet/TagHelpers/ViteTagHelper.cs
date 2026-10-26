using InertiaNet.Core;
using InertiaNet.Vite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace InertiaNet.TagHelpers;

/// <summary>
/// Renders the appropriate <c>&lt;script&gt;</c> or <c>&lt;link&gt;</c> tags for a Vite entry-point.
/// <para>
/// In HMR mode (when the <c>hot</c> file is present) it injects the Vite dev-server client
/// and the requested module directly. In production it resolves the hashed filename from
/// the Vite manifest and emits the correct tags including any associated CSS chunks.
/// </para>
/// </summary>
/// <remarks>
/// Usage in <c>App.cshtml</c>:
/// <code>
/// &lt;vite-input src="src/main.tsx" /&gt;
/// </code>
/// </remarks>
[HtmlTargetElement("vite-input", Attributes = "src", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ViteInputTagHelper : TagHelper
{
    private readonly IOptions<ViteOptions> _options;
    private readonly IMemoryCache? _cache;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    /// <summary>The Vite entry-point path as declared in <c>vite.config</c>, e.g. <c>src/main.tsx</c>.</summary>
    public string Src { get; set; } = string.Empty;

    /// <summary>The current request's CSP nonce, overriding ViteOptions.NonceProvider.</summary>
    public string? Nonce { get; set; }

    public ViteInputTagHelper(IOptions<ViteOptions> options) : this(options, null, null) { }
    public ViteInputTagHelper(IOptions<ViteOptions> options, IMemoryCache? cache) : this(options, cache, null) { }

    /// <summary>Creates a Vite tag helper using the shared cache and current request.</summary>
    [ActivatorUtilitiesConstructor]
    public ViteInputTagHelper(IOptions<ViteOptions> options, IMemoryCache? cache = null, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _cache = cache;
        _httpContextAccessor = httpContextAccessor;
    }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;
        output.TagMode = TagMode.StartTagAndEndTag;

        var opts = _options.Value;
        var httpContext = _httpContextAccessor?.HttpContext;
        var nonce = Nonce ?? (httpContext is null ? null : opts.NonceProvider?.Invoke(httpContext));
        var pathBase = httpContext?.Request.PathBase.ToUriComponent() ?? string.Empty;

        if (IsRunningHot(opts))
        {
            var hotUrl = ReadHotUrl(opts).TrimEnd('/');
            output.Content.AppendHtml(MakeScriptTag($"{hotUrl}/@vite/client", ViteTagAttributes.Security(nonce)));
            output.Content.AppendHtml("\n    ");
            output.Content.AppendHtml(MakeScriptTag($"{hotUrl}/{Src.TrimStart('/')}", ViteTagAttributes.Security(nonce)));
            return;
        }

        var manifestPath = BuildPath(opts, opts.ManifestFilename);
        var manifest = ViteManifest.Read(manifestPath, opts.CacheManifest ? _cache : null);
        if (!manifest.TryGetProperty(Src, out var entry))
            throw new InvalidOperationException(
                $"Entry '{Src}' not found in Vite manifest. Make sure it is listed under `build.rollupOptions.input`.");

        var file = entry.GetProperty("file").GetString()!;
        // CSS files may be listed by filename in another chunk's css array.
        // Index by output filename so their own manifest integrity is preserved.
        var integrityByFile = new Dictionary<string, string>(StringComparer.Ordinal);
        if (opts.IntegrityKey is { } integrityKey)
            foreach (var chunk in manifest.EnumerateObject().Select(property => property.Value))
                if (chunk.TryGetProperty("file", out var chunkFile) && chunkFile.ValueKind == JsonValueKind.String
                    && chunk.TryGetProperty(integrityKey, out var integrity) && integrity.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(integrity.GetString()))
                    integrityByFile[chunkFile.GetString()!] = integrity.GetString()!;
        string Security(string name) => ViteTagAttributes.Security(nonce, integrityByFile.GetValueOrDefault(name));
        string Url(string name) => AssetUrl(opts, pathBase, name);
        var styles = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { Src };
        var preloads = new HashSet<string>(StringComparer.Ordinal);
        void Append(string html)
        {
            if (output.Content.IsModified) output.Content.AppendHtml("\n    ");
            output.Content.AppendHtml(html);
        }
        void Style(string name) { if (styles.Add(name)) Append(MakeLinkTag(Url(name), Security(name))); }
        void Dependencies(JsonElement chunk)
        {
            if (chunk.TryGetProperty("css", out var css))
                foreach (var name in css.EnumerateArray()) Style(name.GetString()!);
            if (!chunk.TryGetProperty("imports", out var imports)) return;
            foreach (var imported in imports.EnumerateArray())
            {
                var key = imported.GetString()!;
                if (!visited.Add(key)) continue;
                if (!manifest.TryGetProperty(key, out var dependency))
                    throw new InvalidOperationException($"Imported entry '{key}' not found in Vite manifest.");
                var dependencyFile = dependency.GetProperty("file").GetString()!;
                if (IsCss(dependencyFile)) Style(dependencyFile);
                else preloads.Add(dependencyFile);
                Dependencies(dependency);
            }
        }
        if (IsCss(file)) Style(file);
        else Append(MakeScriptTag(Url(file), Security(file)));
        Dependencies(entry);
        foreach (var dependency in preloads)
            Append($"<link rel=\"modulepreload\" href=\"{HtmlEncoder.Default.Encode(Url(dependency))}\"{Security(dependency)} />");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsCss(string path) =>
        Regex.IsMatch(path, @"\.(css|less|sass|scss|styl|stylus|pcss|postcss)$", RegexOptions.IgnoreCase);

    private static string MakeScriptTag(string src, string attributes) =>
        $"<script type=\"module\" src=\"{HtmlEncoder.Default.Encode(src)}\"{attributes}></script>";

    private static string MakeLinkTag(string href, string attributes) =>
        $"<link rel=\"stylesheet\" href=\"{HtmlEncoder.Default.Encode(href)}\"{attributes} />";

    private static string AssetUrl(ViteOptions opts, string pathBase, string file) =>
        $"{pathBase}/{opts.BuildDirectory.Trim('/')}/{file.TrimStart('/')}";

    private static string BuildPath(ViteOptions opts, string file) =>
        Path.Combine(opts.PublicDirectory, opts.BuildDirectory, file);

    private static bool IsRunningHot(ViteOptions opts) =>
        File.Exists(Path.Combine(opts.PublicDirectory, opts.HotFile));

    private static string ReadHotUrl(ViteOptions opts) =>
        File.ReadAllText(Path.Combine(opts.PublicDirectory, opts.HotFile)).Trim();
}

/// <summary>
/// Injects the React Fast Refresh preamble required when using
/// <c>@vitejs/plugin-react</c> in HMR mode.
/// Outputs nothing in production (when the <c>hot</c> file is absent).
/// </summary>
/// <remarks>
/// Must appear <em>before</em> <c>&lt;vite-input&gt;</c> in the layout to avoid the
/// "React preamble" console error.
/// Usage in <c>App.cshtml</c>:
/// <code>
/// &lt;vite-react-refresh /&gt;
/// &lt;vite-input src="src/main.tsx" /&gt;
/// </code>
/// </remarks>
[HtmlTargetElement("vite-react-refresh", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ViteReactRefreshTagHelper : TagHelper
{
    private readonly IOptions<ViteOptions> _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    /// <summary>The current request's CSP nonce, overriding ViteOptions.NonceProvider.</summary>
    public string? Nonce { get; set; }

    public ViteReactRefreshTagHelper(IOptions<ViteOptions> options) : this(options, null) { }

    [ActivatorUtilitiesConstructor]
    public ViteReactRefreshTagHelper(IOptions<ViteOptions> options, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor;
    }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;
        output.TagMode = TagMode.StartTagAndEndTag;

        var opts = _options.Value;
        var hotFilePath = Path.Combine(opts.PublicDirectory, opts.HotFile);

        if (!File.Exists(hotFilePath))
            return; // production — emit nothing

        var hotUrl = File.ReadAllText(hotFilePath).Trim().TrimEnd('/');

        var refreshUrl = HtmlEncoder.Default.Encode($"{hotUrl}/@react-refresh");
        var httpContext = _httpContextAccessor?.HttpContext;
        var nonce = Nonce ?? (httpContext is null ? null : opts.NonceProvider?.Invoke(httpContext));

        // The standard React refresh preamble — mirrors what laravel-vite-plugin injects
        output.Content.AppendHtml(
            $"<script type=\"module\"{ViteTagAttributes.Security(nonce)}>\n" +
            $"  import RefreshRuntime from '{refreshUrl}';\n" +
            "  RefreshRuntime.injectIntoGlobalHook(window);\n" +
            "  window.$RefreshReg$ = () => {};\n" +
            "  window.$RefreshSig$ = () => (type) => type;\n" +
            "  window.__vite_plugin_react_preamble_installed__ = true;\n" +
            "</script>");
    }
}

internal static class ViteTagAttributes
{
    public static string Security(string? nonce, string? integrity = null)
    {
        var attributes = string.IsNullOrEmpty(nonce) ? string.Empty : $" nonce=\"{HtmlEncoder.Default.Encode(nonce)}\"";
        if (!string.IsNullOrEmpty(integrity))
            attributes += $" integrity=\"{HtmlEncoder.Default.Encode(integrity)}\" crossorigin=\"anonymous\"";
        return attributes;
    }
}
