using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace InertiaNet.Pathfinder.Analysis;

static class WolverineRouteDiscoverer
{
    private static readonly Dictionary<string, string[]> AttributeToMethods = new()
    {
        ["WolverineGet"] = ["get", "head"],
        ["WolverineGetAttribute"] = ["get", "head"],
        ["WolverinePost"] = ["post"],
        ["WolverinePostAttribute"] = ["post"],
        ["WolverinePut"] = ["put"],
        ["WolverinePutAttribute"] = ["put"],
        ["WolverineDelete"] = ["delete"],
        ["WolverineDeleteAttribute"] = ["delete"],
        ["WolverinePatch"] = ["patch"],
        ["WolverinePatchAttribute"] = ["patch"],
        ["WolverineHead"] = ["head"],
        ["WolverineHeadAttribute"] = ["head"],
        ["WolverineOptions"] = ["options"],
        ["WolverineOptionsAttribute"] = ["options"],
    };

    private static readonly HashSet<string> SkipParameterTypes = new()
    {
        "HttpContext", "ClaimsPrincipal", "CancellationToken",
        "HttpRequest", "HttpResponse",
    };

    public static List<RouteInfo> Discover(SyntaxTree tree)
    {
        var routes = new List<RouteInfo>();
        var root = tree.GetRoot();

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var wolverineAttr = FindWolverineHttpAttribute(method.AttributeLists);
            if (wolverineAttr == null)
                continue;

            var (attrName, attr) = wolverineAttr.Value;

            if (!AttributeToMethods.TryGetValue(attrName, out var httpMethods))
                continue;

            // Extract URL template from first positional argument
            var urlTemplate = GetFirstPositionalArgument(attr);
            if (urlTemplate == null)
                continue;

            if (!urlTemplate.StartsWith('/'))
                urlTemplate = "/" + urlTemplate;

            // Extract route name from Name = "..." named argument
            var routeName = GetNamedArgument(attr, "Name");

            // Parse template parameters
            var templateParts = RouteTemplateParser.Parse(urlTemplate);
            var templateParamNames = templateParts.Select(p => p.Name.ToLowerInvariant()).ToHashSet();
            var cleanUrlTemplate = RouteTemplateParser.StripConstraints(urlTemplate);

            // Get containing class info
            var classDecl = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            var shortName = classDecl?.Identifier.Text ?? "UnknownEndpoint";
            var fullName = classDecl != null ? GetFullName(classDecl) : shortName;

            var actionName = method.Identifier.Text;
            var parameters = ExtractParameters(method, templateParamNames, templateParts);

            // Extract body type — in Wolverine, the first non-route, non-service parameter
            // that is a complex type is treated as the request body
            var bodyTypeName = ExtractBodyTypeName(method, templateParamNames);

            // Capture source file and line
            var lineSpan = method.GetLocation().GetLineSpan();
            var sourceFile = lineSpan.Path;
            var sourceLine = lineSpan.StartLinePosition.Line + 1;

            routes.Add(new RouteInfo(
                fullName,
                shortName,
                actionName,
                httpMethods,
                cleanUrlTemplate,
                parameters,
                routeName,
                sourceFile,
                sourceLine,
                bodyTypeName));
        }

