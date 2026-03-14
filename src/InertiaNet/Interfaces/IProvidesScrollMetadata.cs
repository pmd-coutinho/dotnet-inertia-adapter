namespace InertiaNet.Interfaces;

/// <summary>
/// Provides pagination metadata for a <see cref="Props.ScrollProp"/>,
/// enabling the Inertia frontend to manage infinite scroll state.
/// </summary>
public interface IProvidesScrollMetadata
{
    /// <summary>The query parameter name for the page number (e.g. "page").</summary>
    string? GetPageName();

    /// <summary>The previous page identifier (number or cursor), or null.</summary>
    object? GetPreviousPage();

    /// <summary>The next page identifier (number or cursor), or null.</summary>
    object? GetNextPage();

    /// <summary>The current page identifier (number or cursor).</summary>
    object? GetCurrentPage();
}
