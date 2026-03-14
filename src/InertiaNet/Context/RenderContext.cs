using Microsoft.AspNetCore.Http;

namespace InertiaNet.Context;

/// <summary>
/// Context passed to <see cref="Interfaces.IProvidesInertiaProperties.ToInertiaProperties"/>.
/// Provides access to the component being rendered and the current HTTP request.
/// </summary>
public sealed class RenderContext
{
    public RenderContext(string component, HttpContext httpContext)
    {
        Component = component;
        HttpContext = httpContext;
    }

    /// <summary>The Inertia component name being rendered (e.g. "Users/Index").</summary>
    public string Component { get; }

    /// <summary>The current HTTP context.</summary>
    public HttpContext HttpContext { get; }

    /// <summary>The current HTTP request.</summary>
    public HttpRequest Request => HttpContext.Request;
}
