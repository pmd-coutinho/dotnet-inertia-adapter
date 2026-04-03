namespace InertiaNet.Pathfinder.Generation;

static class NameUtils
{
    private static readonly HashSet<string> JsReservedWords = new()
    {
        "await", "break", "case", "catch", "class", "const", "continue",
        "debugger", "default", "delete", "do", "else", "enum", "export",
        "extends", "false", "finally", "for", "function", "if", "implements",
        "import", "in", "instanceof", "interface", "let", "new", "null",
        "package", "private", "protected", "public", "return", "static",
        "super", "switch", "this", "throw", "true", "try", "typeof", "var",
        "void", "while", "with", "yield",
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
        var camel = ToCamelCase(name);

        if (JsReservedWords.Contains(camel))
            return camel + "Method";

        // Names starting with a digit are invalid JS identifiers — prefix them
        if (camel.Length > 0 && char.IsDigit(camel[0]))
            return "method" + camel;

        return camel;
    }

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
            .Select(segment => segment.ToLowerInvariant())
            .ToList();

        namespaceSegments.Add(shortName.ToLowerInvariant());
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
