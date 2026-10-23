using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace InertiaNet.Pathfinder.Analysis;

internal static class RouteParameterDiscovery
{
    private static readonly HashSet<string> NonRouteAttributes = new(StringComparer.Ordinal)
        { "FromBody", "FromQuery", "FromForm", "FromHeader", "FromServices" };

    public static RouteParameter[] Discover(IEnumerable<ParameterSyntax> parameters, List<RouteTemplateParser.TemplatePart> parts)
    {
        var candidates = parameters.Where(parameter => !parameter.AttributeLists.SelectMany(list => list.Attributes)
            .Any(attribute => NonRouteAttributes.Contains(AttributeName(attribute)))).ToArray();
        return parts.Select(part =>
        {
            var parameter = candidates.FirstOrDefault(candidate =>
                BindingName(candidate).Equals(part.Name, StringComparison.OrdinalIgnoreCase));
            var type = parameter?.Type is NullableTypeSyntax nullable ? nullable.ElementType.ToString() : parameter?.Type?.ToString();
            var defaultValue = part.DefaultValue;
            if (defaultValue is null && part.IsOptional && parameter?.Default?.Value is LiteralExpressionSyntax literal
                && !literal.IsKind(SyntaxKind.NullLiteralExpression))
                defaultValue = literal.Token.ValueText;
            return new RouteParameter(part.Name, part.ClrType ?? type ?? "string", part.IsOptional, defaultValue, part.CatchAll);
        }).ToArray();
    }

    private static string BindingName(ParameterSyntax parameter)
    {
        var attribute = parameter.AttributeLists.SelectMany(list => list.Attributes).FirstOrDefault(a => AttributeName(a) == "FromRoute");
        var alias = attribute?.ArgumentList?.Arguments.FirstOrDefault(argument => argument.NameEquals?.Name.Identifier.ValueText == "Name");
        return alias?.Expression is LiteralExpressionSyntax literal ? literal.Token.ValueText : parameter.Identifier.ValueText;
    }

    private static string AttributeName(AttributeSyntax attribute)
    {
        var name = attribute.Name.ToString().Split('.').Last();
        return name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^9] : name;
    }
}
