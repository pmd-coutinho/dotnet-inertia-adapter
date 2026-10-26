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
        ["HttpGet"] = ["get"],
        ["HttpGetAttribute"] = ["get"],
        ["HttpHead"] = ["head"],
        ["HttpHeadAttribute"] = ["head"],
        ["HttpOptions"] = ["options"],
        ["HttpOptionsAttribute"] = ["options"],
        ["HttpPost"] = ["post"],
        ["HttpPostAttribute"] = ["post"],
        ["HttpPut"] = ["put"],
        ["HttpPutAttribute"] = ["put"],
        ["HttpPatch"] = ["patch"],
        ["HttpPatchAttribute"] = ["patch"],
        ["HttpDelete"] = ["delete"],
        ["HttpDeleteAttribute"] = ["delete"],
    };

    public static List<RouteInfo> Discover(SyntaxTree tree, SemanticModel? semantic = null)
    {
        var routes = new List<RouteInfo>();
        var root = tree.GetRoot();

        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (!IsController(classDecl))
                continue;

            var fullName = GetFullName(classDecl);
            var shortName = classDecl.Identifier.Text;
            var classRouteResolved = TryGetAttributeArgument(classDecl.AttributeLists, semantic, out var classRoute, "Route", "RouteAttribute");
            var areaResolved = TryGetAttributeArgument(classDecl.AttributeLists, semantic, out var area, "Area", "AreaAttribute");

            // Detect [Host] attribute at the class level
            var classHostResolved = TryGetAttributeArgument(classDecl.AttributeLists, semantic, out var classHost, "Host", "HostAttribute");

            foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                if (!method.Modifiers.Any(SyntaxKind.PublicKeyword))
                    continue;

                var actionName = method.Identifier.Text;
                var httpAttrs = GetHttpMethodAttributes(method.AttributeLists, semantic);

                if (httpAttrs.Count == 0)
                {
                    // Check for plain [Route] on the method
                    if (!TryGetAttributeArgument(method.AttributeLists, semantic, out var methodRoute, "Route", "RouteAttribute"))
                        continue;
                    if (methodRoute != null)
                    {
                        httpAttrs.Add((["get"], methodRoute, true));
                    }
                    else
                    {
                        continue; // No HTTP attribute, skip
                    }
                }

                if (!TryGetRouteNameFromAttributes(method.AttributeLists, semantic, out var routeName))
                    continue;

                // Extract [FromBody] parameter type
                var bodyTypeName = ExtractBodyTypeName(method);

                // Capture source file and line
                var lineSpan = method.GetLocation().GetLineSpan();
                var sourceFile = lineSpan.Path;
                var sourceLine = lineSpan.StartLinePosition.Line + 1; // 0-based to 1-based

                // Detect [Host] attribute at the method level (overrides class level)
                if (!TryGetAttributeArgument(method.AttributeLists, semantic, out var methodHost, "Host", "HostAttribute")
                    || (methodHost is null && !classHostResolved))
                    continue;
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
                    .Where(h => h.Resolved)
                    .GroupBy(h => h.TemplateValue)
                    .ToList();

                foreach (var templateGroup in templateGroups)
                {
                    var allMethods = templateGroup.SelectMany(h => h.Methods).Distinct().ToArray();
                    var methodTemplate = templateGroup.Key;
                    var absolute = methodTemplate?.StartsWith('/') == true || methodTemplate?.StartsWith("~/", StringComparison.Ordinal) == true;
                    if ((!absolute && !classRouteResolved) || !areaResolved)
                        continue;
                    if (classRoute is null && methodTemplate is null)
                    {
                        Report(method, "No attribute route template is available. Conventional routes cannot be inferred; route helper omitted. Add an explicit route template.");
                        continue;
                    }

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
                        scheme,
                        SourceSpanStart: method.SpanStart));
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

    private static bool TryGetAttributeArgument(SyntaxList<AttributeListSyntax> attrLists,
        SemanticModel? semantic, out string? value, params string[] names)
    {
        var attr = attrLists.SelectMany(al => al.Attributes)
            .FirstOrDefault(a => names.Contains(GetAttributeName(a)));

        return TryGetAttributeArgument(attr, semantic, out value);
    }

    private static bool TryGetAttributeArgument(AttributeSyntax? attr, SemanticModel? semantic, out string? value)
    {
        value = null;
        var arg = attr?.ArgumentList?.Arguments.FirstOrDefault(argument => argument.NameEquals is null);
        if (arg is not null)
        {
            value = ConstantString(arg.Expression, semantic);
            if (value is null)
            {
                Report(arg, "Cannot resolve route attribute argument to a constant string; dependent route helpers omitted. Use a string literal or a resolvable constant.");
                return false;
            }
        }

        return true;
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

    private static List<(string[] Methods, string? TemplateValue, bool Resolved)> GetHttpMethodAttributes(
        SyntaxList<AttributeListSyntax> attrLists, SemanticModel? semantic)
    {
        var results = new List<(string[] Methods, string? TemplateValue, bool Resolved)>();

        foreach (var attr in attrLists.SelectMany(al => al.Attributes))
        {
            var name = GetAttributeName(attr);
            if (!AttributeToMethods.TryGetValue(name, out var methods))
                continue;

            var resolved = TryGetAttributeArgument(attr, semantic, out var template);
            results.Add((methods, template, resolved));
        }

        return results;
    }

    private static bool TryGetRouteNameFromAttributes(SyntaxList<AttributeListSyntax> attrLists,
        SemanticModel? semantic, out string? name)
    {
        name = null;
        foreach (var attr in attrLists.SelectMany(al => al.Attributes))
        {
            if (attr.ArgumentList == null) continue;

            foreach (var arg in attr.ArgumentList.Arguments)
            {
                if (arg.NameEquals?.Name.Identifier.Text == "Name")
                {
                    name = ConstantString(arg.Expression, semantic);
                    if (name is not null) return true;
                    Report(arg, "Cannot resolve route name to a constant string; route helper omitted.");
                    return false;
                }
            }
        }

        return true;
    }

    private static string? ConstantString(ExpressionSyntax expression, SemanticModel? semantic)
        => expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : semantic?.GetConstantValue(expression).Value as string;

    private static void Report(SyntaxNode node, string message)
    {
        var span = node.GetLocation().GetLineSpan();
        PathfinderDiagnostics.Report(span.Path, span.StartLinePosition.Line + 1, message, "PATHFINDER102");
    }

    private static string BuildUrlTemplate(string? classRoute, string? methodTemplate,
        string controllerName, string actionName, string? area)
    {
        string template;

        if (!string.IsNullOrEmpty(methodTemplate) && (methodTemplate.StartsWith('/') || methodTemplate.StartsWith("~/", StringComparison.Ordinal)))
            template = methodTemplate.StartsWith("~/", StringComparison.Ordinal) ? methodTemplate[1..] : methodTemplate; // Absolute path overrides class route
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
        => RouteParameterDiscovery.Discover(method.ParameterList.Parameters, templateParts);

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
