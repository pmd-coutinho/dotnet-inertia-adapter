using System.Text;
using InertiaNet.Pathfinder.Analysis;

namespace InertiaNet.Pathfinder.Generation;

static class ActionFileWriter
{
    public static void Write(string outputDir, string controllerFullName, string controllerShortName,
        List<RouteInfo> actions, bool generateForms = true, List<ModelInfo>? models = null)
    {
        actions = actions
            .OrderBy(action => action.ActionName, StringComparer.Ordinal)
            .ThenBy(action => action.UrlTemplate, StringComparer.Ordinal)
            .ThenBy(action => string.Join(',', action.HttpMethods), StringComparer.Ordinal)
            .ToList();

        // Compute relative import path from the file location to the output root index.ts
        var relativePath = NameUtils.ControllerToPath(controllerFullName);
        var depth = relativePath.Count(c => c == '/') + 1; // +1 for the "actions" directory
        var importPrefix = string.Concat(Enumerable.Repeat("../", depth));

        var sb = new StringBuilder();
        sb.AppendLine($"import {{ type RouteDefinition, type RouteDefinitionInfo, type RouteQueryOptions, type FormDefinition, queryParams, applyUrlDefaults, validateParameters }} from '{importPrefix}index'");

        // Collect model imports needed for body types
        var modelImports = CollectModelImports(actions, models);
        foreach (var (modelName, _) in modelImports.OrderBy(model => model.ModelName, StringComparer.Ordinal))
        {
            sb.AppendLine($"import type {{ {modelName} }} from '{importPrefix}models/{modelName}'");
        }

        sb.AppendLine();

        var actionNames = new List<string>();
        var displayName = NameUtils.ControllerDisplayName(controllerShortName);

        // Group actions by ActionName to detect multi-template routes
        var multiTemplateGroups = actions
            .GroupBy(a => a.ActionName)
            .Where(g => g.Select(a => a.UrlTemplate).Distinct().Count() > 1)
            .ToDictionary(g => g.Key, g => g.ToList());

        // For naming, deduplicate multi-template actions so they get one base name
        var deduped = actions
            .GroupBy(a => a.ActionName)
            .Select(g => g.First())
            .ToList();
        var nameMap = ResolveNameConflicts(deduped);
        // Extend nameMap to cover all actions in multi-template groups
        foreach (var (_, group) in multiTemplateGroups)
        {
            var baseName = nameMap[group[0]];
            foreach (var a in group)
                nameMap[a] = baseName;
        }

        foreach (var action in actions)
        {
            var jsName = nameMap[action];

            // Body type export
            WriteBodyType(sb, action, displayName + action.ActionName, models);

            var allParams = BuildAllParams(action);
            var hasParams = allParams.Length > 0;
            var defaultMethod = action.HttpMethods[0];
            var methodsArray = FormatMethodsArray(action.HttpMethods);
            var methodsType = FormatMethodsType(action.HttpMethods);

            // For multi-template routes, use a prefixed temp name
            var isMultiTemplate = multiTemplateGroups.ContainsKey(action.ActionName);
            var emitName = isMultiTemplate ? $"_{jsName}{multiTemplateGroups[action.ActionName].IndexOf(action)}" : jsName;

            if (!isMultiTemplate)
                actionNames.Add(jsName);

            // JSDoc comment
            WriteJsDoc(sb, action);

            if (hasParams)
                WriteActionWithParams(sb, action, emitName, defaultMethod, methodsArray, methodsType, generateForms, models);
            else
                WriteActionNoParams(sb, action, emitName, defaultMethod, methodsArray, methodsType, generateForms, models);

            sb.AppendLine();
        }

        // Emit multi-template exports as keyed dictionaries
        foreach (var (actionName, group) in multiTemplateGroups)
        {
            var baseName = nameMap[group[0]];
            var entries = string.Join(", ", group.Select((a, i) =>
                $"\"{a.UrlTemplate}\": _{baseName}{i}"));
            sb.AppendLine($"export const {baseName} = {{ {entries} }}");
            actionNames.Add(baseName);
        }

        // Default export
        sb.AppendLine($"const {displayName} = {{ {string.Join(", ", actionNames)} }}");
        sb.AppendLine($"export default {displayName}");

        // Write file
        var filePath = Path.Combine(outputDir, "actions", relativePath + ".ts");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, sb.ToString());
    }

    private static void WriteBodyType(StringBuilder sb, RouteInfo action, string contextName, List<ModelInfo>? models)
    {
        if (action.BodyTypeName == null) return;

        var tsType = ResolveBodyType(action.BodyTypeName, models);
        sb.AppendLine($"export type {contextName}Payload = {tsType}");
        sb.AppendLine();
    }

    private static string ResolveBodyType(string clrTypeName, List<ModelInfo>? models)
    {
        // Check if it's a known primitive type via TypeMapper
        var mapped = TypeMapper.ToTypeScript(clrTypeName);
        if (mapped != "string | number") // not the unknown fallback
            return mapped;

        // Check if it matches a known model
        if (models != null && models.Any(m => m.ShortName == clrTypeName))
            return clrTypeName;

        // Unknown complex type — prefer a safe fallback over invalid TypeScript identifiers.
        return "unknown";
    }

    private static List<(string ModelName, string ShortName)> CollectModelImports(
        List<RouteInfo> actions, List<ModelInfo>? models)
    {
        if (models == null) return [];

        var modelNames = models.Select(m => m.ShortName).ToHashSet();
        var needed = new HashSet<string>();

        foreach (var action in actions)
        {
            if (action.BodyTypeName != null && modelNames.Contains(action.BodyTypeName))
                needed.Add(action.BodyTypeName);
        }

        return needed
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(n => (n, n))
            .ToList();
    }

    private static void WriteJsDoc(StringBuilder sb, RouteInfo action)
    {
        sb.AppendLine("/**");
        if (action.SourceFile != null && action.SourceLine != null)
        {
            sb.AppendLine($" * @see {action.ControllerFullName}::{action.ActionName}");
            sb.AppendLine($" * @see {action.SourceFile}:{action.SourceLine}");
        }
        sb.AppendLine($" * @route {action.UrlTemplate}");
        foreach (var p in action.Parameters)
        {
            if (p.DefaultValue != null)
                sb.AppendLine($" * @param {p.Name} - Default: {p.DefaultValue}");
        }
        sb.AppendLine(" */");
    }

    private static void WriteActionNoParams(StringBuilder sb, RouteInfo action, string jsName,
        string defaultMethod, string methodsArray, string methodsType, bool generateForms, List<ModelInfo>? models)
    {
        var definitionUrl = BuildDefinitionUrl(action);

        sb.AppendLine($"export const {jsName} = (options?: RouteQueryOptions): RouteDefinition<\"{defaultMethod}\"> => ({{");
        sb.AppendLine($"    url: {jsName}.url(options), method: \"{defaultMethod}\",");
        sb.AppendLine("})");
        sb.AppendLine($"{jsName}.definition = {{ methods: {methodsArray}, url: \"{definitionUrl}\" }} satisfies RouteDefinitionInfo<{methodsType}>");
        sb.AppendLine($"{jsName}.url = (options?: RouteQueryOptions) => {jsName}.definition.url + queryParams(options)");

        foreach (var method in action.HttpMethods)
        {
            sb.AppendLine($"{jsName}.{method} = (options?: RouteQueryOptions) => ({{ url: {jsName}.url(options), method: \"{method}\" as const }})");
        }

        if (generateForms)
            WriteFormHelper(sb, jsName, defaultMethod, action.HttpMethods, hasParams: false);

        if (action.BodyTypeName != null)
        {
            var tsType = ResolveBodyType(action.BodyTypeName, models);
            sb.AppendLine($"{jsName}.body = undefined as unknown as {tsType}");
        }
    }

    private static void WriteActionWithParams(StringBuilder sb, RouteInfo action, string jsName,
        string defaultMethod, string methodsArray, string methodsType, bool generateForms, List<ModelInfo>? models)
    {
        var definitionUrl = BuildDefinitionUrl(action);
        var allParams = BuildAllParams(action);
        var argsType = BuildArgsType(allParams);
        var allOptional = allParams.All(p => p.IsOptional);
        var argsOptional = allOptional ? "?" : "";
        var requiredParams = allParams.Where(p => !p.IsOptional).Select(p => p.Name).ToArray();
        var optionalParams = allParams.Where(p => p.IsOptional).Select(p => p.Name).ToArray();

        sb.AppendLine($"export const {jsName} = (");
        sb.AppendLine($"    args{argsOptional}: {argsType},");
        sb.AppendLine($"    options?: RouteQueryOptions");
        sb.AppendLine($"): RouteDefinition<\"{defaultMethod}\"> => ({{ url: {jsName}.url(args, options), method: \"{defaultMethod}\" }})");
        sb.AppendLine();
        sb.AppendLine($"{jsName}.definition = {{ methods: {methodsArray}, url: \"{definitionUrl}\" }} satisfies RouteDefinitionInfo<{methodsType}>");
        sb.AppendLine();

        // .url function with destructuring
        sb.AppendLine($"{jsName}.url = (args{argsOptional}: {argsType}, options?: RouteQueryOptions) => {{");

        if (allParams.Length == 1)
        {
            var p = allParams[0];
            sb.AppendLine($"    if (typeof args === 'string' || typeof args === 'number') args = {{ {p.Name}: args }}");
            sb.AppendLine($"    if (Array.isArray(args)) args = {{ {p.Name}: args[0] }}");
        }
        else
        {
            var assignments = string.Join(", ", allParams.Select((p, i) => $"{p.Name}: args[{i}]"));
            sb.AppendLine($"    if (Array.isArray(args)) args = {{ {assignments} }}");
        }

        // Apply URL defaults
        sb.AppendLine($"    args = applyUrlDefaults(args ?? {{}}) as typeof args");

        // Validate route parameters after URL defaults are applied.
        if (requiredParams.Length > 0 || optionalParams.Length > 0)
        {
            var requiredArray = "[" + string.Join(", ", requiredParams.Select(p => $"\"{p}\"")) + "]";
            if (optionalParams.Length > 0)
            {
                var optionalArray = "[" + string.Join(", ", optionalParams.Select(p => $"\"{p}\"")) + "]";
                sb.AppendLine($"    validateParameters(\"{jsName}\", {jsName}.definition.url, {requiredArray}, args as Record<string, unknown>, {optionalArray})");
            }
            else
            {
                sb.AppendLine($"    validateParameters(\"{jsName}\", {jsName}.definition.url, {requiredArray}, args as Record<string, unknown>)");
            }
        }

        // Build URL replacement chain
        var urlExpr = $"{jsName}.definition.url";

        // Handle dynamic domain parameters first
        var domainParams = ExtractDomainParams(action);
        foreach (var dp in domainParams)
        {
            urlExpr += $".replace(\"{{{dp}}}\", String(args.{dp} ?? ''))";
        }

        foreach (var p in action.Parameters)
        {
            var placeholder = p.IsOptional ? $"{{{p.Name}?}}" : $"{{{p.Name}}}";
            var defaultFallback = p.DefaultValue != null ? $"'{p.DefaultValue}'" : "''";
            urlExpr += $".replace(\"{placeholder}\", String(args.{p.Name} ?? {defaultFallback}))";
        }
        urlExpr += ".replace(/\\/+$/, '')";

        sb.AppendLine($"    return {urlExpr} + queryParams(options)");
        sb.AppendLine("}");

        // Per-method helpers
        foreach (var method in action.HttpMethods)
        {
            sb.AppendLine($"{jsName}.{method} = (args{argsOptional}: {argsType}, options?: RouteQueryOptions) => ({{ url: {jsName}.url(args, options), method: \"{method}\" as const }})");
        }

        if (generateForms)
            WriteFormHelper(sb, jsName, defaultMethod, action.HttpMethods, hasParams: true, argsType: argsType, argsOptional: argsOptional);

        if (action.BodyTypeName != null)
        {
            var tsType = ResolveBodyType(action.BodyTypeName, models);
            sb.AppendLine($"{jsName}.body = undefined as unknown as {tsType}");
        }
    }

    // Wayfinder: GET/HEAD/OPTIONS → "get", everything else → "post"
    private static string FormSafeMethod(string method) =>
        method is "get" or "head" or "options" ? "get" : "post";

    private static void WriteFormHelper(StringBuilder sb, string jsName, string defaultMethod,
        string[] httpMethods, bool hasParams, string? argsType = null, string? argsOptional = null)
    {
        var formSafe = FormSafeMethod(defaultMethod);
        var needsSpoofing = formSafe != defaultMethod;
        var spoofedMethod = defaultMethod;

        if (hasParams)
        {
            sb.AppendLine($"{jsName}.form = (args{argsOptional}: {argsType}, options?: RouteQueryOptions): FormDefinition<\"{formSafe}\"> => ({{");
            if (needsSpoofing)
                sb.AppendLine($"    action: {jsName}.url(args, options), method: \"{formSafe}\", data: {{ _method: \"{spoofedMethod}\" }},");
            else
                sb.AppendLine($"    action: {jsName}.url(args, options), method: \"{formSafe}\",");
            sb.AppendLine("})");

            // Per-verb form helpers
            foreach (var method in httpMethods)
            {
                var verbFormSafe = FormSafeMethod(method);
                var verbNeedsSpoofing = verbFormSafe != method;
                sb.AppendLine($"{jsName}.form.{method} = (args{argsOptional}: {argsType}, options?: RouteQueryOptions): FormDefinition<\"{verbFormSafe}\"> => ({{");
                if (verbNeedsSpoofing)
                    sb.AppendLine($"    action: {jsName}.url(args, options), method: \"{verbFormSafe}\", data: {{ _method: \"{method}\" }},");
                else
                    sb.AppendLine($"    action: {jsName}.url(args, options), method: \"{verbFormSafe}\",");
                sb.AppendLine("})");
            }
        }
        else
        {
            sb.AppendLine($"{jsName}.form = (options?: RouteQueryOptions): FormDefinition<\"{formSafe}\"> => ({{");
            if (needsSpoofing)
                sb.AppendLine($"    action: {jsName}.url(options), method: \"{formSafe}\", data: {{ _method: \"{spoofedMethod}\" }},");
            else
                sb.AppendLine($"    action: {jsName}.url(options), method: \"{formSafe}\",");
            sb.AppendLine("})");

            // Per-verb form helpers
            foreach (var method in httpMethods)
            {
                var verbFormSafe = FormSafeMethod(method);
                var verbNeedsSpoofing = verbFormSafe != method;
                sb.AppendLine($"{jsName}.form.{method} = (options?: RouteQueryOptions): FormDefinition<\"{verbFormSafe}\"> => ({{");
                if (verbNeedsSpoofing)
                    sb.AppendLine($"    action: {jsName}.url(options), method: \"{verbFormSafe}\", data: {{ _method: \"{method}\" }},");
                else
                    sb.AppendLine($"    action: {jsName}.url(options), method: \"{verbFormSafe}\",");
                sb.AppendLine("})");
            }
        }
    }

    internal static Dictionary<RouteInfo, string> ResolveNameConflicts(List<RouteInfo> actions)
    {
        var nameMap = new Dictionary<RouteInfo, string>();
        var nameCount = new Dictionary<string, int>();

        foreach (var action in actions)
        {
            var baseName = NameUtils.SafeJsName(action.ActionName);
            if (!nameCount.TryGetValue(baseName, out var count))
            {
                nameCount[baseName] = 1;
                nameMap[action] = baseName;
            }
            else
            {
                nameCount[baseName] = count + 1;
                nameMap[action] = $"{baseName}{count}";
            }
        }

        return nameMap;
    }

    private static string BuildArgsType(RouteParameter[] parameters)
    {
        if (parameters.Length == 1)
        {
            var p = parameters[0];
            var tsType = TypeMapper.ToTypeScript(p.ClrTypeName);
            var opt = p.IsOptional ? "?" : "";
            return $"{{ {p.Name}{opt}: {tsType} }} | [{p.Name}: {tsType}] | {tsType}";
        }

        var fields = string.Join("; ", parameters.Select(p =>
        {
            var tsType = TypeMapper.ToTypeScript(p.ClrTypeName);
            var opt = p.IsOptional ? "?" : "";
            return $"{p.Name}{opt}: {tsType}";
        }));

        var tupleFields = string.Join(", ", parameters.Select(p =>
        {
            var tsType = TypeMapper.ToTypeScript(p.ClrTypeName);
            return $"{p.Name}: {tsType}";
        }));

        return $"{{ {fields} }} | [{tupleFields}]";
    }

    private static string FormatMethodsArray(string[] methods)
    {
        return "[" + string.Join(",", methods.Select(m => $"\"{m}\"")) + "]";
    }

    private static string FormatMethodsType(string[] methods)
    {
        return "[" + string.Join(",", methods.Select(m => $"\"{m}\"")) + "]";
    }

    /// <summary>
    /// Builds the definition URL including domain prefix when present.
    /// </summary>
    private static string BuildDefinitionUrl(RouteInfo action)
    {
        if (action.Domain == null)
            return action.UrlTemplate;

        var prefix = action.Scheme != null
            ? $"{action.Scheme}://{action.Domain}"
            : $"//{action.Domain}";

        return prefix + action.UrlTemplate;
    }

    /// <summary>
    /// Extracts dynamic parameter names from a domain pattern like "{sub}.example.com".
    /// </summary>
    private static List<string> ExtractDomainParams(RouteInfo action)
    {
        if (action.Domain == null) return [];

        var matches = System.Text.RegularExpressions.Regex.Matches(action.Domain, @"\{(\w+)\}");
        return matches.Select(m => m.Groups[1].Value).ToList();
    }

    /// <summary>
    /// Builds the combined parameter list including dynamic domain parameters.
    /// </summary>
    private static RouteParameter[] BuildAllParams(RouteInfo action)
    {
        var domainParams = ExtractDomainParams(action);
        if (domainParams.Count == 0)
            return action.Parameters;

        var existingNames = action.Parameters.Select(p => p.Name.ToLowerInvariant()).ToHashSet();
        var extra = domainParams
            .Where(dp => !existingNames.Contains(dp.ToLowerInvariant()))
            .Select(dp => new RouteParameter(dp, "string", false))
            .ToArray();

        return [..extra, ..action.Parameters];
    }
}
