using InertiaNet.Core;
using InertiaNet.Extensions;
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
    private const string SecFetchMode = "Sec-Fetch-Mode";

    /// <summary>
    /// Selects non-Inertia requests whose sign-in or access-denied redirects are replaced with
    /// a bare <c>401</c> (challenge) or <c>403</c> (forbid). Defaults to
    /// <see cref="DefaultApiRequestPredicate"/>. Set to null to leave every non-Inertia
    /// challenge and forbid response unchanged.
    /// </summary>
    public Func<HttpContext, bool>? ApiRequestPredicate { get; set; } = DefaultApiRequestPredicate;

    /// <summary>
    /// Whether <see cref="DefaultApiRequestPredicate"/> treats a non-Inertia request whose
    /// <c>Sec-Fetch-Mode</c> header is present and is not <c>navigate</c> (any script or subresource
    /// request such as a plain <c>fetch()</c>, not only <c>cors</c> mode) as an API request.
    /// Defaults to <see langword="true"/>. Set to <see langword="false"/> to keep the sign-in and
    /// access-denied redirects for such requests (for example when a script fetches HTML pages or
    /// fragments and relies on following the sign-in redirect) while keeping the default
    /// predicate's other rules. This is only useful when the challenge redirects to a same-origin
    /// sign-in page; with a remote scheme such as OpenID Connect the script gets a CORS failure and
    /// the scheme's correlation/nonce cookies are left behind. A custom
    /// <see cref="ApiRequestPredicate"/> that does not call <see cref="DefaultApiRequestPredicate"/>
    /// ignores this option.
    /// </summary>
    public bool TreatFetchRequestsAsApi { get; set; } = true;

    /// <summary>
    /// The default API-request heuristic. A request qualifies when any of these holds:
    /// <list type="bullet">
    /// <item>it expects JSON (Laravel's <c>expectsJson()</c>: the most preferred <c>Accept</c> type
    /// is JSON, or it is an <c>X-Requested-With: XMLHttpRequest</c> request accepting any type);</item>
    /// <item>it is not an Inertia request and the browser marks it as a script request:
    /// <c>Sec-Fetch-Mode</c> is present and is not <c>navigate</c> (for example a plain
    /// <c>fetch()</c>). Browsers send this header only to secure origins and <c>localhost</c>.
    /// This rule applies unless <see cref="TreatFetchRequestsAsApi"/> is <see langword="false"/>
    /// in the registered options;</item>
    /// <item><see cref="InertiaOptions.RequestPredicate"/> excludes it and its <c>Accept</c> header
    /// does not list HTML.</item>
    /// </list>
    /// Browser navigations, including those to excluded paths, therefore keep their redirects.
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

        var treatFetchRequestsAsApi = context.RequestServices?.GetService<IOptions<InertiaChallengeOptions>>()
            ?.Value.TreatFetchRequestsAsApi ?? true;
        var fetchMode = request.Headers[SecFetchMode].ToString();
        if (treatFetchRequestsAsApi && fetchMode.Length > 0
            && !fetchMode.Equals("navigate", StringComparison.OrdinalIgnoreCase)
            && !request.IsInertia())
            return true;

        var predicate = context.RequestServices?.GetService<IOptions<InertiaOptions>>()?.Value.RequestPredicate;
        if (predicate?.Invoke(context) != false) return false;

        return !accepted.Any(type => type.Equals("text/html", StringComparison.OrdinalIgnoreCase)
            || type.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase));
    }
}
