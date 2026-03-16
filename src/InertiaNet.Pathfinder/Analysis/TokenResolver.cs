namespace InertiaNet.Pathfinder.Analysis;

static class TokenResolver
{
    public static string Resolve(string template, string controllerName, string actionName, string? area)
    {
        var controllerToken = controllerName.EndsWith("Controller")
            ? controllerName[..^"Controller".Length]
            : controllerName;

        var result = template
            .Replace("[controller]", controllerToken.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", actionName, StringComparison.OrdinalIgnoreCase);

        if (area != null)
            result = result.Replace("[area]", area.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);

        return result;
    }
}
