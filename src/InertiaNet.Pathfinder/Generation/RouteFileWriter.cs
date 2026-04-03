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

        // Group routes by their name segments
        var namedRoutes = allRoutes
            .Where(r => r.RouteName != null)
            .GroupBy(r =>
            {
                var segments = r.RouteName!.Split('.');
                return segments.Length > 1
                    ? string.Join("/", segments[..^1])
                    : "";
            });

        foreach (var group in namedRoutes.OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var dirPath = string.IsNullOrEmpty(group.Key)
                ? Path.Combine(outputDir, "routes")
                : Path.Combine(outputDir, "routes", group.Key);

            Directory.CreateDirectory(dirPath);

            var segmentCount = string.IsNullOrEmpty(group.Key)
                ? 1
                : 1 + group.Key.Count(c => c == '/') + 1;
            var importPrefix = string.Concat(Enumerable.Repeat("../", segmentCount));

            var routeList = group
                .OrderBy(route => route.RouteName ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(route => route.UrlTemplate, StringComparer.Ordinal)
                .ThenBy(route => route.ActionName, StringComparer.Ordinal)
                .ThenBy(route => string.Join(',', route.HttpMethods), StringComparer.Ordinal)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"import {{ type RouteDefinition, type RouteDefinitionInfo, type RouteQueryOptions, type FormDefinition, queryParams, applyUrlDefaults, validateParameters }} from '{importPrefix}index'");

            // Collect model imports for body types
            var modelImports = CollectModelImports(routeList, models);
            foreach (var modelName in modelImports.OrderBy(name => name, StringComparer.Ordinal))
            {
                sb.AppendLine($"import type {{ {modelName} }} from '{importPrefix}models/{modelName}'");
            }

            sb.AppendLine();

            var exportNames = new List<string>();

            // Resolve naming conflicts within the group
            var nameMap = ResolveNameConflicts(routeList);

            foreach (var route in routeList)
            {
                var exportName = nameMap[route];
                exportNames.Add(exportName);

                // Body type export — use full route name for alias (posts.store → PostsStorePayload)
                var contextName = string.Join("", route.RouteName!.Split('.').Select(s => char.ToUpperInvariant(s[0]) + s[1..]));
                WriteBodyType(sb, route, contextName, models);

                var allRouteParams = BuildAllParams(route);
                var hasParams = allRouteParams.Length > 0;
                var defaultMethod = route.HttpMethods[0];

                // JSDoc comment
                sb.AppendLine("/**");
                if (route.SourceFile != null && route.SourceLine != null)
                {
                    sb.AppendLine($" * @see {route.ControllerFullName}::{route.ActionName}");
                    sb.AppendLine($" * @see {route.SourceFile}:{route.SourceLine}");
                }
                sb.AppendLine($" * @route {route.UrlTemplate}");
                foreach (var p in route.Parameters)
                {
                    if (p.DefaultValue != null)
                        sb.AppendLine($" * @param {p.Name} - Default: {p.DefaultValue}");
                }
                sb.AppendLine(" */");

                if (hasParams)
                {
                    var definitionUrl = BuildDefinitionUrl(route);
                    var allParams = BuildAllParams(route);
                    var argsType = BuildArgsType(allParams);
                    var allOptional = allParams.All(p => p.IsOptional);
                    var argsOptional = allOptional ? "?" : "";
                    var requiredParams = allParams.Where(p => !p.IsOptional).Select(p => p.Name).ToArray();
                    var optionalParams = allParams.Where(p => p.IsOptional).Select(p => p.Name).ToArray();

                    sb.AppendLine($"export const {exportName} = (args{argsOptional}: {argsType}, options?: RouteQueryOptions): RouteDefinition<\"{defaultMethod}\"> => ({{");
                    sb.AppendLine($"    url: {exportName}.url(args, options), method: \"{defaultMethod}\",");
                    sb.AppendLine("})");

                    var methodsArray = "[" + string.Join(",", route.HttpMethods.Select(m => $"\"{m}\"")) + "]";
                    var methodsType = "[" + string.Join(",", route.HttpMethods.Select(m => $"\"{m}\"")) + "]";
                    sb.AppendLine($"{exportName}.definition = {{ methods: {methodsArray}, url: \"{definitionUrl}\" }} satisfies RouteDefinitionInfo<{methodsType}>");

                    sb.AppendLine($"{exportName}.url = (args{argsOptional}: {argsType}, options?: RouteQueryOptions) => {{");

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
                            sb.AppendLine($"    validateParameters(\"{route.RouteName}\", {exportName}.definition.url, {requiredArray}, args as Record<string, unknown>, {optionalArray})");
                        }
                        else
                        {
                            sb.AppendLine($"    validateParameters(\"{route.RouteName}\", {exportName}.definition.url, {requiredArray}, args as Record<string, unknown>)");
                        }
                    }

                    var urlExpr = $"{exportName}.definition.url";

                    // Handle dynamic domain parameters first
                    var domainParams = ExtractDomainParams(route);
                    foreach (var dp in domainParams)
                    {
                        urlExpr += $".replace(\"{{{dp}}}\", String(args.{dp} ?? ''))";
                    }

                    foreach (var p in route.Parameters)
                    {
                        var placeholder = p.IsOptional ? $"{{{p.Name}?}}" : $"{{{p.Name}}}";
                        var defaultFallback = p.DefaultValue != null ? $"'{p.DefaultValue}'" : "''";
                        urlExpr += $".replace(\"{placeholder}\", String(args.{p.Name} ?? {defaultFallback}))";
                    }
                    urlExpr += ".replace(/\\/+$/, '')";

                    sb.AppendLine($"    return {urlExpr} + queryParams(options)");
                    sb.AppendLine("}");

                    // Per-method helpers
                    foreach (var method in route.HttpMethods)
                    {
                        sb.AppendLine($"{exportName}.{method} = (args{argsOptional}: {argsType}, options?: RouteQueryOptions) => ({{ url: {exportName}.url(args, options), method: \"{method}\" as const }})");
                    }

                    // Form helper
                    if (generateForms)
                        WriteFormHelper(sb, exportName, defaultMethod, hasParams: true, httpMethods: route.HttpMethods, argsType: argsType, argsOptional: argsOptional);

                    if (route.BodyTypeName != null)
                    {
                        var tsType = ResolveBodyType(route.BodyTypeName, models);
                        sb.AppendLine($"{exportName}.body = undefined as unknown as {tsType}");
                    }
                }
                else
                {
                    var definitionUrl = BuildDefinitionUrl(route);

                    sb.AppendLine($"export const {exportName} = (options?: RouteQueryOptions): RouteDefinition<\"{defaultMethod}\"> => ({{");
                    sb.AppendLine($"    url: {exportName}.url(options), method: \"{defaultMethod}\",");
                    sb.AppendLine("})");

                    var methodsArray = "[" + string.Join(",", route.HttpMethods.Select(m => $"\"{m}\"")) + "]";
                    var methodsType = "[" + string.Join(",", route.HttpMethods.Select(m => $"\"{m}\"")) + "]";
                    sb.AppendLine($"{exportName}.definition = {{ methods: {methodsArray}, url: \"{definitionUrl}\" }} satisfies RouteDefinitionInfo<{methodsType}>");
                    sb.AppendLine($"{exportName}.url = (options?: RouteQueryOptions) => {exportName}.definition.url + queryParams(options)");

                    // Per-method helpers
                    foreach (var method in route.HttpMethods)
                    {
                        sb.AppendLine($"{exportName}.{method} = (options?: RouteQueryOptions) => ({{ url: {exportName}.url(options), method: \"{method}\" as const }})");
                    }

                    // Form helper
                    if (generateForms)
                        WriteFormHelper(sb, exportName, defaultMethod, hasParams: false, httpMethods: route.HttpMethods);

                    if (route.BodyTypeName != null)
                    {
                        var tsType = ResolveBodyType(route.BodyTypeName, models);
                        sb.AppendLine($"{exportName}.body = undefined as unknown as {tsType}");
                    }
                }

                sb.AppendLine();
            }

            File.WriteAllText(Path.Combine(dirPath, "index.ts"), sb.ToString());
        }
    }

    private static void WriteBodyType(StringBuilder sb, RouteInfo route, string contextName, List<ModelInfo>? models)
    {
        if (route.BodyTypeName == null) return;

        var tsType = ResolveBodyType(route.BodyTypeName, models);
        sb.AppendLine($"export type {contextName}Payload = {tsType}");
        sb.AppendLine();
    }

    private static string ResolveBodyType(string clrTypeName, List<ModelInfo>? models)
    {
        var mapped = TypeMapper.ToTypeScript(clrTypeName);
        if (mapped != "string | number")
            return mapped;

        if (models != null && models.Any(m => m.ShortName == clrTypeName))
            return clrTypeName;

        return "unknown";
    }

    private static HashSet<string> CollectModelImports(List<RouteInfo> routes, List<ModelInfo>? models)
    {
        if (models == null) return [];

        var modelNames = models.Select(m => m.ShortName).ToHashSet();
        var needed = new HashSet<string>();

        foreach (var route in routes)
        {
            if (route.BodyTypeName != null && modelNames.Contains(route.BodyTypeName))
                needed.Add(route.BodyTypeName);
        }

        return needed;
    }

    // Wayfinder: GET/HEAD/OPTIONS → "get", everything else → "post"
    private static string FormSafeMethod(string method) =>
        method is "get" or "head" or "options" ? "get" : "post";

    private static void WriteFormHelper(StringBuilder sb, string jsName, string defaultMethod,
        bool hasParams, string[] httpMethods = null!, string? argsType = null, string? argsOptional = null)
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
            if (httpMethods != null)
            {
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
            if (httpMethods != null)
            {
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
    }

    private static Dictionary<RouteInfo, string> ResolveNameConflicts(List<RouteInfo> routes)
    {
        var nameMap = new Dictionary<RouteInfo, string>();
        var nameCount = new Dictionary<string, int>();

        foreach (var route in routes)
        {
            var segments = route.RouteName!.Split('.');
            var baseName = NameUtils.SafeJsName(segments[^1]);

            if (!nameCount.TryGetValue(baseName, out var count))
            {
                nameCount[baseName] = 1;
                nameMap[route] = baseName;
            }
            else
            {
                nameCount[baseName] = count + 1;
                nameMap[route] = $"{baseName}{count}";
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

    private static string BuildDefinitionUrl(RouteInfo route)
    {
        if (route.Domain == null)
            return route.UrlTemplate;

        var prefix = route.Scheme != null
            ? $"{route.Scheme}://{route.Domain}"
            : $"//{route.Domain}";

        return prefix + route.UrlTemplate;
    }

    private static List<string> ExtractDomainParams(RouteInfo route)
    {
        if (route.Domain == null) return [];

        var matches = System.Text.RegularExpressions.Regex.Matches(route.Domain, @"\{(\w+)\}");
        return matches.Select(m => m.Groups[1].Value).ToList();
    }

    private static RouteParameter[] BuildAllParams(RouteInfo route)
    {
        var domainParams = ExtractDomainParams(route);
        if (domainParams.Count == 0)
            return route.Parameters;

        var existingNames = route.Parameters.Select(p => p.Name.ToLowerInvariant()).ToHashSet();
        var extra = domainParams
            .Where(dp => !existingNames.Contains(dp.ToLowerInvariant()))
            .Select(dp => new RouteParameter(dp, "string", false))
            .ToArray();

        return [..extra, ..route.Parameters];
    }
}
