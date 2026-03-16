using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace InertiaNet.Pathfinder.Analysis;

static class MinimalApiRouteDiscoverer
{
    private static readonly Dictionary<string, string[]> MapMethodToHttp = new()
    {
        ["MapGet"] = ["get", "head"],
        ["MapPost"] = ["post"],
        ["MapPut"] = ["put"],
        ["MapPatch"] = ["patch"],
        ["MapDelete"] = ["delete"],
    };

    private static readonly HashSet<string> SkipParameterTypes = new()
    {
        "HttpContext", "ClaimsPrincipal", "CancellationToken",
    };

    private static readonly HashSet<string> SkipParameterAttributes = new()
    {
        "FromServices", "FromServicesAttribute",
        "FromBody", "FromBodyAttribute",
        "FromQuery", "FromQueryAttribute",
        "FromHeader", "FromHeaderAttribute",
    };

    public static List<RouteInfo> Discover(SyntaxTree tree)
    {
        var routes = new List<RouteInfo>();
        var root = tree.GetRoot();

        // Track MapGroup variable assignments: variable name → prefix
        var groupPrefixes = DiscoverGroupPrefixes(root);

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                continue;

            var methodName = memberAccess.Name.Identifier.Text;

            if (!MapMethodToHttp.TryGetValue(methodName, out var httpMethods))
                continue;

            var args = invocation.ArgumentList.Arguments;
            if (args.Count < 2)
                continue;

            // First arg: route template
            if (args[0].Expression is not LiteralExpressionSyntax routeLiteral)
                continue;

            var routeTemplate = routeLiteral.Token.ValueText;

            // Resolve group prefix from the expression the method is called on
            var prefix = ResolvePrefix(memberAccess.Expression, groupPrefixes);
            if (prefix != null)
                routeTemplate = prefix.TrimEnd('/') + "/" + routeTemplate.TrimStart('/');

            if (!routeTemplate.StartsWith('/'))
                routeTemplate = "/" + routeTemplate;

            // Parse template parameters before stripping constraints (need constraints for type mapping)
            var templateParts = RouteTemplateParser.Parse(routeTemplate);

            routeTemplate = RouteTemplateParser.StripConstraints(routeTemplate);
            var templateParamNames = templateParts.Select(p => p.Name.ToLowerInvariant()).ToHashSet();

            // Second arg: handler (lambda or method group)
            var parameters = ExtractHandlerParameters(args[1].Expression, templateParamNames, templateParts);

            // Extract [FromBody] parameter type
            var bodyTypeName = ExtractBodyTypeName(args[1].Expression);

            // Check for .WithName("...") fluent call
            var routeName = FindWithName(invocation);

            // Check for .RequireHost("...") fluent call
            var hostValue = FindRequireHost(invocation);
            string? domain = null;
            string? scheme = null;

            if (hostValue != null)
            {
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

            // Capture source file and line
            var lineSpan = invocation.GetLocation().GetLineSpan();
            var sourceFile = lineSpan.Path;
            var sourceLine = lineSpan.StartLinePosition.Line + 1;

            routes.Add(new RouteInfo(
                ControllerFullName: "__MinimalApi__",
                ControllerShortName: "__MinimalApi__",
                ActionName: GenerateActionName(routeTemplate, methodName),
                HttpMethods: httpMethods,
                UrlTemplate: routeTemplate,
                Parameters: parameters,
                RouteName: routeName,
                SourceFile: sourceFile,
                SourceLine: sourceLine,
                BodyTypeName: bodyTypeName,
                Domain: domain,
                Scheme: scheme));
        }

        return routes;
    }

    private static Dictionary<string, string> DiscoverGroupPrefixes(SyntaxNode root)
    {
        var prefixes = new Dictionary<string, string>();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                continue;

            if (memberAccess.Name.Identifier.Text != "MapGroup")
                continue;

            var args = invocation.ArgumentList.Arguments;
            if (args.Count < 1 || args[0].Expression is not LiteralExpressionSyntax literal)
                continue;

            var groupPrefix = literal.Token.ValueText;

            // Resolve parent prefix
            var parentPrefix = ResolvePrefix(memberAccess.Expression, prefixes);
            if (parentPrefix != null)
                groupPrefix = parentPrefix.TrimEnd('/') + "/" + groupPrefix.TrimStart('/');

            // Find the variable this is assigned to
            var variableName = GetAssignedVariable(invocation);
            if (variableName != null)
                prefixes[variableName] = groupPrefix;
        }

