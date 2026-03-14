using InertiaNet.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaNet.Extensions;

/// <summary>
/// Extension methods on <see cref="IEndpointRouteBuilder"/> for registering Inertia endpoints.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps a minimal-API endpoint that returns a static Inertia component.
    /// Ideal for SPA-style routes that only forward shared props.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The route pattern, e.g. "/about".</param>
    /// <param name="component">Frontend component name, e.g. "About".</param>
    /// <param name="props">
    ///   Optional static props (anonymous object or dictionary).
    ///   These are evaluated once per request by capturing the anonymous object —
    ///   for dynamic/async props, use <see cref="IInertiaService.Render"/> in a handler directly.
    /// </param>
    public static RouteHandlerBuilder MapInertia(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        string component,
        object? props = null)
    {
        return endpoints.MapGet(pattern, (HttpContext ctx) =>
        {
            var inertia = ctx.RequestServices.GetRequiredService<IInertiaService>();
            return inertia.Render(component, props);
        });
    }

    /// <summary>
    /// Maps a minimal-API endpoint that returns a static Inertia component using an
    /// explicit props dictionary.
    /// </summary>
    public static RouteHandlerBuilder MapInertia(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        string component,
        IDictionary<string, object?> props)
    {
        return endpoints.MapGet(pattern, (HttpContext ctx) =>
        {
            var inertia = ctx.RequestServices.GetRequiredService<IInertiaService>();
            return inertia.Render(component, props);
        });
    }
}
