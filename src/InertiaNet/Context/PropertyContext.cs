using Microsoft.AspNetCore.Http;

namespace InertiaNet.Context;

/// <summary>
/// Context passed to <see cref="Interfaces.IProvidesInertiaProperty.ToInertiaProperty"/>.
/// Provides access to the prop key, sibling props, and the current HTTP request.
/// </summary>
public sealed class PropertyContext
{
    public PropertyContext(string key, IReadOnlyDictionary<string, object?> props, HttpContext httpContext)
    {
        Key = key;
        Props = props;
        HttpContext = httpContext;
    }

    /// <summary>The prop key this object is being resolved for.</summary>
    public string Key { get; }

    /// <summary>
    /// All sibling props (shared + page) at the current resolution level.
    /// Allows referencing adjacent prop values.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Props { get; }

    /// <summary>The current HTTP context.</summary>
    public HttpContext HttpContext { get; }

    /// <summary>The current HTTP request.</summary>
    public HttpRequest Request => HttpContext.Request;
}
