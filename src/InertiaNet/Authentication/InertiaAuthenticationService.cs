using System.Security.Claims;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using HttpHeaderNames = Microsoft.Net.Http.Headers.HeaderNames;

namespace InertiaNet.Authentication;

/// <summary>
/// Decorates the application's <see cref="IAuthenticationService"/> so every scheme's challenge
/// and forbid responses follow the Inertia protocol. The wrapped handler runs first and only its
/// redirect is translated afterwards, so scheme events and side effects are preserved. Inertia
/// prefetch challenges are the exception: they are answered without invoking the handler.
/// </summary>
internal sealed class InertiaAuthenticationService : IAuthenticationService
{
    private const string LocationVisitItemKey = "InertiaNet.Authentication.LocationVisit";

    private readonly IAuthenticationService _inner;
    private readonly IOptions<InertiaChallengeOptions> _options;

    public InertiaAuthenticationService(IAuthenticationService inner, IOptions<InertiaChallengeOptions> options)
    {
        _inner = inner;
        _options = options;
    }

    public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        => _inner.AuthenticateAsync(context, scheme);

    public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
        => _inner.SignInAsync(context, scheme, principal, properties);

    public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        => _inner.SignOutAsync(context, scheme, properties);

    public async Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
    {
        var kind = Classify(context);
        var request = context.Request;
        var response = context.Response;

        // A prefetch must not start a sign-in flow: a remote handler such as OpenID Connect would
        // mint a protected state and set nonce/correlation cookies (and run its redirect events) for
        // a page the user may never visit, and nothing would consume them. If the prefetched
        // response is used, the client reloads the page itself and that full-page request is
        // challenged normally. A path such as //host/x would be a protocol-relative location, so
        // it is challenged instead.
        if (kind == RequestKind.Inertia && IsPrefetch(request))
        {
            var self = request.PathBase + request.Path + request.QueryString;
            if (RefererUrl.IsLocalPath(self))
            {
                if (!response.HasStarted) SetLocationVisit(context, self);
                return;
            }
        }

        // Handlers return to the current URL by default, which a browser cannot revisit with GET
        // after a mutation. Mirror Laravel's Redirector::guest() and return to the previous page.
        if (kind == RequestKind.Inertia && !IsSafeMethod(request.Method)
            && string.IsNullOrEmpty(properties?.RedirectUri))
        {
            properties = properties?.Clone() ?? new AuthenticationProperties();
            properties.RedirectUri = GetPreviousUrl(request);
        }

        var cookies = response.HasStarted ? StringValues.Empty : response.Headers.SetCookie;

        await _inner.ChallengeAsync(context, scheme, properties);

        if (kind == RequestKind.None || response.HasStarted) return;

        // Cookie authentication answers X-Requested-With requests with 401 + Location instead
        // of a 302; both shapes are a sign-in navigation.
        var isNavigation = IsNavigation(response, StatusCodes.Status401Unauthorized);

        if (kind == RequestKind.Inertia)
        {
            var location = response.Headers.Location.ToString();
            if (isNavigation && location.Length > 0)
            {
                SetLocationVisit(context, location);
            }
            else if (response.StatusCode == StatusCodes.Status401Unauthorized && location.Length == 0
                && context.Items.ContainsKey(LocationVisitItemKey))
            {
                // A policy that lists several schemes challenges each in turn. Keep the location
                // visit of an earlier scheme when a later one (e.g. bearer) only answers 401.
                response.StatusCode = StatusCodes.Status409Conflict;
            }

            return;
        }

        if (!isNavigation) return;

        response.StatusCode = StatusCodes.Status401Unauthorized;
        response.Headers.Remove(HttpHeaderNames.Location);

        // A remote handler's correlation and nonce cookies only validate the callback of the
        // redirect it started. A 401 never leads to that callback, so drop them.
        response.Headers.SetCookie = cookies;
    }

    public async Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
    {
        var kind = Classify(context);

        await _inner.ForbidAsync(context, scheme, properties);

        var response = context.Response;
        if (kind == RequestKind.None || response.HasStarted) return;

        // As for challenges, the cookie handler answers XHR forbids with 403 + Location.
        if (IsNavigation(response, StatusCodes.Status403Forbidden))
        {
            response.StatusCode = StatusCodes.Status403Forbidden;
            response.Headers.Remove(HttpHeaderNames.Location);
        }

        if (kind == RequestKind.Inertia)
            InertiaResponseHeaders.AppendVary(response, HeaderNames.Inertia);
    }

    private static void SetLocationVisit(HttpContext context, string location)
    {
        var response = context.Response;
        response.StatusCode = StatusCodes.Status409Conflict;
        response.Headers.Remove(HttpHeaderNames.Location);
        response.Headers[HeaderNames.Location] = location;
        InertiaResponseHeaders.AppendVary(response, HeaderNames.Inertia);
        context.Items[LocationVisitItemKey] = true;
    }

    // An X-Inertia request to a path that InertiaOptions.RequestPredicate excludes is not an
    // Inertia visit, and the default API predicate skips it (the client's Accept lists HTML and the
    // Sec-Fetch-Mode rule ignores X-Inertia requests), so it keeps the scheme's own answer. That is intended: an Inertia visit to an API path is a client bug, and translating
    // it would hide the bug behind a full-page reload.
    private RequestKind Classify(HttpContext context)
    {
        var requestPredicate = context.RequestServices.GetService<IOptions<InertiaOptions>>()?.Value.RequestPredicate;
        if (context.Request.IsInertia() && requestPredicate?.Invoke(context) != false)
            return RequestKind.Inertia;

        return _options.Value.ApiRequestPredicate?.Invoke(context) == true ? RequestKind.Api : RequestKind.None;
    }

    // Remote handlers may redirect to this value after their callback without a local-URL check,
    // so only a local or same-origin Referer that reduces to a local path is used.
    private static string GetPreviousUrl(HttpRequest request)
        => RefererUrl.TryGetLocal(request, includeFragment: false, out var referer)
            ? referer
            : request.PathBase.HasValue ? request.PathBase + "/" : "/";

    private static bool IsPrefetch(HttpRequest request)
        => HttpMethods.IsGet(request.Method)
            && request.Headers[HeaderNames.Purpose].ToString().Equals("prefetch", StringComparison.OrdinalIgnoreCase);

    private static bool IsNavigation(HttpResponse response, int xhrStatusCode)
        => IsRedirect(response.StatusCode)
            || (response.StatusCode == xhrStatusCode && response.Headers.Location.Count > 0);

    private static bool IsSafeMethod(string method)
        => HttpMethods.IsGet(method) || HttpMethods.IsHead(method);

    private static bool IsRedirect(int statusCode) =>
        statusCode is StatusCodes.Status301MovedPermanently
            or StatusCodes.Status302Found
            or StatusCodes.Status303SeeOther
            or StatusCodes.Status307TemporaryRedirect
            or StatusCodes.Status308PermanentRedirect;

    private enum RequestKind
    {
        None,
        Inertia,
        Api,
    }
}
