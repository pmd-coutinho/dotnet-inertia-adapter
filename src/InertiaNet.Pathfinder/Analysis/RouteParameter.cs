namespace InertiaNet.Pathfinder.Analysis;

record RouteParameter(
    string Name,
    string ClrTypeName,
    bool IsOptional,
    string? DefaultValue = null);
