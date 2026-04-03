using System.Text.RegularExpressions;

namespace InertiaNet.Pathfinder.Analysis;

static partial class RouteTemplateParser
{
    [GeneratedRegex(@"\{(\w+)(\?)?(?:=([^}:]*?))?(?::([^}]*))?\}")]
    private static partial Regex ParameterRegex();

    private static readonly Dictionary<string, string> ConstraintTypeMap = new()
    {
        ["int"] = "int",
        ["long"] = "long",
        ["float"] = "float",
        ["double"] = "double",
        ["decimal"] = "decimal",
        ["bool"] = "bool",
        ["guid"] = "Guid",
        ["alpha"] = "string",
        ["minlength"] = "string",
        ["maxlength"] = "string",
        ["length"] = "string",
        ["min"] = "int",
        ["max"] = "int",
        ["range"] = "int",
    };

    public record TemplatePart(string Name, string ClrType, bool IsOptional, string? DefaultValue = null);

    /// <summary>
    /// Strips route constraints from a URL template:
    /// "{id:int}" → "{id}", "{slug?}" stays "{slug?}", "{id:int:min(1)}" → "{id}"
    /// </summary>
    public static string StripConstraints(string template)
    {
        return ParameterRegex().Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            var hasOptionalMarker = match.Groups[2].Success;
            var hasDefault = match.Groups[3].Success && match.Groups[3].Value.Length > 0;
            var isOptional = hasOptionalMarker || hasDefault;
            return isOptional ? $"{{{name}?}}" : $"{{{name}}}";
        });
    }

    public static List<TemplatePart> Parse(string template)
    {
        var parts = new List<TemplatePart>();
        var matches = ParameterRegex().Matches(template);

        foreach (Match match in matches)
        {
            var name = match.Groups[1].Value;
            var hasOptionalMarker = match.Groups[2].Success;
            var defaultValue = match.Groups[3].Success && match.Groups[3].Value.Length > 0
                ? match.Groups[3].Value
                : null;
            var constraint = match.Groups[4].Success ? match.Groups[4].Value : null;
            var isOptional = hasOptionalMarker || defaultValue != null;

            var clrType = "string";

            if (constraint != null)
            {
                // Strip regex constraints entirely
                if (constraint.StartsWith("regex("))
                {
                    clrType = "string";
                }
                else
                {
                    // Handle compound constraints like "int:min(1)"
                    var firstConstraint = constraint.Split(':')[0];
                    // Strip parameters like "min(1)" → "min"
                    var parenIndex = firstConstraint.IndexOf('(');
                    if (parenIndex >= 0)
                        firstConstraint = firstConstraint[..parenIndex];

                    if (ConstraintTypeMap.TryGetValue(firstConstraint, out var mappedType))
                        clrType = mappedType;
                }
            }

            parts.Add(new TemplatePart(name, clrType, isOptional, defaultValue));
        }

        return parts;
    }
}
