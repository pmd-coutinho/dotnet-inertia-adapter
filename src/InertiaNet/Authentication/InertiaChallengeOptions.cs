using InertiaNet.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace InertiaNet.Authentication;

/// <summary>
/// Configures the opt-in authentication challenge/forbid handling registered by
/// <c>AddInertiaChallengeHandling()</c>.
/// </summary>
public class InertiaChallengeOptions
{
    /// <summary>
    /// Selects non-Inertia requests whose sign-in or access-denied redirects are replaced with
    /// a bare <c>401</c> (challenge) or <c>403</c> (forbid). Defaults to
    /// <see cref="DefaultApiRequestPredicate"/>. Set to null to leave every non-Inertia
    /// challenge and forbid response unchanged.
    /// </summary>
    public Func<HttpContext, bool>? ApiRequestPredicate { get; set; } = DefaultApiRequestPredicate;

    /// <summary>
    /// The default API-request heuristic. A request qualifies when it expects JSON (Laravel's
    /// <c>expectsJson()</c>: the most preferred <c>Accept</c> type is JSON, or it is an
    /// <c>X-Requested-With: XMLHttpRequest</c> request accepting any type), or when
    /// <see cref="InertiaOptions.RequestPredicate"/> excludes it and its <c>Accept</c> header does
    /// not list HTML. Browser navigations to excluded paths therefore keep their redirects.
    /// </summary>
    /// <param name="context">The current request context.</param>
    public static bool DefaultApiRequestPredicate(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = context.Request;
        var accepted = request.GetTypedHeaders().Accept
            .Where(type => type.Quality is not <= 0)
            .OrderByDescending(type => type.Quality ?? 1)
            .Select(type => type.MediaType)
            .ToList();

        var preferred = accepted.Count > 0 ? accepted[0] : StringSegment.Empty;
        var wantsJson = preferred.AsSpan().Contains("/json", StringComparison.OrdinalIgnoreCase)
            || preferred.AsSpan().Contains("+json", StringComparison.OrdinalIgnoreCase);
        var acceptsAnyType = accepted.Count == 0 || preferred.Equals("*/*", StringComparison.Ordinal)
            || preferred.Equals("*", StringComparison.Ordinal);
        var isAjax = string.Equals(request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.Ordinal);
        if (wantsJson || (isAjax && acceptsAnyType)) return true;

        var predicate = context.RequestServices?.GetService<IOptions<InertiaOptions>>()?.Value.RequestPredicate;
        if (predicate?.Invoke(context) != false) return false;

        return !accepted.Any(type => type.Equals("text/html", StringComparison.OrdinalIgnoreCase)
            || type.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase));
    }
}
