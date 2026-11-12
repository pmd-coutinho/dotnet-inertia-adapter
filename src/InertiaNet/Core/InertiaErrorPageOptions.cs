using InertiaNet.Authentication;
using InertiaNet.Extensions;
using InertiaNet.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InertiaNet.Core;

/// <summary>
/// Configures the error pages registered by <c>UseInertiaErrorPages()</c>.
/// </summary>
public class InertiaErrorPageOptions
{
    /// <summary>
    /// The frontend component rendered for every error status, with a single <c>status</c> prop.
    /// Defaults to <c>"ErrorPage"</c>, the name the Inertia error-handling guide uses.
    /// </summary>
    public string Component { get; set; } = "ErrorPage";

    /// <summary>
    /// The path the error route is mapped under, as <c>{Path}/{status}</c>. Defaults to <c>"/error"</c>.
    /// It must start with <c>/</c>, cannot be the root path <c>"/"</c> and cannot contain route parameters or a
    /// query string. A trailing <c>/</c> is ignored.
    /// </summary>
    public string Path { get; set; } = "/error";

    /// <summary>
    /// Whether unhandled exceptions are rendered as a 500 error page through <c>UseExceptionHandler</c>.
    /// <c>UseInertiaErrorPages()</c> sets this to false in the Development environment before running the
    /// configuration delegate, so the developer exception page keeps showing exception details there.
    /// When false only empty 4xx/5xx responses are replaced, through status code pages.
    /// </summary>
    public bool HandleExceptions { get; set; } = true;

    /// <summary>
    /// Selects the requests whose errors are rendered as Inertia error pages. It is evaluated on the original
    /// request, before the pipeline is re-executed on the error route; other requests keep ASP.NET Core's
    /// plain error responses. Defaults to <see cref="DefaultRequestPredicate"/>.
    /// </summary>
    public Func<HttpContext, bool> RequestPredicate { get; set; } = DefaultRequestPredicate;

    /// <summary>
    /// Whether the error route is mapped with <c>AllowAnonymous()</c>. Defaults to true, so a fallback
    /// authorization policy does not challenge the re-executed request: without it an anonymous user's 404 on a
    /// public page would be redirected to sign in with the error route as the return URL. The page only exposes
    /// the status, as Laravel renders its exception pages outside route middleware. Set to false to let
    /// authorization apply to the error route, for example through <see cref="ConfigureEndpoint"/>.
    /// </summary>
    public bool AllowAnonymous { get; set; } = true;

    /// <summary>
    /// Optionally configures the mapped error endpoint, for example with rate limiting or other endpoint
    /// conventions. It runs after <see cref="AllowAnonymous"/> is applied, so an authorization requirement added
    /// here (<c>RequireAuthorization()</c>) only takes effect when <see cref="AllowAnonymous"/> is false.
    /// </summary>
    public Action<RouteHandlerBuilder>? ConfigureEndpoint { get; set; }

    /// <summary>
    /// The default request selection. A request is excluded when any of these holds:
    /// <list type="bullet">
    /// <item><see cref="InertiaOptions.RequestPredicate"/> excludes it;</item>
    /// <item>it is a Precognition request, whose client expects a Precognition response;</item>
    /// <item>it is not an Inertia request and the configured <see cref="InertiaChallengeOptions.ApiRequestPredicate"/>
    /// classifies it as an API request. That is <see cref="InertiaChallengeOptions.DefaultApiRequestPredicate"/>
    /// (it prefers JSON, or is a script <c>fetch()</c>) unless <c>AddInertiaChallengeHandling</c> or another
    /// <see cref="InertiaChallengeOptions"/> configuration replaced it; when it is set to null, which only turns
    /// off the challenge translation, the default predicate is used here.</item>
    /// </list>
    /// Browser navigations and Inertia visits to included paths are rendered.
    /// </summary>
    /// <param name="context">The current request context.</param>
    public static bool DefaultRequestPredicate(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var predicate = context.RequestServices?.GetService<IOptions<InertiaOptions>>()?.Value.RequestPredicate;
        if (predicate?.Invoke(context) == false) return false;
        if (PrecognitionValidation.IsPrecognitive(context.Request)) return false;

        if (context.Request.IsInertia()) return true;

        var isApiRequest = context.RequestServices?.GetService<IOptions<InertiaChallengeOptions>>()?.Value.ApiRequestPredicate
            ?? InertiaChallengeOptions.DefaultApiRequestPredicate;
        return !isApiRequest(context);
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Component))
            throw new ArgumentException("The error page component name cannot be empty.", nameof(Component));
        if (string.IsNullOrEmpty(Path) || Path[0] != '/' || Path.IndexOfAny(['{', '}', '?', '#']) >= 0)
            throw new ArgumentException(
                "The error page path must start with '/' and cannot contain route parameters, a query string or a fragment.",
                nameof(Path));
        // "/" would map /{status} and turn top-level URLs such as /404 into error pages.
        if (Path.TrimEnd('/').Length == 0)
            throw new ArgumentException("The error page path cannot be the root path.", nameof(Path));
        ArgumentNullException.ThrowIfNull(RequestPredicate, nameof(RequestPredicate));
    }
}
