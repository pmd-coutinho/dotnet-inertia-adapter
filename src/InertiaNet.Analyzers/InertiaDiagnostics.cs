using Microsoft.CodeAnalysis;

namespace InertiaNet.Analyzers;

internal static class InertiaDiagnostics
{
    public static readonly DiagnosticDescriptor InvalidComponentName = new(
        id: "INERTIA001",
        title: "Invalid Inertia component name",
        messageFormat: "Inertia component name '{0}' should be non-empty and use forward slashes without leading or trailing separators",
        category: "InertiaNet",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor JsonSerializerPolicyIgnored = new(
        id: "INERTIA002",
        title: "Inertia envelope naming is fixed",
        messageFormat: "Setting InertiaOptions.JsonSerializerOptions.{0} does not rename the Inertia protocol envelope; it only affects prop value serialization",
        category: "InertiaNet",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingPageFile = new(
        id: "INERTIA003",
        title: "Inertia page component file was not found",
        messageFormat: "Inertia component '{0}' was not found under the configured page paths: {1}",
        category: "InertiaNet",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedMinimalApiTemplate = new(
        id: "PATHFINDER001",
        title: "Pathfinder cannot resolve this Minimal API route template",
        messageFormat: "Pathfinder cannot resolve this Minimal API route template statically",
        category: "InertiaNet.Pathfinder",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedMinimalApiHandler = new(
        id: "PATHFINDER002",
        title: "Pathfinder currently supports only lambda handlers",
        messageFormat: "Pathfinder currently supports only lambda handlers for Minimal API route discovery",
        category: "InertiaNet.Pathfinder",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);
}
