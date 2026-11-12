using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace InertiaNet.Support;

internal static class InertiaRequestUrl
{
    /// <summary>
    /// Returns the path base, path and query string of the request the user made. While
    /// <c>UseStatusCodePagesWithReExecute</c> or <c>UseExceptionHandler</c> re-executes the pipeline
    /// on an error route, this is the original request URL rather than the error route, so the
    /// client keeps the address the user visited.
    /// </summary>
    public static string GetOriginalUrl(HttpContext context)
    {
        var request = context.Request;

        if (context.Features.Get<IStatusCodeReExecuteFeature>() is { } statusCodeReExecute)
            return statusCodeReExecute.OriginalPathBase + statusCodeReExecute.OriginalPath
                + statusCodeReExecute.OriginalQueryString;

        if (context.Features.Get<IExceptionHandlerPathFeature>() is { Path: { Length: > 0 } originalPath })
            return request.PathBase + originalPath + request.QueryString;

        return request.PathBase + request.Path + request.QueryString;
    }
}
