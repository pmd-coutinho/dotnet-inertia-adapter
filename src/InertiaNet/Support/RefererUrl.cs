using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;

namespace InertiaNet.Support;

/// <summary>
/// Reduces the request's <c>Referer</c> to a local URL that is safe to redirect to.
/// </summary>
internal static class RefererUrl
{
    /// <summary>
    /// Returns the <c>Referer</c> as a local path when it is a safe relative URL, or an absolute
    /// URL with the request's scheme and host. Same-origin URLs are reduced to their path and query,
    /// plus the fragment when <paramref name="includeFragment"/> is set.
    /// </summary>
    /// <remarks>
    /// The result is rejected unless it is a local path in the sense of ASP.NET Core's
    /// <c>IsLocalUrl</c>: it must start with a single <c>/</c> that is not followed by <c>/</c> or
    /// <c>\</c>, and must not contain control characters. A same-origin absolute URL is checked
    /// after reduction because a path such as <c>//evil.example/x</c> is a protocol-relative URL.
    /// </remarks>
    public static bool TryGetLocal(HttpRequest request, bool includeFragment, [NotNullWhen(true)] out string? url)
    {
        var referer = request.Headers.Referer.ToString();
        if (referer.StartsWith('/'))
        {
            url = referer;
        }
        else if (Uri.TryCreate(referer, UriKind.Absolute, out var uri)
            && uri.Scheme.Equals(request.Scheme, StringComparison.OrdinalIgnoreCase)
            && uri.Authority.Equals(request.Host.Value, StringComparison.OrdinalIgnoreCase))
        {
            url = includeFragment ? uri.PathAndQuery + uri.Fragment : uri.PathAndQuery;
        }
        else
        {
            url = null;
            return false;
        }

        if (IsLocalPath(url)) return true;

        url = null;
        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="path"/> is a local path in the sense of ASP.NET Core's
    /// <c>IsLocalUrl</c>: a single leading <c>/</c> not followed by <c>/</c> or <c>\</c>, and no
    /// control characters.
    /// </summary>
    public static bool IsLocalPath(string path)
    {
        if (path.Length == 0 || path[0] != '/') return false;
        if (path.Length == 1) return true;
        if (path[1] is '/' or '\\') return false;

        foreach (var character in path.AsSpan(1))
            if (char.IsControl(character)) return false;

        return true;
    }
}
