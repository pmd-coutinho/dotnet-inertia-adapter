using InertiaNet.Interfaces;

namespace InertiaNet.Props;

/// <summary>
/// Provides pagination metadata for a <see cref="ScrollProp"/>.
/// Mirrors the structure used by Laravel's paginator auto-detection.
/// </summary>
public sealed class ScrollMetadata : IProvidesScrollMetadata
{
    public string? PageName { get; init; } = "page";
    public object? PreviousPage { get; init; }
    public object? NextPage { get; init; }
    public object? CurrentPage { get; init; }

    public string? GetPageName() => PageName;
    public object? GetPreviousPage() => PreviousPage;
    public object? GetNextPage() => NextPage;
    public object? GetCurrentPage() => CurrentPage;

    /// <summary>
    /// Converts this metadata to the dictionary format included in the Inertia page object.
    /// </summary>
    public Dictionary<string, object?> ToInertiaFormat() => new()
    {
        ["pageName"]     = PageName,
        ["previousPage"] = PreviousPage,
        ["nextPage"]     = NextPage,
        ["currentPage"]  = CurrentPage,
    };
}
