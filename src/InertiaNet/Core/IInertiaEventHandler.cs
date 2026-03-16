using Microsoft.AspNetCore.Http;

namespace InertiaNet.Core;

/// <summary>
/// Event handler interface for the Inertia render pipeline.
/// Register implementations via <c>services.AddInertiaEventHandler&lt;T&gt;()</c>.
/// </summary>
public interface IInertiaEventHandler
{
    /// <summary>Called after props have been resolved, before the page object is built.</summary>
    Task OnAfterResolveProps(HttpContext context, Dictionary<string, object?> props)
        => Task.CompletedTask;

    /// <summary>Called after the page object is built, before the response is written.</summary>
    Task OnBeforeRender(HttpContext context, InertiaPage page)
        => Task.CompletedTask;
}
