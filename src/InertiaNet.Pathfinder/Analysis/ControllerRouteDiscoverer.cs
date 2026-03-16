using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace InertiaNet.Pathfinder.Analysis;

static class ControllerRouteDiscoverer
{
    private static readonly HashSet<string> HttpMethodAttributes = new()
    {
        "HttpGet", "HttpGetAttribute",
        "HttpPost", "HttpPostAttribute",
        "HttpPut", "HttpPutAttribute",
        "HttpPatch", "HttpPatchAttribute",
        "HttpDelete", "HttpDeleteAttribute",
    };

    private static readonly HashSet<string> SkipParameterAttributes = new()
    {
        "FromBody", "FromBodyAttribute",
        "FromServices", "FromServicesAttribute",
        "FromHeader", "FromHeaderAttribute",
        "FromQuery", "FromQueryAttribute",
    };

    private static readonly Dictionary<string, string[]> AttributeToMethods = new()
    {
        ["HttpGet"] = ["get", "head"],
        ["HttpGetAttribute"] = ["get", "head"],
        ["HttpPost"] = ["post"],
        ["HttpPostAttribute"] = ["post"],
        ["HttpPut"] = ["put"],
        ["HttpPutAttribute"] = ["put"],
        ["HttpPatch"] = ["patch"],
        ["HttpPatchAttribute"] = ["patch"],
        ["HttpDelete"] = ["delete"],
        ["HttpDeleteAttribute"] = ["delete"],
    };

