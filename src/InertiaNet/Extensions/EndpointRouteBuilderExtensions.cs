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
        => endpoints.MapGet(pattern, () => InertiaResults.Inertia(component, props));

    /// <summary>
    /// Maps a minimal-API endpoint that returns a static Inertia component using an
    /// explicit props dictionary.
    /// </summary>
    public static RouteHandlerBuilder MapInertia(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        string component,
        IDictionary<string, object?> props)
        => endpoints.MapGet(pattern, () => InertiaResults.Inertia(component, props));

    /// <summary>
    /// Maps a minimal-API endpoint that returns an Inertia component with props computed from the request.
    /// </summary>
    public static RouteHandlerBuilder MapInertia(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        string component,
        Func<HttpContext, object?> propsFactory)
        => endpoints.MapGet(pattern, (HttpContext context) => InertiaResults.Inertia(component, propsFactory(context)));

    /// <summary>
    /// Maps a minimal-API endpoint that returns an Inertia component with async props computed from the request.
    /// </summary>
    public static RouteHandlerBuilder MapInertia(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        string component,
        Func<HttpContext, CancellationToken, Task<object?>> propsFactory)
        => endpoints.MapGet(pattern, (HttpContext context) => InertiaResults.Inertia(component, propsFactory));

    /// <summary>
    /// Maps a fallback endpoint that renders a static Inertia component.
    /// Useful for frontend-driven route segments in a single-page shell.
    /// </summary>
    public static RouteHandlerBuilder MapInertiaFallback(
        this IEndpointRouteBuilder endpoints,
        string component,
        object? props = null)
        => endpoints.MapFallback(() => InertiaResults.Inertia(component, props));

    /// <summary>
    /// Maps a fallback endpoint that renders an Inertia component with props computed from the current request.
    /// </summary>
    public static RouteHandlerBuilder MapInertiaFallback(
        this IEndpointRouteBuilder endpoints,
        string component,
        Func<HttpContext, object?> propsFactory)
        => endpoints.MapFallback((HttpContext context) => InertiaResults.Inertia(component, propsFactory(context)));

    /// <summary>
    /// Maps a fallback endpoint that renders an Inertia component with async props computed from the request.
    /// </summary>
    public static RouteHandlerBuilder MapInertiaFallback(
        this IEndpointRouteBuilder endpoints,
        string component,
        Func<HttpContext, CancellationToken, Task<object?>> propsFactory)
        => endpoints.MapFallback((HttpContext _) => InertiaResults.Inertia(component, propsFactory));
}