        return routes;
    }

    private static (string Name, AttributeSyntax Attr)? FindWolverineHttpAttribute(
        SyntaxList<AttributeListSyntax> attrLists)
    {
        foreach (var attr in attrLists.SelectMany(al => al.Attributes))
        {
            var name = GetAttributeName(attr);
            if (AttributeToMethods.ContainsKey(name))
                return (name, attr);
        }

        return null;
    }

    private static string? GetFirstPositionalArgument(AttributeSyntax attr)
    {
        if (attr.ArgumentList?.Arguments.Count > 0)
        {
            var firstArg = attr.ArgumentList.Arguments[0];
            if (firstArg.NameEquals == null && firstArg.Expression is LiteralExpressionSyntax literal)
                return literal.Token.ValueText;
        }

        return null;
    }

    private static string? GetNamedArgument(AttributeSyntax attr, string argumentName)
    {
        if (attr.ArgumentList == null) return null;

        foreach (var arg in attr.ArgumentList.Arguments)
        {
            if (arg.NameEquals?.Name.Identifier.Text == argumentName &&
                arg.Expression is LiteralExpressionSyntax literal)
            {
                return literal.Token.ValueText;
            }
        }

        return null;
    }

    private static string GetAttributeName(AttributeSyntax attr)
    {
        return attr.Name switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            QualifiedNameSyntax q => q.Right.Identifier.Text,
            _ => attr.Name.ToString()
        };
    }

    private static string GetFullName(ClassDeclarationSyntax classDecl)
    {
        var parts = new List<string> { classDecl.Identifier.Text };
        var parent = classDecl.Parent;

        while (parent != null)
        {
            if (parent is NamespaceDeclarationSyntax ns)
                parts.Insert(0, ns.Name.ToString());
            else if (parent is FileScopedNamespaceDeclarationSyntax fileNs)
                parts.Insert(0, fileNs.Name.ToString());
            parent = parent.Parent;
        }

        return string.Join(".", parts);
    }

    private static RouteParameter[] ExtractParameters(MethodDeclarationSyntax method,
        HashSet<string> templateParamNames, List<RouteTemplateParser.TemplatePart> templateParts)
    {
        var parameters = new List<RouteParameter>();

        foreach (var param in method.ParameterList.Parameters)
        {
            var paramName = param.Identifier.Text;
            var typeName = GetParameterTypeName(param);
            var matchesTemplate = templateParamNames.Contains(paramName.ToLowerInvariant());

            // Skip known framework/service types
            if (typeName != null && SkipParameterTypes.Contains(typeName))
                continue;

            // Skip interfaces (likely injected services) — starts with I + uppercase letter
            if (typeName != null && typeName.Length >= 2 &&
                typeName[0] == 'I' && char.IsUpper(typeName[1]))
                continue;

            // Skip [NotBody] params that don't match a template placeholder
            var hasNotBody = param.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => GetAttributeName(a) is "NotBody" or "NotBodyAttribute");
            if (hasNotBody && !matchesTemplate)
                continue;

            // Only include if it matches a template placeholder
            if (!matchesTemplate)
                continue;

            var templatePart = templateParts.FirstOrDefault(p =>
                p.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase));

            var clrType = templatePart?.ClrType ?? typeName ?? "string";
            var isOptional = templatePart?.IsOptional ?? param.Default != null;

            // Prefer route template default over method param default
            var defaultValue = templatePart?.DefaultValue;
            if (defaultValue == null && param.Default != null)
                defaultValue = param.Default.Value.ToString();

            parameters.Add(new RouteParameter(paramName, clrType, isOptional, defaultValue));
        }

        return parameters.ToArray();
    }

    private static string? GetParameterTypeName(ParameterSyntax param)
    {
        if (param.Type == null) return null;

        return param.Type switch
        {
            PredefinedTypeSyntax p => p.Keyword.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            NullableTypeSyntax n => n.ElementType switch
            {
                PredefinedTypeSyntax p => p.Keyword.Text,
                IdentifierNameSyntax id => id.Identifier.Text,
                _ => n.ElementType.ToString()
            },
            _ => param.Type.ToString()
        };
    }

    private static readonly HashSet<string> PrimitiveTypes = new()
    {
        "string", "int", "long", "short", "byte", "float", "double", "decimal",
        "bool", "Guid", "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly",
    };

    private static string? ExtractBodyTypeName(MethodDeclarationSyntax method, HashSet<string> templateParamNames)
    {
        // In Wolverine convention, the first parameter that:
        // - is not a route parameter
        // - is not a known service/framework type
        // - is not an interface
        // - is not a primitive type
        // - is not marked [NotBody]
        // ...is the request body (command/query)
        foreach (var param in method.ParameterList.Parameters)
        {
            var paramName = param.Identifier.Text;
            var typeName = GetParameterTypeName(param);

            if (typeName == null) continue;
            if (SkipParameterTypes.Contains(typeName)) continue;
            if (typeName.Length >= 2 && typeName[0] == 'I' && char.IsUpper(typeName[1])) continue;
            if (templateParamNames.Contains(paramName.ToLowerInvariant())) continue;
            if (PrimitiveTypes.Contains(typeName)) continue;

            var hasNotBody = param.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => GetAttributeName(a) is "NotBody" or "NotBodyAttribute");
            if (hasNotBody) continue;

            return typeName;
        }

        return null;
    }
}
