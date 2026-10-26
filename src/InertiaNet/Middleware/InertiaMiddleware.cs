using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Props;
using InertiaNet.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;

namespace InertiaNet.Middleware;

/// <summary>
/// The core Inertia protocol middleware. Add to the pipeline via
/// <c>app.UseInertia()</c> after session / authentication middleware.
/// </summary>
/// <remarks>
/// <para>
/// Subclass this and override the virtual methods to customise behaviour
/// (share global props, change root view, handle version conflicts, etc.)
/// exactly as Laravel's <c>HandleInertiaRequests</c> middleware works.
/// </para>
/// <para>
/// Register a custom subclass:
/// <code>
/// builder.Services.AddInertia&lt;MyInertiaMiddleware&gt;();
/// app.UseInertia&lt;MyInertiaMiddleware&gt;();
/// </code>
/// </para>
/// </remarks>
public class InertiaMiddleware
{
    private readonly RequestDelegate _next;
    private static bool _tempDataWarningLogged;

    public InertiaMiddleware(RequestDelegate next) => _next = next;

    // ── Override points ───────────────────────────────────────────────────────

    /// <summary>
    /// Override to share props for every Inertia response.
    /// Called once per request, before the handler runs.
    /// </summary>
    protected virtual Task Share(HttpContext context, IInertiaService inertia)
        => Task.CompletedTask;

    /// <summary>
    /// Override to share once-props: resolved once and remembered by the client
    /// across subsequent navigations. Equivalent to Laravel's <c>shareOnce()</c>
    /// middleware method. Use <c>inertia.ShareOnce(key, callback)</c> inside.
    /// </summary>
    protected virtual Task ShareOnce(HttpContext context, IInertiaService inertia)
        => Task.CompletedTask;

    /// <summary>
    /// Override to return the current asset version string.
    /// Return <c>null</c> (default) to disable version-mismatch detection.
    /// </summary>
    protected virtual string? GetVersion(HttpContext context) => null;

    /// <summary>
    /// Override to change the root Razor view name for the current request.
    /// Return <c>null</c> (default) to use <see cref="InertiaOptions.RootView"/>.
    /// </summary>
    protected virtual string? GetRootView(HttpContext context) => null;

