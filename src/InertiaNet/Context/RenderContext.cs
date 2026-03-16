using InertiaNet.Extensions;
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

    /// <summary>The Referer header value, or null if absent.</summary>
    public string? Referer => HttpContext.Request.Headers.Referer.ToString() is { Length: > 0 } r ? r : null;

    /// <summary>The partial-reload component name, or null if this is not a partial reload.</summary>
    public string? PartialComponent => Request.GetPartialComponent();

    /// <summary>True when this is a partial reload for the current component.</summary>
    public bool IsPartialReload => PartialComponent == Component;

    /// <summary>True when this request is an Inertia XHR request.</summary>
    public bool IsInertiaRequest => Request.IsInertia();
}
