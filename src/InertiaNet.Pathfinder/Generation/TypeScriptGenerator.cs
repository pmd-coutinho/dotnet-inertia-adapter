using InertiaNet.Pathfinder.Analysis;

namespace InertiaNet.Pathfinder.Generation;

static class TypeScriptGenerator
{
    public static void Generate(List<RouteInfo> routes, List<EnumInfo> enums,
        List<PagePropsInfo> pageProps, List<ModelInfo> models, PathfinderConfig config)
    {
        var outputDir = Path.GetFullPath(config.OutputPath);
        var orderedRoutes = routes
            .OrderBy(route => route.ControllerFullName, StringComparer.Ordinal)
            .ThenBy(route => route.RouteName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(route => route.ActionName, StringComparer.Ordinal)
            .ThenBy(route => route.UrlTemplate, StringComparer.Ordinal)
            .ThenBy(route => string.Join(',', route.HttpMethods), StringComparer.Ordinal)
            .ToList();
        var orderedEnums = enums
            .OrderBy(enumInfo => enumInfo.FullName, StringComparer.Ordinal)
            .ThenBy(enumInfo => enumInfo.ShortName, StringComparer.Ordinal)
            .ToList();
        var orderedPageProps = pageProps
            .OrderBy(page => page.ComponentName, StringComparer.Ordinal)
            .ToList();
        var orderedModels = models
            .OrderBy(model => model.FullName, StringComparer.Ordinal)
            .ThenBy(model => model.ShortName, StringComparer.Ordinal)
            .ToList();

        if (config.Clean && Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);

        Directory.CreateDirectory(outputDir);

        // 1. Always write runtime file
        RuntimeFileWriter.Write(outputDir);

        if (orderedRoutes.Count == 0 && orderedEnums.Count == 0 && orderedPageProps.Count == 0 && orderedModels.Count == 0)
        {
            if (!config.Quiet)
                Console.WriteLine("Warning: No routes found.");
            BarrelFileWriter.WriteAll(outputDir);
            return;
        }

        // 2. Generate action files (grouped by controller)
        if (config.GenerateActions)
        {
            var controllerGroups = orderedRoutes
                .Where(r => r.ControllerFullName != "__MinimalApi__")
                .GroupBy(r => r.ControllerFullName);

            foreach (var group in controllerGroups)
            {
                ActionFileWriter.Write(
                    outputDir,
                    group.Key,
                    group.First().ControllerShortName,
                    group.ToList(),
                    config.GenerateForms,
                    orderedModels);
            }

            // Minimal API routes get their own "file"
            var minimalApiRoutes = orderedRoutes
                .Where(r => r.ControllerFullName == "__MinimalApi__")
                .ToList();

            if (minimalApiRoutes.Count > 0)
            {
                ActionFileWriter.Write(
                    outputDir,
                    "MinimalApi",
                    "MinimalApi",
                    minimalApiRoutes,
                    config.GenerateForms,
                    orderedModels);
            }
        }

        // 3. Generate named route files
        if (config.GenerateRoutes)
        {
            var namedRoutes = orderedRoutes.Where(r => r.RouteName != null).ToList();
            if (namedRoutes.Count > 0)
                RouteFileWriter.Write(outputDir, namedRoutes, config.GenerateForms, orderedModels);
        }

        // 4. Generate enum files
        if (orderedEnums.Count > 0)
            EnumFileWriter.Write(outputDir, orderedEnums);

        // 5. Generate model type files (before props, so props can import them)
        if (orderedModels.Count > 0)
            ModelFileWriter.Write(outputDir, orderedModels);

        // 6. Generate page props type files
        if (orderedPageProps.Count > 0)
            PropsFileWriter.Write(outputDir, orderedPageProps, orderedModels);

        // 7. Generate barrel files
        BarrelFileWriter.WriteAll(outputDir);
    }
}