        return prefixes;
    }

    private static string? GetAssignedVariable(SyntaxNode node)
    {
        var parent = node.Parent;

        // var x = app.MapGroup(...)
        while (parent != null)
        {
            if (parent is VariableDeclaratorSyntax declarator)
                return declarator.Identifier.Text;

            if (parent is AssignmentExpressionSyntax assignment &&
                assignment.Left is IdentifierNameSyntax id)
                return id.Identifier.Text;

            // Go up through fluent calls
            if (parent is InvocationExpressionSyntax or MemberAccessExpressionSyntax or
                EqualsValueClauseSyntax)
            {
                parent = parent.Parent;
                continue;
            }

            break;
        }

        return null;
    }

    private static string? ResolvePrefix(ExpressionSyntax expression, Dictionary<string, string> groupPrefixes)
    {
        if (expression is IdentifierNameSyntax identifier)
        {
            return groupPrefixes.GetValueOrDefault(identifier.Identifier.Text);
        }

        return null;
    }

    private static RouteParameter[] ExtractHandlerParameters(ExpressionSyntax handler,
        HashSet<string> templateParamNames, List<RouteTemplateParser.TemplatePart> templateParts)
    {
        var parameters = new List<RouteParameter>();

        IEnumerable<ParameterSyntax>? paramList = handler switch
        {
            ParenthesizedLambdaExpressionSyntax lambda => lambda.ParameterList.Parameters,
            SimpleLambdaExpressionSyntax simple => [simple.Parameter],
            _ => null
        };

        if (paramList == null)
            return [];

        foreach (var param in paramList)
        {
            var paramName = param.Identifier.Text;

            // Skip known service types
            var typeName = param.Type?.ToString();
            if (typeName != null && SkipParameterTypes.Contains(typeName))
                continue;

            // Skip params with excluded attributes
            if (param.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => SkipParameterAttributes.Contains(GetAttributeName(a))))
                continue;

            // Only include if it matches a template placeholder
            if (!templateParamNames.Contains(paramName.ToLowerInvariant()))
                continue;

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

    private static string GetAttributeName(AttributeSyntax attr)
    {
        return attr.Name switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            QualifiedNameSyntax q => q.Right.Identifier.Text,
            _ => attr.Name.ToString()
        };
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

    private static string? FindWithName(InvocationExpressionSyntax invocation)
    {
        // Look for .WithName("...") in the fluent chain above
        var parent = invocation.Parent;

        while (parent != null)
        {
            if (parent is InvocationExpressionSyntax parentInvocation &&
                parentInvocation.Expression is MemberAccessExpressionSyntax parentMember &&
                parentMember.Name.Identifier.Text == "WithName" &&
                parentInvocation.ArgumentList.Arguments.Count > 0 &&
                parentInvocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax nameLiteral)
            {
                return nameLiteral.Token.ValueText;
            }

            // Also check if the invocation itself is the expression being called .WithName on
            if (parent is MemberAccessExpressionSyntax ma &&
                ma.Expression == invocation &&
                ma.Name.Identifier.Text == "WithName" &&
                ma.Parent is InvocationExpressionSyntax withNameCall &&
                withNameCall.ArgumentList.Arguments.Count > 0 &&
                withNameCall.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax lit)
            {
                return lit.Token.ValueText;
            }

            parent = parent.Parent;
        }

        return null;
    }

    private static string? FindRequireHost(InvocationExpressionSyntax invocation)
    {
        // Look for .RequireHost("...") in the fluent chain above
        var parent = invocation.Parent;

        while (parent != null)
        {
            if (parent is InvocationExpressionSyntax parentInvocation &&
                parentInvocation.Expression is MemberAccessExpressionSyntax parentMember &&
                parentMember.Name.Identifier.Text == "RequireHost" &&
                parentInvocation.ArgumentList.Arguments.Count > 0 &&
                parentInvocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax hostLiteral)
            {
                return hostLiteral.Token.ValueText;
            }

            if (parent is MemberAccessExpressionSyntax ma &&
                ma.Expression == invocation &&
                ma.Name.Identifier.Text == "RequireHost" &&
                ma.Parent is InvocationExpressionSyntax requireHostCall &&
                requireHostCall.ArgumentList.Arguments.Count > 0 &&
                requireHostCall.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax lit)
            {
                return lit.Token.ValueText;
            }

            parent = parent.Parent;
        }

        return null;
    }

    private static string? ExtractBodyTypeName(ExpressionSyntax handler)
    {
        IEnumerable<ParameterSyntax>? paramList = handler switch
        {
            ParenthesizedLambdaExpressionSyntax lambda => lambda.ParameterList.Parameters,
            SimpleLambdaExpressionSyntax simple => [simple.Parameter],
            _ => null
        };

        if (paramList == null) return null;

        foreach (var param in paramList)
        {
            var hasFromBody = param.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => GetAttributeName(a) is "FromBody" or "FromBodyAttribute");

            if (hasFromBody && param.Type != null)
            {
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
        }

        return null;
    }

    private static string GenerateActionName(string routeTemplate, string mapMethod)
    {
        var allSegments = routeTemplate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var verb = mapMethod.Replace("Map", "").ToLowerInvariant();

        // Build name from non-parameter segments
        var nameSegments = allSegments.Where(s => !s.StartsWith('{')).ToArray();

        if (nameSegments.Length == 0)
            return verb;

        var baseName = string.Join("", nameSegments.Select(s =>
            char.ToUpperInvariant(s[0]) + s[1..]));

        // Include parameter names to disambiguate (e.g., getUsers vs getUsersById)
        var paramSegments = allSegments
            .Where(s => s.StartsWith('{'))
            .Select(s => s.Trim('{', '}', '?'))
            .ToArray();

        var suffix = paramSegments.Length > 0
            ? "By" + string.Join("And", paramSegments.Select(p =>
                char.ToUpperInvariant(p[0]) + p[1..]))
            : "";

        return $"{verb}{baseName}{suffix}";
    }
}
