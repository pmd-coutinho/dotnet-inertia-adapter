using InertiaNet.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaNet.Middleware;

/// <summary>
/// Per-route middleware that enables history encryption for the current response.
/// </summary>
/// <remarks>
/// Register in the pipeline for specific routes:
/// <code>
/// app.MapGet("/dashboard", handler).AddEndpointFilter&lt;EncryptHistoryEndpointFilter&gt;();
/// // or for MVC:
/// [EncryptHistory]
/// public IActionResult Dashboard() => ...
/// </code>
/// The simplest use is to add it as route-specific middleware:
/// <code>
/// app.Map("/secure", secure =>
/// {
///     secure.UseMiddleware&lt;EncryptHistoryMiddleware&gt;();
///     secure.Run(handler);
/// });
/// </code>
/// </remarks>
public sealed class EncryptHistoryMiddleware
{
    private readonly RequestDelegate _next;

    public EncryptHistoryMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var inertia = context.RequestServices.GetRequiredService<IInertiaService>();
        inertia.EncryptHistory(true);
        await _next(context);
    }
}

/// <summary>
/// MVC action filter attribute that enables history encryption for the decorated action.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class EncryptHistoryAttribute : Attribute,
    Microsoft.AspNetCore.Mvc.Filters.IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context,
        Microsoft.AspNetCore.Mvc.Filters.ActionExecutionDelegate next)
    {
        var inertia = context.HttpContext.RequestServices.GetRequiredService<IInertiaService>();
        inertia.EncryptHistory(true);
        await next();
    }
}
