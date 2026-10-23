using InertiaNet.Core;
using InertiaNet.Ssr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace InertiaNet.TagHelpers;

/// <summary>
/// Renders the Inertia app container.
/// </summary>
/// <remarks>
/// <para>On the initial page load renders:</para>
/// <code>
/// &lt;div id="app"&gt;&lt;/div&gt;
/// &lt;script type="application/json" data-page="app"&gt;{"component":...}&lt;/script&gt;
/// </code>
/// <para>Usage in App.cshtml:</para>
/// <code>
/// &lt;inertia /&gt;
/// </code>
/// <para>Successful official SSR bodies include their own root and page script
/// and are emitted directly. Configure the same root ID in the SSR entry point.</para>
/// </remarks>
[HtmlTargetElement("inertia", TagStructure = TagStructure.WithoutEndTag)]
public sealed class InertiaTagHelper : TagHelper
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly InertiaOptions _options;

    public InertiaTagHelper(IHttpContextAccessor httpContextAccessor, IOptions<InertiaOptions> options)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
    }

    /// <summary>The HTML id of the root div. Defaults to "app".</summary>
    public string Id { get; set; } = "app";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("InertiaTagHelper requires an active HttpContext.");

        var page = httpContext.Items[InertiaContextKeys.PageKey] as InertiaPage;
        var ssr = httpContext.Items[InertiaContextKeys.SsrResponseKey] as SsrResponse;

        output.TagName = null; // suppress the <inertia> wrapper tag
        output.TagMode = TagMode.StartTagAndEndTag;

        // Official SSR bodies already include the root element and page-data script.
        if (ssr is not null)
        {
            output.Content.AppendHtml(ssr.Body);
            return;
        }

        output.Content.AppendHtml($"<div id=\"{HtmlEncoder.Default.Encode(Id)}\"></div>");

        // ── Page data script ──────────────────────────────────────────────────
        if (page is not null)
        {
            // Custom converters may use WriteRawValue, bypassing the encoder. Protect
            // the HTML boundary after serialization too, without changing JSON values.
            var json = JsonSerializer.Serialize(page, InertiaJsonOptions.GetTagHelperOptions(_options))
                .Replace("<", "\\u003c", StringComparison.Ordinal)
                .Replace(">", "\\u003e", StringComparison.Ordinal)
                .Replace("&", "\\u0026", StringComparison.Ordinal);
            output.Content.AppendHtml(
                $"\n<script type=\"application/json\" data-page=\"{HtmlEncoder.Default.Encode(Id)}\">{json}</script>");
        }
    }
}

/// <summary>
/// Renders Inertia SSR head tags (meta, title, link elements produced by the SSR server).
/// Outputs nothing on initial client-side renders (when SSR is disabled or returns no head).
/// </summary>
/// <remarks>
/// Usage in App.cshtml:
/// <code>
/// &lt;inertia-head /&gt;
/// </code>
/// </remarks>
[HtmlTargetElement("inertia-head", TagStructure = TagStructure.WithoutEndTag)]
public sealed class InertiaHeadTagHelper : TagHelper
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public InertiaHeadTagHelper(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;
        output.TagMode = TagMode.StartTagAndEndTag;

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null) return;

        var ssr = httpContext.Items[InertiaContextKeys.SsrResponseKey] as SsrResponse;
        if (ssr?.Head is null || ssr.Head.Length == 0) return;

        output.Content.AppendHtml(ssr.Head);
    }
}
