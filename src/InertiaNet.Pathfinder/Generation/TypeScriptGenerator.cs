using InertiaNet.Pathfinder.Analysis;

namespace InertiaNet.Pathfinder.Generation;

static class TypeScriptGenerator
{
    public static void Generate(List<RouteInfo> routes, List<EnumInfo> enums,
        List<PagePropsInfo> pageProps, List<ModelInfo> models, PathfinderConfig config)
    {
        var outputDir = Path.GetFullPath(config.OutputPath);

        if (config.Clean && Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);

        Directory.CreateDirectory(outputDir);

        // 1. Always write runtime file
        RuntimeFileWriter.Write(outputDir);

        if (routes.Count == 0 && enums.Count == 0 && pageProps.Count == 0 && models.Count == 0)
        {
            if (!config.Quiet)
                Console.WriteLine("Warning: No routes found.");
            BarrelFileWriter.WriteAll(outputDir);
            return;
        }

        // 2. Generate action files (grouped by controller)
        if (config.GenerateActions)
        {
            var controllerGroups = routes
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
                    models);
            }

            // Minimal API routes get their own "file"
            var minimalApiRoutes = routes
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
                    models);
            }
        }

        // 3. Generate named route files
        if (config.GenerateRoutes)
        {
            var namedRoutes = routes.Where(r => r.RouteName != null).ToList();
            if (namedRoutes.Count > 0)
                RouteFileWriter.Write(outputDir, namedRoutes, config.GenerateForms, models);
        }

        // 4. Generate enum files
        if (enums.Count > 0)
            EnumFileWriter.Write(outputDir, enums);

        // 5. Generate model type files (before props, so props can import them)
        if (models.Count > 0)
            ModelFileWriter.Write(outputDir, models);

        // 6. Generate page props type files
        if (pageProps.Count > 0)
            PropsFileWriter.Write(outputDir, pageProps, models);

        // 7. Generate barrel files
        BarrelFileWriter.WriteAll(outputDir);
    }
}
