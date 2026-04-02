using InertiaNet.Resolution;
using InertiaNet.Ssr;
using InertiaNet.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InertiaNet.Core;

/// <summary>
/// The Inertia response result. Implements both <see cref="IActionResult"/> (MVC controllers)
/// and <see cref="IResult"/> (Minimal API endpoints) so it works in both programming models.
/// <para>
/// On the initial page load (no X-Inertia header): renders the root Razor view with the page
/// object embedded as a &lt;script type="application/json"&gt; element (or SSR body if enabled).
/// </para>
/// <para>
/// On subsequent Inertia requests (X-Inertia header present): returns the page object as JSON
/// with the X-Inertia: true response header.
/// </para>
/// </summary>
public sealed class InertiaResult : IActionResult, IResult
{
    private readonly string _component;
    private readonly Dictionary<string, object?> _sharedProps;
    private readonly IDictionary<string, object?> _pageProps;
    private readonly IInertiaService _inertiaService;
    private readonly IOptions<InertiaOptions> _options;

    // Additional view-only data (not sent to frontend)
    private readonly Dictionary<string, object?> _viewData = [];

    internal InertiaResult(
        string component,
        Dictionary<string, object?> sharedProps,
        IDictionary<string, object?> pageProps,
        IInertiaService inertiaService,
        IOptions<InertiaOptions> options)
    {
        _component = component;
        _sharedProps = sharedProps;
        _pageProps = pageProps;
        _inertiaService = inertiaService;
        _options = options;
    }

    /// <summary>Adds data that is available in the Razor view but NOT sent to the frontend.</summary>
    public InertiaResult WithViewData(string key, object? value) { _viewData[key] = value; return this; }

    // ── IActionResult (MVC) ──────────────────────────────────────────────────

    public Task ExecuteResultAsync(ActionContext context)
        => ExecuteCoreAsync(context.HttpContext, context);

    // ── IResult (Minimal API) ────────────────────────────────────────────────

    public Task ExecuteAsync(HttpContext httpContext)
        => ExecuteCoreAsync(httpContext, actionContext: null);

    // ── Core execution ───────────────────────────────────────────────────────

    private async Task ExecuteCoreAsync(HttpContext httpContext, ActionContext? actionContext)
    {
        var ct = httpContext.RequestAborted;
        var services = httpContext.RequestServices;
        var options = _options.Value;

        try
        {
            // 0. Validate component exists (if configured)
            if (options.Pages.EnsurePagesExist)
                ValidateComponentExists(_component, options.Pages);

            // 1. Run PropsResolver
            var resolver = new PropsResolver(httpContext, _component, services);
            var (resolvedProps, metadata) = await resolver.ResolveAsync(
                new Dictionary<string, object?>(_sharedProps),
                new Dictionary<string, object?>(_pageProps),
                ct);

            // Back-fill scroll metadata
            await resolver.CollectScrollMetadataAsync(
                new Dictionary<string, object?>(_pageProps), ct);

            // 1b. Event hook: OnAfterResolveProps
            var handlers = services.GetServices<IInertiaEventHandler>();
            foreach (var handler in handlers)
                await handler.OnAfterResolveProps(httpContext, resolvedProps);

            // 2. Build the page object
            var encryptHistory = _inertiaService.GetEncryptHistory() ?? options.EncryptHistory;

            var page = BuildPageObject(httpContext, resolvedProps, metadata, encryptHistory, options);

            // 2b. Event hook: OnBeforeRender
            foreach (var handler in handlers)
                await handler.OnBeforeRender(httpContext, page);

            // 3. Determine response type
            var isInertiaRequest = httpContext.Request.Headers.ContainsKey(HeaderNames.Inertia);

            // Always set Vary: X-Inertia (append, don't overwrite)
            httpContext.Response.Headers.Append("Vary", HeaderNames.Inertia);

            // Prefetch cache headers
            var isPrefetch = httpContext.Request.Headers.TryGetValue(HeaderNames.Purpose, out var purpose)
                && purpose.ToString().Equals("prefetch", StringComparison.OrdinalIgnoreCase);

            if (isPrefetch)
            {
                var maxAge = httpContext.Request.Headers.TryGetValue(HeaderNames.Prefetch, out var prefetchDuration)
                    && int.TryParse(prefetchDuration, out var clientMaxAge)
                    ? clientMaxAge
                    : options.PrefetchCacheMaxAge;

                httpContext.Response.Headers.CacheControl = $"private, max-age={maxAge}";
            }

            if (isInertiaRequest)
            {
                await WriteJsonResponseAsync(httpContext, page, ct);
            }
            else
            {
                await WriteHtmlResponseAsync(httpContext, actionContext, page, services, options, ct);
            }
        }
        catch (Exception ex) when (options.HandleExceptionsUsing is not null)
        {
            // Delegate to the configured exception handler; if it returns null, rethrow.
            // The handler receives a fresh InertiaResult which it executes independently
            // (no recursion risk since the error page itself will not throw the same exception).
            var errorResult = options.HandleExceptionsUsing(ex, httpContext);
            if (errorResult is null)
                throw;

            if (actionContext is not null)
                await errorResult.ExecuteResultAsync(actionContext);
            else
                await errorResult.ExecuteAsync(httpContext);
        }
    }

