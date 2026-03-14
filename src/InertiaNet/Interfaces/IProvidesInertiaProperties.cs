using InertiaNet.Context;

namespace InertiaNet.Interfaces;

/// <summary>
/// Implemented by objects that provide multiple Inertia props at once.
/// Useful for grouping related shared data (e.g. auth state, flash messages)
/// into a single reusable object.
/// </summary>
public interface IProvidesInertiaProperties
{
    /// <summary>
    /// Returns key-value pairs to be merged into the Inertia props.
    /// The <paramref name="context"/> provides the component name and request.
    /// </summary>
    IEnumerable<KeyValuePair<string, object?>> ToInertiaProperties(RenderContext context);
}
