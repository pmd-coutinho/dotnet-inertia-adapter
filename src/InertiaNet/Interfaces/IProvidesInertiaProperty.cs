using InertiaNet.Context;

namespace InertiaNet.Interfaces;

/// <summary>
/// Implemented by objects that resolve themselves to a single Inertia prop value.
/// Allows custom types to control their own serialization or lazy-resolve sibling props.
/// </summary>
public interface IProvidesInertiaProperty
{
    /// <summary>
    /// Returns the value for this prop.
    /// The <paramref name="context"/> provides the prop key, sibling props, and request.
    /// </summary>
    object? ToInertiaProperty(PropertyContext context);
}
