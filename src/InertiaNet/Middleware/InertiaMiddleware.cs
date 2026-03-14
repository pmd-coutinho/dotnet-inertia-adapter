using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
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
    /// Override to change the root Razor view name (default: "App").
    /// </summary>
    protected virtual string GetRootView(HttpContext context) => "App";

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
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when an Inertia request receives an empty (204) response.
    /// Default: rewrite to 200 so the Inertia client doesn't navigate away.
    /// </summary>
    protected virtual Task OnEmptyResponse(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when a redirect response carries a fragment in the Location header.
    /// Default: emit 409 + X-Inertia-Redirect so the client preserves the fragment.
    /// </summary>
    protected virtual Task OnRedirectWithFragment(HttpContext context, string redirectUrl)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        context.Response.Headers[HeaderNames.Redirect] = redirectUrl;
        return Task.CompletedTask;
    }

    // ── Pipeline ──────────────────────────────────────────────────────────────

    public async Task InvokeAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var options = services.GetRequiredService<IOptions<InertiaOptions>>().Value;

        // Apply RootView / Version overrides via options (allow subclass to win)
        var version = GetVersion(context) ?? options.Version?.Invoke();
        var rootView = GetRootView(context);
        if (rootView != "App") options.RootView = rootView;

        var inertia = services.GetRequiredService<IInertiaService>();

        // Detect prefetch requests — avoid all session/TempData side-effects
        var isPrefetch = context.Request.Headers.TryGetValue(HeaderNames.Purpose, out var purposeHeader)
            && purposeHeader.ToString().Equals("prefetch", StringComparison.OrdinalIgnoreCase);

        // Let the subclass share global / once props
        await Share(context, inertia);
        await ShareOnce(context, inertia);

        // ── Load errors & flash from TempData (forwarded from previous redirect) ──
        // Skip on prefetch to avoid consuming TempData that should be delivered on the real visit.
        var tempDataFactory = services.GetService<ITempDataDictionaryFactory>();
        if (!isPrefetch && tempDataFactory is not null)
        {
            var tempData = tempDataFactory.GetTempData(context);
            tempData.Keep(); // reflash by default — consumed props are removed below

            if (tempData.TryGetValue(SessionKeys.ValidationErrors, out var errorsJson) &&
                errorsJson is string errorsStr)
            {
                var errorBag = tempData.ContainsKey(SessionKeys.ErrorBag)
                    ? (string?)tempData[SessionKeys.ErrorBag]
                    : null;

                var errors = DeserializeErrors(errorsStr, options.WithAllErrors);

                if (errorBag is not null && errorBag.Length > 0)
                    inertia.Share("errors", new Dictionary<string, object?> { [errorBag] = errors });
                else
                    inertia.Share("errors", errors);

                // consume — don't reflash
                tempData.Remove(SessionKeys.ValidationErrors);
                tempData.Remove(SessionKeys.ErrorBag);
                tempData.Save();
            }
        }

        bool isInertia = context.Request.IsInertia();

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
        }

        // ── Version check (Inertia-only, GET requests) ────────────────────────
        if (isInertia && context.Request.Method == HttpMethods.Get && version is not null)
        {
            var clientVersion = context.Request.GetInertiaVersion();
            if (clientVersion is not null && clientVersion != version)
            {
                await OnVersionChange(context);
                return;
            }
        }

        // Always add Vary: X-Inertia
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey("Vary"))
                context.Response.Headers.Vary = HeaderNames.Inertia;
            return Task.CompletedTask;
        });

        await _next(context);

        // ── Post-handler fixups ───────────────────────────────────────────────

        // Persist flash data to TempData when redirecting so it survives the next request.
        // Skip on prefetch to avoid double-writing flash that was already restored.
        var flashData = inertia.GetFlashData();
        if (!isPrefetch && flashData.Count > 0 && IsRedirect(context.Response.StatusCode) && tempDataFactory is not null)
        {
            var tempData = tempDataFactory.GetTempData(context);
            tempData[SessionKeys.FlashData] = JsonSerializer.Serialize(flashData, FlashJsonOptions);
            tempData.Save();
        }

        if (!isInertia) return;

        // Inertia requires that redirects from non-GET use 303 instead of 302/301
        if (IsRedirect(context.Response.StatusCode) &&
            context.Request.Method != HttpMethods.Get)
        {
            context.Response.StatusCode = StatusCodes.Status303SeeOther;
        }

        // Empty responses → 200 (Inertia protocol)
        if (context.Response.StatusCode == StatusCodes.Status204NoContent)
        {
            await OnEmptyResponse(context);
            return;
        }

        // Fragment-bearing redirects → 409 + X-Inertia-Redirect (v3)
        if (IsRedirect(context.Response.StatusCode))
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
                    _ => prop.Value.GetRawText(),
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
