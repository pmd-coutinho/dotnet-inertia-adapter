using InertiaNet.Middleware;
using Microsoft.AspNetCore.Builder;

namespace InertiaNet.Extensions;

/// <summary>
/// Extension methods on <see cref="IApplicationBuilder"/> for registering the Inertia middleware.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Registers the default <see cref="InertiaMiddleware"/> in the pipeline.
    /// </summary>
    public static IApplicationBuilder UseInertia(this IApplicationBuilder app)
        => app.UseMiddleware<InertiaMiddleware>();

    /// <summary>
    /// Registers a custom subclass of <see cref="InertiaMiddleware"/> in the pipeline.
    /// </summary>
    public static IApplicationBuilder UseInertia<TMiddleware>(this IApplicationBuilder app)
        where TMiddleware : InertiaMiddleware
        => app.UseMiddleware<TMiddleware>();
}
