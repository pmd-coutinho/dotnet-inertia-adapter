namespace InertiaNet.Interfaces;

/// <summary>
/// Marks a prop as deferrable — excluded from the initial page load and
/// fetched asynchronously by the frontend after the first render.
/// </summary>
public interface IDeferrable
{
    /// <summary>Returns true if this prop should be deferred.</summary>
    bool ShouldDefer();

    /// <summary>
    /// The deferred prop group name. Props in the same group are fetched
    /// together in a single request. Defaults to "default".
    /// </summary>
    string Group();
}
