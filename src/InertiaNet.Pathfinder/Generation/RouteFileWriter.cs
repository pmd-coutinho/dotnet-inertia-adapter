using System.Text;
using InertiaNet.Pathfinder.Analysis;

namespace InertiaNet.Pathfinder.Generation;

static class RouteFileWriter
{
    public static void Write(string outputDir, List<RouteInfo> allRoutes, bool generateForms = true,
        List<ModelInfo>? models = null)
    {
        allRoutes = allRoutes
            .OrderBy(route => route.RouteName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(route => route.ActionName, StringComparer.Ordinal)
            .ThenBy(route => route.UrlTemplate, StringComparer.Ordinal)
            .ThenBy(route => string.Join(',', route.HttpMethods), StringComparer.Ordinal)
            .ToList();

        var paths = ModulePaths.RouteDirectories(allRoutes.Select(route => route.RouteName!).OfType<string>());
        // Group routes by their name segments
        var namedRoutes = allRoutes
            .Where(r => r.RouteName != null)
            .GroupBy(r =>
            {
                var segments = r.RouteName!.Split('.');
                return segments.Length > 1
                    ? string.Join(".", segments[..^1])
                    : "";
            });

        foreach (var group in namedRoutes.OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var modulePath = paths[group.Key];
            var dirPath = string.IsNullOrEmpty(modulePath)
                ? GeneratedOutput.PathUnder(outputDir, "routes")
                : GeneratedOutput.PathUnder(outputDir, "routes", modulePath);

            Directory.CreateDirectory(dirPath);

            var segmentCount = string.IsNullOrEmpty(modulePath)
                ? 1
                : 1 + modulePath.Count(c => c == '/') + 1;
            var importPrefix = string.Concat(Enumerable.Repeat("../", segmentCount));

            var routeList = group
                .OrderBy(route => route.RouteName ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(route => route.UrlTemplate, StringComparer.Ordinal)
                .ThenBy(route => route.ActionName, StringComparer.Ordinal)
                .ThenBy(route => string.Join(',', route.HttpMethods), StringComparer.Ordinal)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"import {{ type RouteDefinition, type RouteDefinitionInfo, type RouteQueryOptions, type FormDefinition, queryParams, applyUrlDefaults, normalizeParameters, buildUrl }} from '{importPrefix}index'");

            // Collect model imports for body types
            var modelImports = RouteFunctionWriter.ModelImports(routeList, models).ToArray();
            var enumImports = RouteFunctionWriter.EnumImports(routeList).ToArray();
            foreach (var modelName in modelImports.OrderBy(name => name, StringComparer.Ordinal))
            {
                sb.AppendLine($"import type {{ {modelName} }} from '{importPrefix}models/{modelName}'");
            }
            foreach (var enumName in enumImports)
                sb.AppendLine($"import type {{ {enumName} }} from '{importPrefix}enums/{enumName}'");

            sb.AppendLine();

            var exportNames = new List<string>();

            // Resolve naming conflicts within the group
            var usedNames = NameUtils.RuntimeNames();
            usedNames.UnionWith(modelImports);
            usedNames.UnionWith(enumImports);
            var nameMap = ResolveNameConflicts(routeList, usedNames);
            usedNames.UnionWith(nameMap.Values);

            foreach (var route in routeList)
            {
                var exportName = nameMap[route];
                exportNames.Add(exportName);

                // Body type export — use full route name for alias (posts.store → PostsStorePayload)
                var preferredContext = string.Join("", route.RouteName!.Split('.').Select(s => s.Length == 0 ? "_" : char.ToUpperInvariant(s[0]) + s[1..]));
                var contextName = NameUtils.Unique(preferredContext, usedNames, "Payload", "Query", "Form");
                RouteFunctionWriter.Write(sb, route, exportName, contextName, generateForms, models);
            }

            File.WriteAllText(Path.Combine(dirPath, "index.ts"), sb.ToString());
        }
    }

    private static Dictionary<RouteInfo, string> ResolveNameConflicts(List<RouteInfo> routes, IEnumerable<string> reserved)
    {
        var used = new HashSet<string>(reserved, StringComparer.Ordinal);
        return routes.ToDictionary(route => route, route => NameUtils.Unique(NameUtils.SafeJsName(route.RouteName!.Split('.').Last()), used));
    }
}
