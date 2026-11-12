using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace InertiaNet.Support;

internal static class InertiaResponseHeaders
{
    public static void AppendVary(HttpResponse response, string value)
    {
        var existing = response.Headers.Vary.ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (existing.Contains("*")) return;
        response.Headers.Vary = string.Join(", ", existing.Append(value).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Marks a bodiless protocol response (such as a 409 location or version-conflict response) so
    /// that <c>UseStatusCodePages</c> does not replace it with an error page.
    /// </summary>
    public static void SkipStatusCodePages(HttpContext context)
    {
        if (context.Features.Get<IStatusCodePagesFeature>() is { } statusCodePages)
            statusCodePages.Enabled = false;
    }
}
