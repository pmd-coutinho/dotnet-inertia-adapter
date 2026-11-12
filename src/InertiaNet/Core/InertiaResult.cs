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
using Microsoft.Extensions.Caching.Memory;
using InertiaNet.Vite;
using InertiaNet.Diagnostics;
using System.Text.Json;

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
/// <para>
/// Use <see cref="WithStatus(int)"/> to render a page such as a 404 or 500 error page with an explicit
/// status. Otherwise both responses keep the status code already on the response (200 unless application
/// code, <c>UseExceptionHandler</c> or status-code-page re-execution changed it), except that the JSON
/// response falls back to 200 when that status cannot carry a page.
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

    // Explicit response status; null keeps the status already on the response
    private int? _statusCode;

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

    /// <summary>
    /// Sets the HTTP status code for both the initial HTML response and the JSON response sent to
    /// Inertia visits, e.g. <c>inertia.Render("Errors/Status", new { status = 404 }).WithStatus(404)</c>.
    /// </summary>
    /// <remarks>
    /// The Inertia client renders any response carrying the <c>X-Inertia</c> header as a page. For a
    /// status of 400 or above it first raises the cancelable <c>httpException</c> event and the
    /// visit's <c>onHttpException</c> callback.
    /// Without this call the status already on the response is kept.
    /// </remarks>
    /// <param name="statusCode">A status that can carry a page body: 2xx except 204 and 205, or 4xx/5xx.</param>
    /// <exception cref="ArgumentOutOfRangeException">The status is informational, a redirect, or bodiless.</exception>
    public InertiaResult WithStatus(int statusCode)
    {
        if (!CanCarryPage(statusCode))
            throw new ArgumentOutOfRangeException(nameof(statusCode), statusCode,
                "An Inertia page requires a 2xx (except 204 and 205), 4xx or 5xx status code.");

        _statusCode = statusCode;
        return this;
    }

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
        var requestSettings = InertiaRequestSettings.Resolve(httpContext, options);
        var initialStatusCode = httpContext.Response.StatusCode;
        var initialCacheControl = httpContext.Response.Headers.CacheControl;
        string? prefetchCacheControl = null;

        try
        {
            // 0. Validate component exists (if configured)
            if (options.Pages.EnsurePagesExist)
                ValidateComponentExists(_component, options.Pages,
                    services.GetService<IOptions<ViteOptions>>()?.Value.CacheManifest == false ? null : services.GetService<IMemoryCache>());

            // 1. Run PropsResolver
            var resolver = new PropsResolver(httpContext, _component, services);
            var (resolvedProps, metadata) = await resolver.ResolveAsync(
                PropObjectDictionary.Copy(_sharedProps),
                PropObjectDictionary.Copy(_pageProps),
                ct);

            // 1b. Event hook: OnAfterResolveProps
            var handlers = services.GetServices<IInertiaEventHandler>();
            foreach (var handler in handlers)
                await handler.OnAfterResolveProps(httpContext, resolvedProps);

            // 2. Build the page object
            var encryptHistory = _inertiaService.GetEncryptHistory() ?? options.EncryptHistory;

            var page = BuildPageObject(
                httpContext,
                resolvedProps,
                metadata,
                encryptHistory,
                options,
                requestSettings);

            // 2b. Event hook: OnBeforeRender
            foreach (var handler in handlers)
                await handler.OnBeforeRender(httpContext, page);

            // 3. Determine response type
            var isInertiaRequest = httpContext.Request.Headers.ContainsKey(HeaderNames.Inertia);

            // Always set Vary: X-Inertia (append, don't overwrite)
            InertiaResponseHeaders.AppendVary(httpContext.Response, HeaderNames.Inertia);

            var statusCode = ResolveStatusCode(httpContext.Response.StatusCode, isInertiaRequest);

            // Prefetch cache headers. Only successful pages may be cached: an error page must not be
            // served from the browser's HTTP cache after the error has gone away.
            var isPrefetch = httpContext.Request.Headers.TryGetValue(HeaderNames.Purpose, out var purpose)
                && purpose.ToString().Equals("prefetch", StringComparison.OrdinalIgnoreCase);

            if (isPrefetch && statusCode is >= 200 and < 300)
            {
                var maxAge = httpContext.Request.Headers.TryGetValue(HeaderNames.Prefetch, out var prefetchDuration)
                    && int.TryParse(prefetchDuration, out var clientMaxAge)
                    ? clientMaxAge
                    : options.PrefetchCacheMaxAge;

                prefetchCacheControl = $"private, max-age={maxAge}";
                httpContext.Response.Headers.CacheControl = prefetchCacheControl;
            }

            async Task WritePageAsync()
            {
                if (isInertiaRequest) await WriteJsonResponseAsync(httpContext, page, statusCode, ct);
                else await WriteHtmlResponseAsync(httpContext, actionContext, page, statusCode, services, requestSettings, ct);
            }
            if (httpContext.Items.TryGetValue(PropDiagnosticsTrace.ContextKey, out var trace) && trace is PropDiagnosticsTrace diagnostics)
                await diagnostics.MeasureRenderAsync(httpContext, _component, isInertiaRequest ? "json" : "html", WritePageAsync);
            else await WritePageAsync();
        }
        catch (Exception ex) when (options.HandleExceptionsUsing is not null
            && !(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            if (httpContext.Items.ContainsKey(InertiaContextKeys.ExceptionHandlerActiveKey))
                throw;

            // Delegate to the configured exception handler; if it returns null, rethrow.
            httpContext.Items[InertiaContextKeys.ExceptionHandlerActiveKey] = true;

            // Do not let this result's own status or prefetch caching leak into the handler's response.
            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = initialStatusCode;
                if (prefetchCacheControl is not null && httpContext.Response.Headers.CacheControl == prefetchCacheControl)
                    httpContext.Response.Headers.CacheControl = initialCacheControl;
            }

            try
            {
                var errorResult = options.HandleExceptionsUsing(ex, httpContext);
                if (errorResult is null)
                    throw;

                switch (errorResult)
                {
                    case IActionResult mvcResult:
                        await mvcResult.ExecuteResultAsync(actionContext ?? CreateActionContext(httpContext));
                        break;
                    case IResult httpResult:
                        await httpResult.ExecuteAsync(httpContext);
                        break;
                    default:
                        throw new InvalidOperationException(
                            "HandleExceptionsUsing must return an IResult, an IActionResult, or null.");
                }
            }
            finally
            {
                httpContext.Items.Remove(InertiaContextKeys.ExceptionHandlerActiveKey);
            }
        }
    }

    private InertiaPage BuildPageObject(
        HttpContext httpContext,
        Dictionary<string, object?> resolvedProps,
        ResolvedMetadata metadata,
        bool encryptHistory,
        InertiaOptions options,
        InertiaRequestSettings requestSettings)
    {
        var flashData = _inertiaService.GetFlashData();

        return new InertiaPage
        {
            Component = _component,
            Props = resolvedProps,
            Url = InertiaRequestUrl.GetOriginalUrl(httpContext),
            Version = requestSettings.Version,
            SharedProps = options.ExposeSharedPropKeys && metadata.SharedPropKeys?.Count > 0
                ? metadata.SharedPropKeys
                : null,
            MergeProps     = metadata.MergeProps,
            PrependProps   = metadata.PrependProps,
            DeepMergeProps = metadata.DeepMergeProps,
            MatchPropsOn   = metadata.MatchPropsOn,
            DeferredProps  = metadata.DeferredProps,
            RescuedProps   = metadata.RescuedProps,
            ScrollProps    = metadata.ScrollProps,
            OnceProps      = metadata.OnceProps,
            Flash          = flashData.Count > 0 ? (IReadOnlyDictionary<string, object?>)flashData : null,
            // v3: only include boolean flags when true
            ClearHistory   = _inertiaService.GetClearHistory() ? true : null,
            EncryptHistory = encryptHistory ? true : null,
            PreserveFragment = _inertiaService.GetPreserveFragment() ? true : null,
            PreserveBigIntegers = options.LargeIntegerSerialization == LargeIntegerSerialization.BigInt ? true : null,
        };
    }

    // JSON keeps a status set by the application or the error-handling middleware; statuses that cannot
    // carry a page body fall back to 200, the previous behaviour. HTML applies only an explicit status.
    private int ResolveStatusCode(int currentStatusCode, bool isInertiaRequest)
    {
        var statusCode = _statusCode ?? currentStatusCode;
        return !isInertiaRequest || CanCarryPage(statusCode) ? statusCode : StatusCodes.Status200OK;
    }

    private static bool CanCarryPage(int statusCode)
        => statusCode is (>= 200 and < 300) and not (StatusCodes.Status204NoContent or StatusCodes.Status205ResetContent)
            or (>= 400 and < 600);

    // ── JSON response (XHR Inertia request) ──────────────────────────────────

    private async Task WriteJsonResponseAsync(
        HttpContext httpContext, InertiaPage page, int statusCode, CancellationToken ct)
    {
        httpContext.Response.StatusCode = statusCode;
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
        int statusCode,
        IServiceProvider services,
        InertiaRequestSettings requestSettings,
        CancellationToken ct)
    {
        // Try SSR first
        var ssrGateway = services.GetService<ISsrGateway>();
        SsrResponse? ssrResponse = null;
        var options = _options.Value;

        if (ssrGateway is not null)
        {
            var excludedPaths = options.Ssr.ExcludePaths
                .Concat(_inertiaService.GetSsrExcludedPaths())
                .Where(path => !string.IsNullOrWhiteSpace(path));

            var path = httpContext.Request.Path.Value ?? string.Empty;
            var isExcluded = SsrPathMatcher.IsExcluded(path, excludedPaths);

            if (!isExcluded)
                ssrResponse = await ssrGateway.DispatchAsync(page, ct);
        }

        // Store in HttpContext.Items for tag helpers
        httpContext.Items[InertiaContextKeys.PageKey] = page;
        httpContext.Items[InertiaContextKeys.SsrResponseKey] = ssrResponse;

        if (httpContext.Response.StatusCode != statusCode)
            httpContext.Response.StatusCode = statusCode;

        if (actionContext is not null)
        {
            // MVC: render via ViewResult
            await RenderViewAsync(actionContext, requestSettings.RootView, page);
        }
        else
        {
            // Minimal API: write HTML directly via the view engine
            // For minimal APIs, we need to resolve the view engine manually
            await RenderViewMinimalAsync(httpContext, services, requestSettings.RootView, page);
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

    private static void ValidateComponentExists(string component, PagesOptions pages, IMemoryCache? cache)
    {
        // Strategy 1: Check Vite manifest keys (production / build available)
        foreach (var manifestPath in pages.ManifestPaths)
        {
            if (File.Exists(manifestPath))
            {
                var manifest = ViteManifest.Read(manifestPath, cache);
                foreach (var ext in pages.Extensions)
                {
                    var suffix = $"{component}.{ext}";
                    if (manifest.EnumerateObject().Any(p => p.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
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
        try
        {
            // For Minimal API, use IActionResultExecutor via a temporary ActionContext
            var viewResult = new ViewResult { ViewName = viewName };
            viewResult.ViewData = new ViewDataDictionary(
                new EmptyModelMetadataProvider(), new ModelStateDictionary())
            {
                Model = page,
            };

            // Create a minimal ActionContext
            var actionContext = CreateActionContext(httpContext);

            await viewResult.ExecuteResultAsync(actionContext);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                "Initial Inertia HTML rendering requires Razor view services. " +
                "Register MVC/Razor with builder.Services.AddControllersWithViews() or AddRazorPages(), " +
                "and ensure the configured root view exists.",
                ex);
        }
    }

    private static ActionContext CreateActionContext(HttpContext httpContext)
    {
        var routeData = httpContext.Features.Get<IRoutingFeature>()?.RouteData ?? new RouteData();
        var actionDescriptor = new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor();
        return new ActionContext(httpContext, routeData, actionDescriptor);
    }
}

/// <summary>HttpContext.Items keys used by InertiaNet.</summary>
internal static class InertiaContextKeys
{
    public const string PageKey = "InertiaNet.Page";
    public const string SsrResponseKey = "InertiaNet.SsrResponse";
    public const string RequestSettingsKey = "InertiaNet.RequestSettings";
    public const string ExceptionHandlerActiveKey = "InertiaNet.ExceptionHandlerActive";
}
