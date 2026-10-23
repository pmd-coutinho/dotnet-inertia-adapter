namespace InertiaNet.Pathfinder.Generation;

static class NameUtils
{
    public static string PropertyKey(string name)
        => InertiaNet.Pathfinder.Analysis.ContractNames.Identifier(name) == name ? name : System.Text.Json.JsonSerializer.Serialize(name);
    private static readonly HashSet<string> JsReservedWords = new()
    {
        "await", "break", "case", "catch", "class", "const", "continue",
        "debugger", "default", "delete", "do", "else", "enum", "export",
        "extends", "false", "finally", "for", "function", "if", "implements",
        "import", "in", "instanceof", "interface", "let", "new", "null",
        "package", "private", "protected", "public", "return", "static",
        "super", "switch", "this", "throw", "true", "try", "typeof", "var",
        "void", "while", "with", "yield", "__proto__", "then",
    };

    public static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        // If the name is all uppercase, lowercase the whole thing
        if (name.All(char.IsUpper))
            return name.ToLowerInvariant();

        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    public static string SafeJsName(string name)
    {
        var camel = InertiaNet.Pathfinder.Analysis.ContractNames.Identifier(ToCamelCase(name));

        if (JsReservedWords.Contains(camel))
            return camel + "Method";

        // Names starting with a digit are invalid JS identifiers — prefix them
        if (camel.Length > 0 && char.IsDigit(camel[0]))
            return "method" + camel;

        return camel;
    }

    public static string Unique(string preferred, ISet<string> used, params string[] companions)
    {
        preferred = InertiaNet.Pathfinder.Analysis.ContractNames.Identifier(preferred);
        if (JsReservedWords.Contains(preferred)) preferred += "Method";
        var candidate = preferred;
        var suffix = 0;
        while (used.Contains(candidate) || companions.Any(companion => used.Contains(candidate + companion))) candidate = preferred + ++suffix;
        used.Add(candidate);
        foreach (var companion in companions) used.Add(candidate + companion);
        return candidate;
    }

    public static HashSet<string> RuntimeNames() => new(StringComparer.Ordinal)
        { "Object", "RouteDefinition", "RouteDefinitionInfo", "RouteQueryOptions", "FormDefinition", "queryParams", "applyUrlDefaults", "normalizeParameters", "buildUrl" };

    public static string ControllerToPath(string controllerFullName)
    {
        var segments = controllerFullName.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return string.Empty;

        var shortName = StripControllerSuffix(segments[^1]);
        var namespaceSegments = segments
            .Skip(1)
            .Take(Math.Max(segments.Length - 2, 0))
            .Where(segment => !IsPathNoise(segment))
            .Select(segment => InertiaNet.Pathfinder.Analysis.ContractNames.Identifier(segment.ToLowerInvariant()))
            .ToList();

        namespaceSegments.Add(InertiaNet.Pathfinder.Analysis.ContractNames.Identifier(shortName.ToLowerInvariant()));
        return string.Join('/', namespaceSegments);
    }

    public static string ControllerDisplayName(string shortName)
    {
        return StripControllerSuffix(shortName);
    }

    private static string StripControllerSuffix(string shortName)
    {
        if (shortName.EndsWith("Controller"))
            return shortName[..^"Controller".Length];
        if (shortName.EndsWith("Endpoints"))
            return shortName[..^"Endpoints".Length];
        if (shortName.EndsWith("Endpoint"))
            return shortName[..^"Endpoint".Length];
        return shortName;
    }

    private static bool IsPathNoise(string segment)
        => segment is "Controllers" or "Controller" or "Endpoints" or "Endpoint";
}
