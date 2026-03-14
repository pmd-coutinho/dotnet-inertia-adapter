using InertiaNet.Core;
using Microsoft.AspNetCore.Razor.TagHelpers;
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

    /// <summary>The Vite entry-point path as declared in <c>vite.config</c>, e.g. <c>src/main.tsx</c>.</summary>
    public string Src { get; set; } = string.Empty;

    public ViteInputTagHelper(IOptions<ViteOptions> options) => _options = options;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;
        output.TagMode = TagMode.StartTagAndEndTag;

        var opts = _options.Value;

        if (IsRunningHot(opts))
        {
            var hotUrl = ReadHotUrl(opts).TrimEnd('/');
            output.Content.AppendHtml(MakeScriptTag($"{hotUrl}/@vite/client"));
            output.Content.AppendHtml("\n    ");
            output.Content.AppendHtml(MakeScriptTag($"{hotUrl}/{Src.TrimStart('/')}"));
            return;
        }

        var manifestPath = BuildPath(opts, opts.ManifestFilename);
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException(
                $"Vite manifest not found at '{manifestPath}'. Run `npm run build` first.");

        var manifestJson = File.ReadAllText(manifestPath);
        using var doc = JsonDocument.Parse(manifestJson);

        if (!doc.RootElement.TryGetProperty(Src, out var entry))
            throw new InvalidOperationException(
                $"Entry '{Src}' not found in Vite manifest. Make sure it is listed under `build.rollupOptions.input`.");

        var file = entry.GetProperty("file").GetString()!;

        // If the entry itself is a CSS file, emit a <link> only
        if (IsCss(file))
        {
            output.Content.AppendHtml(MakeLinkTag(AssetUrl(opts, file)));
            return;
        }

        // JS entry — emit a <script type="module"> …
        output.Content.AppendHtml(MakeScriptTag(AssetUrl(opts, file)));

        // … plus any associated CSS chunks
        if (entry.TryGetProperty("css", out var cssChunks))
        {
            foreach (var chunk in cssChunks.EnumerateArray())
            {
                var cssFile = chunk.GetString();
                if (cssFile is not null)
                {
                    output.Content.AppendHtml("\n    ");
                    output.Content.AppendHtml(MakeLinkTag(AssetUrl(opts, cssFile)));
                }
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsCss(string path) =>
        Regex.IsMatch(path, @"\.(css|less|sass|scss|styl|stylus|pcss|postcss)$", RegexOptions.IgnoreCase);

    private static string MakeScriptTag(string src) =>
        $"<script type=\"module\" src=\"{HtmlEncoder.Default.Encode(src)}\"></script>";

    private static string MakeLinkTag(string href) =>
        $"<link rel=\"stylesheet\" href=\"{HtmlEncoder.Default.Encode(href)}\" />";

    private static string AssetUrl(ViteOptions opts, string file) =>
        $"/{opts.BuildDirectory.Trim('/')}/{file.TrimStart('/')}";

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

    public ViteReactRefreshTagHelper(IOptions<ViteOptions> options) => _options = options;

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

        // The standard React refresh preamble — mirrors what laravel-vite-plugin injects
        output.Content.AppendHtml(
            $"<script type=\"module\">\n" +
            $"  import RefreshRuntime from '{refreshUrl}';\n" +
            "  RefreshRuntime.injectIntoGlobalHook(window);\n" +
            "  window.$RefreshReg$ = () => {};\n" +
            "  window.$RefreshSig$ = () => (type) => type;\n" +
            "  window.__vite_plugin_react_preamble_installed__ = true;\n" +
            "</script>");
    }
}
