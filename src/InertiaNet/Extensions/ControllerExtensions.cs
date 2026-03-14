using InertiaNet.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaNet.Extensions;

/// <summary>
/// Extension methods on <see cref="ControllerBase"/> for rendering Inertia responses.
/// </summary>
public static class ControllerExtensions
{
    /// <summary>
    /// Renders an Inertia component with the given props (anonymous object or dictionary).
    /// </summary>
    /// <param name="controller">The MVC controller.</param>
    /// <param name="component">Frontend component name, e.g. "Users/Index".</param>
    /// <param name="props">
    ///   An anonymous object whose public properties become props,
    ///   or an <see cref="IDictionary{String,Object}"/>.
    ///   Pass null for a component with no props.
    /// </param>
    public static InertiaResult Inertia(
        this ControllerBase controller,
        string component,
        object? props = null)
    {
        var inertia = controller.HttpContext.RequestServices.GetRequiredService<IInertiaService>();
        return inertia.Render(component, props);
    }

    /// <summary>
    /// Renders an Inertia component with an explicit props dictionary.
    /// </summary>
    public static InertiaResult Inertia(
        this ControllerBase controller,
        string component,
        IDictionary<string, object?> props)
    {
        var inertia = controller.HttpContext.RequestServices.GetRequiredService<IInertiaService>();
        return inertia.Render(component, props);
    }
}