    private InertiaPage BuildPageObject(
        HttpContext httpContext,
        Dictionary<string, object?> resolvedProps,
        ResolvedMetadata metadata,
        bool encryptHistory,
        InertiaOptions options)
    {
        var flashData = _inertiaService.GetFlashData();

        return new InertiaPage
        {
            Component = _component,
            Props = resolvedProps,
            Url = GetUrl(httpContext),
            Version = options.Version?.Invoke(),
            SharedProps = options.ExposeSharedPropKeys && metadata.SharedPropKeys?.Count > 0
                ? metadata.SharedPropKeys
                : null,
            MergeProps     = metadata.MergeProps,
            PrependProps   = metadata.PrependProps,
            DeepMergeProps = metadata.DeepMergeProps,
            MatchPropsOn   = metadata.MatchPropsOn,
            DeferredProps  = metadata.DeferredProps,
            ScrollProps    = metadata.ScrollProps,
            OnceProps      = metadata.OnceProps,
            Flash          = flashData.Count > 0 ? (IReadOnlyDictionary<string, object?>)flashData : null,
            // v3: only include boolean flags when true
            ClearHistory   = _inertiaService.GetClearHistory() ? true : null,
            EncryptHistory = encryptHistory ? true : null,
            PreserveFragment = _inertiaService.GetPreserveFragment() ? true : null,
        };
    }

    private static string GetUrl(HttpContext httpContext)
    {
        var req = httpContext.Request;
        return req.PathBase + req.Path + req.QueryString;
    }

    // ── JSON response (XHR Inertia request) ──────────────────────────────────

    private async Task WriteJsonResponseAsync(
        HttpContext httpContext, InertiaPage page, CancellationToken ct)
    {
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.Headers[HeaderNames.Inertia] = "true";
        httpContext.Response.ContentType = "application/json";

        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            page,
            InertiaJsonOptions.GetOptions(_options.Value),
            ct);
    }

    // ── HTML response (initial page load) ────────────────────────────────────

    private async Task WriteHtmlResponseAsync(
        HttpContext httpContext,
        ActionContext? actionContext,
        InertiaPage page,
        IServiceProvider services,
        InertiaOptions options,
        CancellationToken ct)
    {
        // Try SSR first
        var ssrGateway = services.GetService<ISsrGateway>();
        SsrResponse? ssrResponse = null;

        if (ssrGateway is not null)
        {
            var excludedPaths = _inertiaService.GetSsrExcludedPaths();
            var path = httpContext.Request.Path.Value ?? string.Empty;
            var isExcluded = excludedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

            if (!isExcluded)
                ssrResponse = await ssrGateway.DispatchAsync(page, ct);
        }

        // Store in HttpContext.Items for tag helpers
        httpContext.Items[InertiaContextKeys.PageKey] = page;
        httpContext.Items[InertiaContextKeys.SsrResponseKey] = ssrResponse;

        if (actionContext is not null)
        {
            // MVC: render via ViewResult
            await RenderViewAsync(actionContext, options.RootView, page);
        }
        else
        {
            // Minimal API: write HTML directly via the view engine
            // For minimal APIs, we need to resolve the view engine manually
            await RenderViewMinimalAsync(httpContext, services, options.RootView, page);
        }
    }

    private async Task RenderViewAsync(ActionContext actionContext, string viewName, InertiaPage page)
    {
        var viewResult = new ViewResult
        {
            ViewName = viewName,
            ViewData = new ViewDataDictionary(
                new EmptyModelMetadataProvider(),
                actionContext.ModelState)
            {
                Model = page,
            },
        };

        foreach (var (k, v) in _viewData)
            viewResult.ViewData[k] = v;

        await viewResult.ExecuteResultAsync(actionContext);
    }

    private static void ValidateComponentExists(string component, PagesOptions pages)
    {
        // Strategy 1: Check Vite manifest keys (production / build available)
        foreach (var manifestPath in pages.ManifestPaths)
        {
            if (File.Exists(manifestPath))
            {
                var json = File.ReadAllText(manifestPath);
                using var doc = JsonDocument.Parse(json);
                foreach (var ext in pages.Extensions)
                {
                    var suffix = $"{component}.{ext}";
                    if (doc.RootElement.EnumerateObject().Any(p => p.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
                        return;
                }
            }
        }

        // Strategy 2: Check source directories directly (dev mode, no build yet)
        foreach (var dir in pages.Paths)
        {
            var componentPath = component.Replace('/', Path.DirectorySeparatorChar);
            foreach (var ext in pages.Extensions)
                if (File.Exists(Path.Combine(dir, $"{componentPath}.{ext}")))
                    return;
        }

        var searched = pages.ManifestPaths.Concat(pages.Paths).ToArray();
        var extensions = string.Join(", ", pages.Extensions.Select(e => $".{e}"));
        throw new InvalidOperationException(
            $"Inertia page component \"{component}\" not found. " +
            $"Searched: [{string.Join(", ", searched)}] with extensions: [{extensions}].");
    }

    private static async Task RenderViewMinimalAsync(
        HttpContext httpContext, IServiceProvider services, string viewName, InertiaPage page)
    {
        // For Minimal API, use IActionResultExecutor via a temporary ActionContext
        var viewResult = new ViewResult { ViewName = viewName };
        viewResult.ViewData = new ViewDataDictionary(
            new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = page,
        };

        // Create a minimal ActionContext
        var routeData = httpContext.Features.Get<IRoutingFeature>()?.RouteData ?? new RouteData();
        var actionDescriptor = new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor();
        var actionContext = new ActionContext(httpContext, routeData, actionDescriptor);

        await viewResult.ExecuteResultAsync(actionContext);
    }
}

/// <summary>HttpContext.Items keys used by InertiaNet.</summary>
internal static class InertiaContextKeys
{
    public const string PageKey = "InertiaNet.Page";
    public const string SsrResponseKey = "InertiaNet.SsrResponse";
}

/// <summary>Shared JSON serializer options for Inertia page object serialization.</summary>
internal static class InertiaJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>
    /// Returns the user-configured options if set, otherwise the built-in defaults.
    /// </summary>
    public static JsonSerializerOptions GetOptions(InertiaOptions? options)
        => options?.JsonSerializerOptions ?? Default;
}
