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
/// &lt;div id="app"&gt;...SSR body if available...&lt;/div&gt;
/// &lt;script type="application/json" data-page="app"&gt;{"component":...}&lt;/script&gt;
/// </code>
/// <para>Usage in App.cshtml:</para>
/// <code>
/// &lt;inertia /&gt;
/// </code>
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

        // ── Root div ──────────────────────────────────────────────────────────
        var divContent = ssr?.Body ?? string.Empty;
        output.Content.AppendHtml($"<div id=\"{HtmlEncoder.Default.Encode(Id)}\">{divContent}</div>");

        // ── Page data script ──────────────────────────────────────────────────
        if (page is not null)
        {
            var json = JsonSerializer.Serialize(page, InertiaJsonOptions.GetTagHelperOptions(_options));
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