    public static List<RouteInfo> Discover(SyntaxTree tree)
    {
        var routes = new List<RouteInfo>();
        var root = tree.GetRoot();

        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (!IsController(classDecl))
                continue;

            var fullName = GetFullName(classDecl);
            var shortName = classDecl.Identifier.Text;
            var classRoute = GetAttributeArgument(classDecl.AttributeLists, "Route", "RouteAttribute");
            var area = GetAttributeArgument(classDecl.AttributeLists, "Area", "AreaAttribute");

            // Detect [Host] attribute at the class level
            var classHost = GetAttributeArgument(classDecl.AttributeLists, "Host", "HostAttribute");

            foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                if (!method.Modifiers.Any(SyntaxKind.PublicKeyword))
                    continue;

                var actionName = method.Identifier.Text;
                var httpAttrs = GetHttpMethodAttributes(method.AttributeLists);

                if (httpAttrs.Count == 0)
                {
                    // Check for plain [Route] on the method
                    var methodRoute = GetAttributeArgument(method.AttributeLists, "Route", "RouteAttribute");
                    if (methodRoute != null)
                    {
                        httpAttrs.Add(("get", ["get", "head"], methodRoute));
                    }
                    else
                    {
                        continue; // No HTTP attribute, skip
                    }
                }

                var routeName = GetRouteNameFromAttributes(method.AttributeLists);

                // Extract [FromBody] parameter type
                var bodyTypeName = ExtractBodyTypeName(method);

                // Capture source file and line
                var lineSpan = method.GetLocation().GetLineSpan();
                var sourceFile = lineSpan.Path;
                var sourceLine = lineSpan.StartLinePosition.Line + 1; // 0-based to 1-based

                // Detect [Host] attribute at the method level (overrides class level)
                var methodHost = GetAttributeArgument(method.AttributeLists, "Host", "HostAttribute");
                var hostValue = methodHost ?? classHost;
                string? domain = null;
                string? scheme = null;

                if (hostValue != null)
                {
                    // Parse scheme://domain or just domain
                    if (hostValue.Contains("://"))
                    {
                        var schemeEnd = hostValue.IndexOf("://");
                        scheme = hostValue[..schemeEnd];
                        domain = hostValue[(schemeEnd + 3)..];
                    }
                    else
                    {
                        domain = hostValue;
                    }
                }

                // Group httpAttrs by their template value to handle multi-template routes
                var templateGroups = httpAttrs
                    .GroupBy(h => h.TemplateValue ?? "")
                    .ToList();

                foreach (var templateGroup in templateGroups)
                {
                    var allMethods = templateGroup.SelectMany(h => h.Methods).Distinct().ToArray();
                    var methodTemplate = templateGroup.Key == "" ? null : templateGroup.Key;

                    // Build URL template
                    var urlTemplate = BuildUrlTemplate(classRoute, methodTemplate, shortName, actionName, area);

                    // Parse template parameters before stripping constraints (need constraints for type mapping)
                    var templateParts = RouteTemplateParser.Parse(urlTemplate);
                    var templateParamNames = templateParts.Select(p => p.Name.ToLowerInvariant()).ToHashSet();

                    // Strip constraints from URL template for the generated output
                    var cleanUrlTemplate = RouteTemplateParser.StripConstraints(urlTemplate);

                    // Extract method parameters
                    var parameters = ExtractParameters(method, templateParamNames, templateParts);

                    routes.Add(new RouteInfo(
                        fullName,
                        shortName,
                        actionName,
                        allMethods,
                        cleanUrlTemplate,
                        parameters,
                        routeName,
                        sourceFile,
                        sourceLine,
                        bodyTypeName,
                        domain,
                        scheme));
                }
            }
        }

        return routes;
    }

    private static bool IsController(ClassDeclarationSyntax classDecl)
    {
        // Check class name ends with "Controller"
        if (classDecl.Identifier.Text.EndsWith("Controller"))
            return true;

        // Check if inherits from Controller/ControllerBase
        if (classDecl.BaseList != null)
        {
            foreach (var baseType in classDecl.BaseList.Types)
            {
                var name = baseType.Type switch
                {
                    IdentifierNameSyntax id => id.Identifier.Text,
                    QualifiedNameSyntax q => q.Right.Identifier.Text,
                    _ => null
                };

                if (name is "Controller" or "ControllerBase")
                    return true;
            }
        }

        // Check for [ApiController] attribute
        return HasAttribute(classDecl.AttributeLists, "ApiController", "ApiControllerAttribute");
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

    private static bool HasAttribute(SyntaxList<AttributeListSyntax> attrLists, params string[] names)
    {
        return attrLists.SelectMany(al => al.Attributes)
            .Any(a => names.Contains(GetAttributeName(a)));
    }

    private static string? GetAttributeArgument(SyntaxList<AttributeListSyntax> attrLists, params string[] names)
    {
        var attr = attrLists.SelectMany(al => al.Attributes)
            .FirstOrDefault(a => names.Contains(GetAttributeName(a)));

        if (attr?.ArgumentList?.Arguments.Count > 0)
        {
            var arg = attr.ArgumentList.Arguments[0];
            if (arg.Expression is LiteralExpressionSyntax literal)
                return literal.Token.ValueText;
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

    private static List<(string Template, string[] Methods, string? TemplateValue)> GetHttpMethodAttributes(
        SyntaxList<AttributeListSyntax> attrLists)
    {
        var results = new List<(string Template, string[] Methods, string? TemplateValue)>();

        foreach (var attr in attrLists.SelectMany(al => al.Attributes))
        {
            var name = GetAttributeName(attr);
            if (!AttributeToMethods.TryGetValue(name, out var methods))
                continue;

            string? template = null;
            if (attr.ArgumentList?.Arguments.Count > 0)
            {
                var firstArg = attr.ArgumentList.Arguments[0];
                if (firstArg.NameEquals == null && firstArg.Expression is LiteralExpressionSyntax literal)
                    template = literal.Token.ValueText;
            }

            // Use the original template value as-is for grouping, empty string as fallback
            results.Add((template ?? "", methods, template));
        }

        return results;
    }

    private static string? GetRouteNameFromAttributes(SyntaxList<AttributeListSyntax> attrLists)
    {
        foreach (var attr in attrLists.SelectMany(al => al.Attributes))
        {
            if (attr.ArgumentList == null) continue;

            foreach (var arg in attr.ArgumentList.Arguments)
            {
                if (arg.NameEquals?.Name.Identifier.Text == "Name" &&
                    arg.Expression is LiteralExpressionSyntax literal)
                {
                    return literal.Token.ValueText;
                }
            }
        }

        return null;
    }

    private static string BuildUrlTemplate(string? classRoute, string? methodTemplate,
        string controllerName, string actionName, string? area)
    {
        string template;

        if (!string.IsNullOrEmpty(methodTemplate) && methodTemplate.StartsWith('/'))
            template = methodTemplate; // Absolute path overrides class route
        else if (classRoute != null && !string.IsNullOrEmpty(methodTemplate))
            template = classRoute.TrimEnd('/') + "/" + methodTemplate.TrimStart('/');
        else if (classRoute != null)
            template = classRoute;
        else if (!string.IsNullOrEmpty(methodTemplate))
            template = methodTemplate;
        else
            template = "";

        template = TokenResolver.Resolve(template, controllerName, actionName, area);

        if (!template.StartsWith('/'))
            template = "/" + template;

        return template;
    }

    private static RouteParameter[] ExtractParameters(MethodDeclarationSyntax method,
        HashSet<string> templateParamNames, List<RouteTemplateParser.TemplatePart> templateParts)
    {
        var parameters = new List<RouteParameter>();

        foreach (var param in method.ParameterList.Parameters)
        {
            if (param.Identifier.Text == null) continue;

            // Skip params with excluded attributes
            if (param.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => SkipParameterAttributes.Contains(GetAttributeName(a))))
                continue;

            var paramName = param.Identifier.Text;
            var hasFromRoute = param.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => GetAttributeName(a) is "FromRoute" or "FromRouteAttribute");

            // Include if [FromRoute] or matches a template placeholder
            if (!hasFromRoute && !templateParamNames.Contains(paramName.ToLowerInvariant()))
                continue;

            // Try to get type from template constraint first, then from parameter type
            var templatePart = templateParts.FirstOrDefault(p =>
                p.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase));

            var clrType = templatePart?.ClrType ?? GetParameterTypeName(param) ?? "string";
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
            NullableTypeSyntax n => GetTypeNameFromSyntax(n.ElementType),
            _ => param.Type.ToString()
        };
    }

    private static string GetTypeNameFromSyntax(TypeSyntax type)
    {
        return type switch
        {
            PredefinedTypeSyntax p => p.Keyword.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => type.ToString()
        };
    }

    private static string? ExtractBodyTypeName(MethodDeclarationSyntax method)
    {
        foreach (var param in method.ParameterList.Parameters)
        {
            var hasFromBody = param.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => GetAttributeName(a) is "FromBody" or "FromBodyAttribute");

            if (hasFromBody && param.Type != null)
                return GetTypeNameFromSyntax(param.Type is NullableTypeSyntax n ? n.ElementType : param.Type);
        }

        return null;
    }
}
