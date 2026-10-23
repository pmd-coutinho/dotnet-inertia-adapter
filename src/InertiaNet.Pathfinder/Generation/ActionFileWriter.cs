using System.Text;
using InertiaNet.Pathfinder.Analysis;

namespace InertiaNet.Pathfinder.Generation;

static class ActionFileWriter
{
    public static void Write(string outputDir, string controllerFullName, string controllerShortName,
        List<RouteInfo> actions, bool generateForms = true, List<ModelInfo>? models = null, string? modulePath = null)
    {
        actions = actions
            .GroupBy(action => (action.ActionName, action.UrlTemplate, action.Domain, action.Scheme))
            .Select(group => group.First() with { HttpMethods = group.SelectMany(action => action.HttpMethods).Distinct().ToArray() })
            .OrderBy(action => action.ActionName, StringComparer.Ordinal)
            .ThenBy(action => action.UrlTemplate, StringComparer.Ordinal)
            .ThenBy(action => string.Join(',', action.HttpMethods), StringComparer.Ordinal)
            .ToList();

        // Compute relative import path from the file location to the output root index.ts
        var relativePath = modulePath ?? ModulePaths.Actions([controllerFullName])[controllerFullName];
        var depth = relativePath.Count(c => c == '/') + 1; // +1 for the "actions" directory
        var importPrefix = string.Concat(Enumerable.Repeat("../", depth));

        var sb = new StringBuilder();
        sb.AppendLine($"import {{ type RouteDefinition, type RouteDefinitionInfo, type RouteQueryOptions, type FormDefinition, queryParams, applyUrlDefaults, normalizeParameters, buildUrl }} from '{importPrefix}index'");

        // Collect model imports needed for body types
        var modelImports = RouteFunctionWriter.ModelImports(actions, models).ToArray();
        var enumImports = RouteFunctionWriter.EnumImports(actions).ToArray();
        foreach (var modelName in modelImports)
        {
            sb.AppendLine($"import type {{ {modelName} }} from '{importPrefix}models/{modelName}'");
        }
        foreach (var enumName in enumImports)
            sb.AppendLine($"import type {{ {enumName} }} from '{importPrefix}enums/{enumName}'");

        sb.AppendLine();

        var actionNames = new List<string>();
        var usedNames = NameUtils.RuntimeNames();
        usedNames.UnionWith(modelImports);
        usedNames.UnionWith(enumImports);
        var displayName = NameUtils.Unique(NameUtils.ControllerDisplayName(controllerShortName), usedNames);

        // Group actions by ActionName to detect multi-template routes
        var multiTemplateGroups = actions
            .GroupBy(a => a.ActionName)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.ToList());

        // For naming, deduplicate multi-template actions so they get one base name
        var deduped = actions
            .GroupBy(a => a.ActionName)
            .Select(g => g.First())
            .ToList();
        var nameMap = ResolveNameConflicts(deduped, usedNames);
        usedNames.UnionWith(nameMap.Values);
        // Extend nameMap to cover all actions in multi-template groups
        foreach (var (_, group) in multiTemplateGroups)
        {
            var baseName = nameMap[group[0]];
            foreach (var a in group)
                nameMap[a] = baseName;
        }

        var temporaryNames = new Dictionary<RouteInfo, string>();
        foreach (var action in actions)
        {
            var jsName = nameMap[action];

            // For multi-template routes, use a prefixed temp name
            var isMultiTemplate = multiTemplateGroups.ContainsKey(action.ActionName);
            var emitName = isMultiTemplate ? NameUtils.Unique($"_{jsName}{multiTemplateGroups[action.ActionName].IndexOf(action)}", usedNames) : jsName;
            var payloadName = NameUtils.Unique(displayName + action.ActionName + (isMultiTemplate ? multiTemplateGroups[action.ActionName].IndexOf(action).ToString() : ""), usedNames, "Payload", "Query", "Form");

            temporaryNames[action] = emitName;
            if (!isMultiTemplate)
                actionNames.Add(jsName);

            RouteFunctionWriter.Write(sb, action, emitName, payloadName, generateForms, models);

        }

        // Emit multi-template exports as keyed dictionaries
        foreach (var (actionName, group) in multiTemplateGroups)
        {
            var baseName = nameMap[group[0]];
            var entries = string.Join(", ", group.Select((a, i) =>
                $"{System.Text.Json.JsonSerializer.Serialize((a.Domain ?? "") + a.UrlTemplate)}: {temporaryNames[a]}"));
            sb.AppendLine($"export const {baseName} = {{ {entries} }}");
            actionNames.Add(baseName);
        }

        // Default export
        sb.AppendLine($"const {displayName} = {{ {string.Join(", ", actionNames)} }}");
        sb.AppendLine($"export default {displayName}");

        // Write file
        var filePath = GeneratedOutput.PathUnder(outputDir, "actions", relativePath + ".ts");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, sb.ToString());
    }

    internal static Dictionary<RouteInfo, string> ResolveNameConflicts(List<RouteInfo> actions, IEnumerable<string>? reserved = null)
    {
        var used = reserved is null ? NameUtils.RuntimeNames() : new HashSet<string>(reserved, StringComparer.Ordinal);
        return actions.ToDictionary(action => action, action => NameUtils.Unique(NameUtils.SafeJsName(action.ActionName), used));
    }
}
