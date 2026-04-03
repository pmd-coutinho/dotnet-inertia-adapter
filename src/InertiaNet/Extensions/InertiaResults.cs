using InertiaNet.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaNet.Extensions;

/// <summary>
/// Minimal API helpers for producing Inertia responses without resolving <see cref="IInertiaService"/> manually.
/// </summary>
public static class InertiaResults
{
    /// <summary>
    /// Produces an Inertia response for the given component and props.
    /// </summary>
    public static IResult Inertia(string component, object? props = null)
        => new DeferredInertiaRenderResult(component, _ => ValueTask.FromResult(props));

    /// <summary>
    /// Produces an Inertia response for the given component and props dictionary.
    /// </summary>
    public static IResult Inertia(string component, IDictionary<string, object?> props)
        => new DeferredInertiaRenderResult(component, _ => ValueTask.FromResult<object?>(props));

    /// <summary>
    /// Produces an Inertia response with props computed from the current request.
    /// </summary>
    public static IResult Inertia(string component, Func<HttpContext, object?> propsFactory)
        => new DeferredInertiaRenderResult(component, context => ValueTask.FromResult(propsFactory(context)));

    /// <summary>
    /// Produces an Inertia response with async props computed from the current request.
    /// </summary>
    public static IResult Inertia(
        string component,
        Func<HttpContext, CancellationToken, Task<object?>> propsFactory)
        => new DeferredInertiaRenderResult(component, context => new ValueTask<object?>(propsFactory(context, context.RequestAborted)));

    /// <summary>
    /// Produces an Inertia-compatible external redirect.
    /// </summary>
    public static IResult Location(string url)
        => new DeferredInertiaLocationResult(url);

    private sealed class DeferredInertiaRenderResult(
        string component,
        Func<HttpContext, ValueTask<object?>> propsFactory) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var inertia = httpContext.RequestServices.GetRequiredService<IInertiaService>();
            var props = await propsFactory(httpContext);
            await inertia.Render(component, props).ExecuteAsync(httpContext);
        }
    }

    private sealed class DeferredInertiaLocationResult(string url) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            var inertia = httpContext.RequestServices.GetRequiredService<IInertiaService>();
            return inertia.Location(url).ExecuteAsync(httpContext);
        }
    }
}
