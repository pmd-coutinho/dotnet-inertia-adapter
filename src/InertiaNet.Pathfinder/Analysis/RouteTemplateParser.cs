using System.Globalization;
using Microsoft.AspNetCore.Routing.Patterns;

namespace InertiaNet.Pathfinder.Analysis;

static class RouteTemplateParser
{
    private static readonly Dictionary<string, string> ConstraintTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["int"] = "int", ["long"] = "long", ["float"] = "float", ["double"] = "double",
        ["decimal"] = "decimal", ["bool"] = "bool", ["guid"] = "Guid", ["datetime"] = "DateTime",
        ["alpha"] = "string", ["minlength"] = "string", ["maxlength"] = "string", ["length"] = "string",
    };

    public record TemplatePart(string Name, string? ClrType, bool IsOptional, string? DefaultValue = null, int CatchAll = 0);

    public static string StripConstraints(string template)
    {
        var pattern = RoutePatternFactory.Parse(template);
        var path = "/" + string.Join('/', pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => part switch
        {
            RoutePatternParameterPart parameter => Placeholder(parameter.Name,
                parameter.IsOptional || parameter.Default is not null || parameter.IsCatchAll,
                parameter.IsCatchAll ? parameter.EncodeSlashes ? 1 : 2 : 0),
            RoutePatternLiteralPart literal => literal.Content.Replace("{", "%7B").Replace("}", "%7D"),
            RoutePatternSeparatorPart separator => separator.Content,
            _ => throw new NotSupportedException($"Unsupported route part in '{template}'."),
        }))));
        return template.EndsWith('/') && path != "/" ? path + "/" : path;
    }

    public static List<TemplatePart> Parse(string template)
        => RoutePatternFactory.Parse(template).Parameters.Select(parameter => new TemplatePart(
            parameter.Name,
            parameter.ParameterPolicies.Select(policy => policy.Content?.Split('(')[0])
                .Where(name => name is not null && ConstraintTypeMap.ContainsKey(name))
                .Select(name => ConstraintTypeMap[name!]).FirstOrDefault(),
            parameter.IsOptional || parameter.Default is not null || parameter.IsCatchAll,
            Convert.ToString(parameter.Default, CultureInfo.InvariantCulture) is { Length: > 0 } value ? value : null,
            parameter.IsCatchAll ? parameter.EncodeSlashes ? 1 : 2 : 0)).ToList();

    public static string Placeholder(string name, bool optional, int catchAll = 0)
        => "{" + new string('*', catchAll) + name + (optional && catchAll == 0 ? "?" : "") + "}";
}
