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
        // "MyApp.Controllers.PostsController" → "Posts"
        // Use just the short class name, stripped of "Controller" suffix
        var lastDot = controllerFullName.LastIndexOf('.');
        var shortName = lastDot >= 0 ? controllerFullName[(lastDot + 1)..] : controllerFullName;

        if (shortName.EndsWith("Controller"))
            shortName = shortName[..^"Controller".Length];
        else if (shortName.EndsWith("Endpoints"))
            shortName = shortName[..^"Endpoints".Length];
        else if (shortName.EndsWith("Endpoint"))
            shortName = shortName[..^"Endpoint".Length];

        return shortName.ToLowerInvariant();
    }

    public static string ControllerDisplayName(string shortName)
    {
        if (shortName.EndsWith("Controller"))
            return shortName[..^"Controller".Length];
        if (shortName.EndsWith("Endpoints"))
            return shortName[..^"Endpoints".Length];
        if (shortName.EndsWith("Endpoint"))
            return shortName[..^"Endpoint".Length];
        return shortName;
    }
}