    /// <summary>
    /// Called when the Inertia version sent by the client differs from
    /// <see cref="GetVersion"/>. Per the Inertia v3 protocol, the server must
    /// return a <c>409 Conflict</c> response with the destination URL in the
    /// <c>X-Inertia-Location</c> header. This tells the client to perform a
    /// full-page reload to pick up the updated assets.
    /// </summary>
    protected virtual Task OnVersionChange(HttpContext context)
    {
        var url = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        context.Response.Headers[HeaderNames.Location] = url;
        var version = InertiaRequestSettings.Resolve(context,
            context.RequestServices.GetRequiredService<IOptions<InertiaOptions>>().Value).Version;
        if (version is not null) context.Response.Headers[HeaderNames.Version] = version;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called for an empty Inertia response when RedirectEmptyResponses is enabled.
    /// Redirects back to load a page object.
    /// </summary>
    protected virtual Task OnEmptyResponse(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status302Found;
        var request = context.Request;
        var back = request.PathBase + request.Path + request.QueryString;
        var referer = request.Headers.Referer.ToString();
        if (referer.StartsWith('/') && !referer.StartsWith("//") && !referer.StartsWith("/\\"))
            back = referer;
        else if (Uri.TryCreate(referer, UriKind.Absolute, out var uri)
            && uri.Scheme.Equals(request.Scheme, StringComparison.OrdinalIgnoreCase)
            && uri.Authority.Equals(request.Host.Value, StringComparison.OrdinalIgnoreCase))
            back = uri.PathAndQuery + uri.Fragment;
        context.Response.Headers.Location = string.IsNullOrEmpty(back) ? "/" : back;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when a redirect response carries a fragment in the Location header.
    /// Default: emit 409 + X-Inertia-Redirect so the client preserves the fragment.
    /// </summary>
    protected virtual Task OnRedirectWithFragment(HttpContext context, string redirectUrl)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        context.Response.Headers.Remove("Location");
        context.Response.Headers[HeaderNames.Redirect] = redirectUrl;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Override to customise CSRF/XSRF token sharing. No-ops when IAntiforgery is not registered.
    /// </summary>
    protected virtual Task ShareCsrfToken(HttpContext context, IInertiaService inertia)
    {
        var antiforgery = context.RequestServices.GetService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
        if (antiforgery is null) return Task.CompletedTask;

        // Resolve only when the prop is included in a rendered page, after the
        // handler has established the current user and before headers are sent.
        inertia.Share("xsrfToken", new Lazy<string?>(() => antiforgery.GetAndStoreTokens(context).RequestToken));

        return Task.CompletedTask;
    }

    // ── Pipeline ──────────────────────────────────────────────────────────────

    public async Task InvokeAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var options = services.GetRequiredService<IOptions<InertiaOptions>>().Value;

        if (options.RequestPredicate?.Invoke(context) == false)
        {
            await _next(context);
            return;
        }

        var requestSettings = new InertiaRequestSettings(
            rootView: GetRootView(context) ?? options.RootView,
            version: GetVersion(context) ?? options.Version?.Invoke());

        context.Items[InertiaContextKeys.RequestSettingsKey] = requestSettings;

        var inertia = services.GetRequiredService<IInertiaService>();

        InertiaResponseHeaders.AppendVary(context.Response, HeaderNames.Inertia);
        context.Response.OnStarting(() =>
        {
            InertiaResponseHeaders.AppendVary(context.Response, HeaderNames.Inertia);
            return Task.CompletedTask;
        });

        // Check before consuming any flash/errors/history state. The following
        // full-page request must still be able to receive those values.
        var isInertia = context.Request.IsInertia();
        if (isInertia && HttpMethods.IsGet(context.Request.Method) && requestSettings.Version is not null
            && (context.Request.GetInertiaVersion() ?? "") != requestSettings.Version)
        {
            await OnVersionChange(context);
            return;
        }

        // Detect prefetch requests — avoid all session/TempData side-effects
        var isPrefetch = context.Request.Headers.TryGetValue(HeaderNames.Purpose, out var purposeHeader)
            && purposeHeader.ToString().Equals("prefetch", StringComparison.OrdinalIgnoreCase);

        // An empty errors object clears old validation errors during partial reloads.
        inertia.Share("errors", new AlwaysProp(new Dictionary<string, object?>()));
        // Let the subclass share global / once props
        await Share(context, inertia);
        await ShareOnce(context, inertia);
        await ShareCsrfToken(context, inertia);

        // ── Load errors & flash from TempData (forwarded from previous redirect) ──
        // Skip on prefetch to avoid consuming TempData that should be delivered on the real visit.
        var tempDataFactory = services.GetService<ITempDataDictionaryFactory>();
        if (tempDataFactory is null && !_tempDataWarningLogged)
        {
            _tempDataWarningLogged = true;
            var logger = services.GetService<ILoggerFactory>()?.CreateLogger<InertiaMiddleware>();
            logger?.LogWarning(
                "ITempDataDictionaryFactory is not registered. Flash data and validation error " +
                "forwarding will be disabled. Register MVC TempData services with a cookie or session provider.");
        }
        if (!isPrefetch && tempDataFactory is not null)
        {
            var tempData = tempDataFactory.GetTempData(context);
            if (tempData.TryGetValue(SessionKeys.ValidationErrors, out var errorsJson) &&
                errorsJson is string errorsStr)
            {
                var errorBag = tempData.ContainsKey(SessionKeys.ErrorBag)
                    ? (string?)tempData[SessionKeys.ErrorBag]
                    : null;

                var errors = DeserializeErrors(errorsStr, options.WithAllErrors);

                if (errorBag is not null && errorBag.Length > 0)
                    inertia.Share("errors", new AlwaysProp(new Dictionary<string, object?> { [errorBag] = errors }));
                else
                    inertia.Share("errors", new AlwaysProp(errors));

                // consume — don't reflash
                tempData.Remove(SessionKeys.ValidationErrors);
                tempData.Remove(SessionKeys.ErrorBag);
                tempData.Save();
            }
        }

        // ── Restore flash data from TempData (from a previous redirect) ───────
        // Skip on prefetch — flash should only fire on the real navigation visit.
        if (!isPrefetch && tempDataFactory is not null)
        {
            var tempData = tempDataFactory.GetTempData(context);
            if (tempData.TryGetValue(SessionKeys.FlashData, out var flashJson) && flashJson is string flashStr)
            {
                var flashDict = DeserializeFlash(flashStr);
                inertia.Flash(flashDict);
                tempData.Remove(SessionKeys.FlashData);
                tempData.Save();
            }

            // Restore clearHistory / preserveFragment flags from a previous redirect
            if (tempData.TryGetValue(SessionKeys.ClearHistory, out var ch) && ch is true)
            {
                inertia.ClearHistory();
                tempData.Remove(SessionKeys.ClearHistory);
                tempData.Save();
            }
            if (tempData.TryGetValue(SessionKeys.PreserveFragment, out var pf) && pf is true)
            {
                inertia.PreserveFragment();
                tempData.Remove(SessionKeys.PreserveFragment);
                tempData.Save();
            }
        }

        await _next(context);

        // ── Post-handler fixups ───────────────────────────────────────────────

        if (!context.Response.HasStarted)
            InertiaResponseHeaders.AppendVary(context.Response, HeaderNames.Inertia);
        var canNormalize = isInertia && !context.Response.HasStarted
            && !PrecognitionValidation.IsPrecognitive(context.Request);
        if (options.RedirectEmptyResponses && canNormalize && (context.Response.StatusCode == StatusCodes.Status204NoContent
            || (context.Response.StatusCode == StatusCodes.Status200OK
                && context.Response.ContentLength is null or 0
                && (!context.Response.Body.CanSeek || context.Response.Body.Length == 0))))
            await OnEmptyResponse(context);

        // Persist flash data to TempData when redirecting so it survives the next request.
        // Skip on prefetch to avoid double-writing flash that was already restored.
        var flashData = inertia.GetFlashData();
        if (!isPrefetch && IsRedirect(context.Response.StatusCode) && tempDataFactory is not null)
        {
            var tempData = tempDataFactory.GetTempData(context);

            if (flashData.Count > 0)
                tempData[SessionKeys.FlashData] = JsonSerializer.Serialize(flashData, FlashJsonOptions);

            if (inertia.GetClearHistory())
                tempData[SessionKeys.ClearHistory] = true;

            if (inertia.GetPreserveFragment())
                tempData[SessionKeys.PreserveFragment] = true;

            tempData.Save();
        }

        if (!canNormalize) return;

        // Mutation redirects must be followed with GET; preserve deliberate
        // permanent/method-preserving redirects and safe-method semantics.
        if (context.Response.StatusCode == StatusCodes.Status302Found &&
            (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method)
                || HttpMethods.IsPatch(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method)))
        {
            context.Response.StatusCode = StatusCodes.Status303SeeOther;
        }

        // Fragment-bearing redirects → 409 + X-Inertia-Redirect (v3)
        if (IsRedirect(context.Response.StatusCode) && !isPrefetch)
        {
            var location = context.Response.Headers.Location.ToString();
            if (location.Contains('#'))
            {
                await OnRedirectWithFragment(context, location);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsRedirect(int statusCode) =>
        statusCode is StatusCodes.Status301MovedPermanently
            or StatusCodes.Status302Found
            or StatusCodes.Status303SeeOther
            or StatusCodes.Status307TemporaryRedirect
            or StatusCodes.Status308PermanentRedirect;

    private static readonly System.Text.Json.JsonSerializerOptions FlashJsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    private static Dictionary<string, object?> DeserializeFlash(string json)
    {
        var result = new Dictionary<string, object?>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? (object?)l : prop.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => prop.Value.Clone(),
                };
            }
        }
        catch (JsonException)
        {
            // Ignore malformed TempData — return empty
        }
        return result;
    }

    private static Dictionary<string, object?> DeserializeErrors(string json, bool withAllErrors)
    {
        var result = new Dictionary<string, object?>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    var messages = prop.Value.EnumerateArray()
                        .Select(e => e.GetString() ?? string.Empty)
                        .ToArray();
                    result[prop.Name] = withAllErrors ? (object)messages : messages.FirstOrDefault() ?? string.Empty;
                }
                else
                {
                    result[prop.Name] = prop.Value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed TempData — return empty
        }
        return result;
    }
}
