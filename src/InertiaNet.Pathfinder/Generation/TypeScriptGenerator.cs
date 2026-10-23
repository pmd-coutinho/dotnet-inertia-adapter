using InertiaNet.Pathfinder.Analysis;

namespace InertiaNet.Pathfinder.Generation;

static class TypeScriptGenerator
{
    public static bool Generate(List<RouteInfo> routes, List<EnumInfo> enums,
        List<PagePropsInfo> pageProps, List<ModelInfo> models, PathfinderConfig config, List<PropField>? sharedProps = null)
    {
        var staging = Directory.CreateTempSubdirectory("pathfinder-stage-");
        try
        {
            WriteStaged(staging.FullName, routes, enums, pageProps, models, config, sharedProps ?? []);
            return GeneratedOutput.Synchronize(staging.FullName, config);
        }
        finally { staging.Delete(recursive: true); }
    }

    private static void WriteStaged(string outputDir, List<RouteInfo> routes, List<EnumInfo> enums,
        List<PagePropsInfo> pageProps, List<ModelInfo> models, PathfinderConfig config, List<PropField> sharedProps)
    {
        TypeMapper.ClearEnums();
        foreach (var item in enums)
            TypeMapper.RegisterEnum(item.ShortName, config.GenerateEnums ? item.ShortName : item.StringValues ? "string" : "number");
        // Disabling experimental artifacts must leave the selected modules usable.
        // Route-only consumers can opt out without dangling imports or type names.
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!config.GenerateEnums)
            foreach (var item in enums) replacements[item.ShortName] = item.StringValues ? "string" : "number";
        if (!config.GenerateModels)
            foreach (var reference in pageProps.SelectMany(page => page.Props)
                         .SelectMany(property => TypeReferenceCollector.Collect(property.TypeScriptType)))
                if (!TypeMapper.IsEnum(reference) && !replacements.ContainsKey(reference)) replacements[reference] = "unknown";
        if (replacements.Count > 0)
        {
            pageProps = pageProps.Select(page => page with { Props = page.Props.Select(property => property with
                { TypeScriptType = TypeReferenceCollector.Replace(property.TypeScriptType, replacements) }).ToList() }).ToList();
            models = models.Select(model => model with { Properties = model.Properties.Select(property => property with
                { TypeScriptType = TypeReferenceCollector.Replace(property.TypeScriptType, replacements) }).ToList() }).ToList();
        }
        if (!config.GenerateModels) models = [];
        if (!config.GenerateProps) pageProps = [];
        if (!config.GenerateProps) sharedProps = [];
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

        Directory.CreateDirectory(outputDir);

        // 1. Always write runtime file
        RuntimeFileWriter.Write(outputDir);

        if (orderedRoutes.Count == 0 && orderedEnums.Count == 0 && orderedPageProps.Count == 0 && orderedModels.Count == 0 && sharedProps.Count == 0)
        {
            BarrelFileWriter.WriteAll(outputDir);
            return;
        }

        // 2. Generate action files (grouped by controller)
        if (config.GenerateActions)
        {
            var modulePaths = ModulePaths.Actions(orderedRoutes.Select(route => route.ControllerFullName));
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
                    orderedModels, modulePaths[group.Key]);
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
                    orderedModels, modulePaths["__MinimalApi__"]);
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
        if (config.GenerateEnums && orderedEnums.Count > 0)
            EnumFileWriter.Write(outputDir, orderedEnums);

        // 5. Generate model type files (before props, so props can import them)
        if (orderedModels.Count > 0)
            ModelFileWriter.Write(outputDir, orderedModels);

        // 6. Generate page props type files
        if (orderedPageProps.Count > 0)
            PropsFileWriter.Write(outputDir, orderedPageProps, orderedModels);
        if (config.GenerateProps && (orderedPageProps.Count > 0 || sharedProps.Count > 0))
            PageMapFileWriter.Write(outputDir, orderedPageProps, sharedProps, orderedModels);

        // 7. Generate barrel files
        BarrelFileWriter.WriteAll(outputDir);
    }
}
