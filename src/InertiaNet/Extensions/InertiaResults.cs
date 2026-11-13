using InertiaNet.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaNet.Extensions;

/// <summary>
/// Minimal API helpers for producing Inertia responses without resolving <see cref="IInertiaService"/> manually.
/// </summary>
/// <remarks>
/// Each overload has a variant taking a <c>statusCode</c>, which renders the page with
/// <see cref="InertiaResult.WithStatus(int)"/>, e.g. <c>InertiaResults.Inertia("Errors/Status", new { status = 404 }, 404)</c>.
/// Without it the page keeps the status already on the response.
/// </remarks>
public static class InertiaResults
{
    /// <summary>Produces a page using the component's declared props contract.</summary>
    public static IResult Inertia<TProps>(InertiaComponent<TProps> component, TProps props)
        => Inertia(component.Name, (object?)props);

    /// <summary>Produces a page using the component's declared props contract, with an explicit status code.</summary>
    /// <param name="component">The typed component.</param>
    /// <param name="props">The page props.</param>
    /// <param name="statusCode">A status that can carry a page body: 2xx except 204 and 205, or 4xx/5xx.</param>
    /// <exception cref="ArgumentOutOfRangeException">The status is informational, a redirect, or bodiless.</exception>
    public static IResult Inertia<TProps>(InertiaComponent<TProps> component, TProps props, int statusCode)
        => Inertia(component.Name, (object?)props, statusCode);

    /// <summary>
    /// Produces an Inertia response for the given component and props.
    /// </summary>
    public static IResult Inertia(string component, object? props = null)
        => new DeferredInertiaRenderResult(component, _ => ValueTask.FromResult(props));

    /// <summary>
    /// Produces an Inertia response for the given component and props with an explicit status code, e.g. an error page.
    /// </summary>
    /// <param name="component">The frontend component name.</param>
    /// <param name="props">The page props.</param>
    /// <param name="statusCode">A status that can carry a page body: 2xx except 204 and 205, or 4xx/5xx.</param>
    /// <exception cref="ArgumentOutOfRangeException">The status is informational, a redirect, or bodiless.</exception>
    public static IResult Inertia(string component, object? props, int statusCode)
        => new DeferredInertiaRenderResult(component, _ => ValueTask.FromResult(props), ValidStatus(statusCode));

    /// <summary>
    /// Produces an Inertia response for the given component and props dictionary.
    /// </summary>
    public static IResult Inertia(string component, IDictionary<string, object?> props)
        => new DeferredInertiaRenderResult(component, _ => ValueTask.FromResult<object?>(props));

    /// <summary>
    /// Produces an Inertia response for the given component and props dictionary with an explicit status code.
    /// </summary>
    /// <param name="component">The frontend component name.</param>
    /// <param name="props">The page props.</param>
    /// <param name="statusCode">A status that can carry a page body: 2xx except 204 and 205, or 4xx/5xx.</param>
    /// <exception cref="ArgumentOutOfRangeException">The status is informational, a redirect, or bodiless.</exception>
    public static IResult Inertia(string component, IDictionary<string, object?> props, int statusCode)
        => new DeferredInertiaRenderResult(component, _ => ValueTask.FromResult<object?>(props), ValidStatus(statusCode));

    /// <summary>
    /// Produces an Inertia response with props computed from the current request.
    /// </summary>
    public static IResult Inertia(string component, Func<HttpContext, object?> propsFactory)
        => new DeferredInertiaRenderResult(component, context => ValueTask.FromResult(propsFactory(context)));

    /// <summary>
    /// Produces an Inertia response with props computed from the current request and an explicit status code.
    /// </summary>
    /// <param name="component">The frontend component name.</param>
    /// <param name="propsFactory">Computes the page props from the current request.</param>
    /// <param name="statusCode">A status that can carry a page body: 2xx except 204 and 205, or 4xx/5xx.</param>
    /// <exception cref="ArgumentOutOfRangeException">The status is informational, a redirect, or bodiless.</exception>
    public static IResult Inertia(string component, Func<HttpContext, object?> propsFactory, int statusCode)
        => new DeferredInertiaRenderResult(component, context => ValueTask.FromResult(propsFactory(context)), ValidStatus(statusCode));

    /// <summary>
    /// Produces an Inertia response with async props computed from the current request.
    /// </summary>
    public static IResult Inertia(
        string component,
        Func<HttpContext, CancellationToken, Task<object?>> propsFactory)
        => new DeferredInertiaRenderResult(component, context => new ValueTask<object?>(propsFactory(context, context.RequestAborted)));

    /// <summary>
    /// Produces an Inertia response with async props computed from the current request and an explicit status code.
    /// </summary>
    /// <param name="component">The frontend component name.</param>
    /// <param name="propsFactory">Computes the page props from the current request.</param>
    /// <param name="statusCode">A status that can carry a page body: 2xx except 204 and 205, or 4xx/5xx.</param>
    /// <exception cref="ArgumentOutOfRangeException">The status is informational, a redirect, or bodiless.</exception>
    public static IResult Inertia(
        string component,
        Func<HttpContext, CancellationToken, Task<object?>> propsFactory,
        int statusCode)
        => new DeferredInertiaRenderResult(component, context => new ValueTask<object?>(propsFactory(context, context.RequestAborted)),
            ValidStatus(statusCode));

    /// <summary>
    /// Produces an Inertia-compatible external redirect.
    /// </summary>
    public static IResult Location(string url)
        => new DeferredInertiaLocationResult(url);

    // Validated when the result is created, so a bad status fails at the call site rather than mid-response.
    private static int ValidStatus(int statusCode)
    {
        InertiaResult.ThrowIfCannotCarryPage(statusCode);
        return statusCode;
    }

    private sealed class DeferredInertiaRenderResult(
        string component,
        Func<HttpContext, ValueTask<object?>> propsFactory,
        int? statusCode = null) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var inertia = httpContext.RequestServices.GetRequiredService<IInertiaService>();
            var props = await propsFactory(httpContext);
            var result = inertia.Render(component, props);
            if (statusCode is { } status) result.WithStatus(status);
            await result.ExecuteAsync(httpContext);
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
