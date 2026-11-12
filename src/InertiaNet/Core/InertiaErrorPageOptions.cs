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
    /// It must start with <c>/</c> and cannot contain route parameters or a query string.
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
    /// Optionally configures the mapped error endpoint, for example
    /// <c>endpoint => endpoint.AllowAnonymous()</c> when a fallback authorization policy would otherwise
    /// apply to the error route.
    /// </summary>
    public Action<RouteHandlerBuilder>? ConfigureEndpoint { get; set; }

    /// <summary>
    /// The default request selection. A request is excluded when any of these holds:
    /// <list type="bullet">
    /// <item><see cref="InertiaOptions.RequestPredicate"/> excludes it;</item>
    /// <item>it is a Precognition request, whose client expects a Precognition response;</item>
    /// <item>it is not an Inertia request and <see cref="InertiaChallengeOptions.DefaultApiRequestPredicate"/>
    /// classifies it as an API request (it prefers JSON, or is a script <c>fetch()</c>).</item>
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

        return context.Request.IsInertia() || !InertiaChallengeOptions.DefaultApiRequestPredicate(context);
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Component))
            throw new ArgumentException("The error page component name cannot be empty.", nameof(Component));
        if (string.IsNullOrEmpty(Path) || Path[0] != '/' || Path.IndexOfAny(['{', '}', '?', '#']) >= 0)
            throw new ArgumentException(
                "The error page path must start with '/' and cannot contain route parameters, a query string or a fragment.",
                nameof(Path));
        ArgumentNullException.ThrowIfNull(RequestPredicate, nameof(RequestPredicate));
    }
}
