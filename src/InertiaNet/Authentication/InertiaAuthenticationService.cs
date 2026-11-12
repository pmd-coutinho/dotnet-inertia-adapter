using System.Security.Claims;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InertiaNet.Authentication;

/// <summary>
/// Decorates the application's <see cref="IAuthenticationService"/> so every scheme's challenge
/// and forbid responses follow the Inertia protocol. The wrapped handler always runs first;
/// only its redirect is translated afterwards, so scheme events and side effects are preserved.
/// </summary>
internal sealed class InertiaAuthenticationService : IAuthenticationService
{
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

        // Handlers return to the current URL by default, which a browser cannot revisit with GET
        // after a mutation. Mirror Laravel's Redirector::guest() and return to the previous page.
        if (kind == RequestKind.Inertia && !IsSafeMethod(context.Request.Method)
            && string.IsNullOrEmpty(properties?.RedirectUri))
        {
            properties = properties?.Clone() ?? new AuthenticationProperties();
            properties.RedirectUri = GetPreviousUrl(context.Request);
        }

        await _inner.ChallengeAsync(context, scheme, properties);

        var response = context.Response;
        if (kind == RequestKind.None || response.HasStarted) return;

        // Cookie authentication answers X-Requested-With requests with 401 + Location instead
        // of a 302; both shapes are a sign-in navigation.
        if (!IsNavigation(response, StatusCodes.Status401Unauthorized)) return;

        if (kind == RequestKind.Inertia)
        {
            var location = response.Headers.Location.ToString();
            if (location.Length == 0) return;

            response.StatusCode = StatusCodes.Status409Conflict;
            response.Headers.Remove(Microsoft.Net.Http.Headers.HeaderNames.Location);
            response.Headers[HeaderNames.Location] = location;
            InertiaResponseHeaders.AppendVary(response, HeaderNames.Inertia);
        }
        else
        {
            response.StatusCode = StatusCodes.Status401Unauthorized;
            response.Headers.Remove(Microsoft.Net.Http.Headers.HeaderNames.Location);
        }
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
            response.Headers.Remove(Microsoft.Net.Http.Headers.HeaderNames.Location);
        }

        if (kind == RequestKind.Inertia)
            InertiaResponseHeaders.AppendVary(response, HeaderNames.Inertia);
    }

    private RequestKind Classify(HttpContext context)
    {
        var requestPredicate = context.RequestServices.GetService<IOptions<InertiaOptions>>()?.Value.RequestPredicate;
        if (context.Request.IsInertia() && requestPredicate?.Invoke(context) != false)
            return RequestKind.Inertia;

        return _options.Value.ApiRequestPredicate?.Invoke(context) == true ? RequestKind.Api : RequestKind.None;
    }

    private static string GetPreviousUrl(HttpRequest request)
    {
        var referer = request.Headers.Referer.ToString();
        if (referer.StartsWith('/') && !referer.StartsWith("//") && !referer.StartsWith("/\\"))
            return referer;

        // Remote handlers may redirect to this value after their callback without a local-URL
        // check, so only a same-origin Referer is reduced to its path and query.
        if (Uri.TryCreate(referer, UriKind.Absolute, out var uri)
            && uri.Scheme.Equals(request.Scheme, StringComparison.OrdinalIgnoreCase)
            && uri.Authority.Equals(request.Host.Value, StringComparison.OrdinalIgnoreCase))
            return uri.PathAndQuery;

        return request.PathBase.HasValue ? request.PathBase + "/" : "/";
    }

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
