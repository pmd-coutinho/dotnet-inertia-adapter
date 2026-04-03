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
        var stringValues = DiscoverStringValues(root);

        // Track MapGroup variable assignments: variable name → prefix
        var groupPrefixes = DiscoverGroupPrefixes(root, stringValues);

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

            var lineSpan = invocation.GetLocation().GetLineSpan();
            var sourceFile = lineSpan.Path;
            var sourceLine = lineSpan.StartLinePosition.Line + 1;

            // First arg: route template
            var routeTemplate = ResolveStringExpression(args[0].Expression, stringValues);
            if (routeTemplate == null)
            {
                PathfinderDiagnostics.Report(
                    sourceFile,
                    sourceLine,
                    $"Skipped minimal API route '{methodName}' because the route template could not be resolved statically.");
                continue;
            }

            // Resolve group prefix from the expression the method is called on
            var prefix = ResolvePrefix(memberAccess.Expression, groupPrefixes, stringValues);
            if (memberAccess.Expression is InvocationExpressionSyntax && prefix == null)
            {
                PathfinderDiagnostics.Report(
                    sourceFile,
                    sourceLine,
                    $"Skipped minimal API route '{routeTemplate}' because its inline MapGroup chain could not be resolved statically.");
                continue;
            }

            if (prefix != null)
                routeTemplate = prefix.TrimEnd('/') + "/" + routeTemplate.TrimStart('/');

            if (!routeTemplate.StartsWith('/'))
                routeTemplate = "/" + routeTemplate;

            // Parse template parameters before stripping constraints (need constraints for type mapping)
            var templateParts = RouteTemplateParser.Parse(routeTemplate);

            routeTemplate = RouteTemplateParser.StripConstraints(routeTemplate);
            var templateParamNames = templateParts.Select(p => p.Name.ToLowerInvariant()).ToHashSet();

            // Second arg: handler (lambda or method group)
            if (!IsSupportedHandler(args[1].Expression))
            {
                PathfinderDiagnostics.Report(
                    sourceFile,
                    sourceLine,
                    $"Skipped minimal API route '{routeTemplate}' because only lambda handlers are currently supported.");
                continue;
            }

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

    private static Dictionary<string, string> DiscoverGroupPrefixes(SyntaxNode root, Dictionary<string, string> stringValues)
    {
        var prefixes = new Dictionary<string, string>();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                continue;

            if (memberAccess.Name.Identifier.Text != "MapGroup")
                continue;

            var args = invocation.ArgumentList.Arguments;
            if (args.Count < 1)
                continue;

            var groupPrefix = ResolveStringExpression(args[0].Expression, stringValues);
            if (groupPrefix == null)
                continue;

            // Resolve parent prefix
            var parentPrefix = ResolvePrefix(memberAccess.Expression, prefixes, stringValues);
            if (parentPrefix != null)
                groupPrefix = parentPrefix.TrimEnd('/') + "/" + groupPrefix.TrimStart('/');

            // Find the variable this is assigned to
            var variableName = IsNestedMapGroupInvocation(invocation) ? null : GetAssignedVariable(invocation);
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

    private static bool IsNestedMapGroupInvocation(InvocationExpressionSyntax invocation)
        => invocation.Parent is MemberAccessExpressionSyntax parentMember &&
           parentMember.Name.Identifier.Text == "MapGroup" &&
           parentMember.Parent is InvocationExpressionSyntax;

    private static string? ResolvePrefix(
        ExpressionSyntax expression,
        Dictionary<string, string> groupPrefixes,
        Dictionary<string, string> stringValues)
    {
        if (expression is IdentifierNameSyntax identifier)
        {
            return groupPrefixes.GetValueOrDefault(identifier.Identifier.Text)
                ?? stringValues.GetValueOrDefault(identifier.Identifier.Text);
        }

        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text == "MapGroup" &&
            invocation.ArgumentList.Arguments.Count > 0)
        {
            var prefix = ResolvePrefix(memberAccess.Expression, groupPrefixes, stringValues);
            var segment = ResolveStringExpression(invocation.ArgumentList.Arguments[0].Expression, stringValues);

            if (segment == null)
                return null;

            return prefix == null
                ? segment
                : prefix.TrimEnd('/') + "/" + segment.TrimStart('/');
        }

        return null;
    }

    private static Dictionary<string, string> DiscoverStringValues(SyntaxNode root)
    {
        var bindings = new List<(string Key, ExpressionSyntax Expression)>();

        foreach (var local in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            foreach (var variable in local.Declaration.Variables)
            {
                if (variable.Initializer?.Value != null)
                    bindings.Add((variable.Identifier.Text, variable.Initializer.Value));
            }
        }

        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            var typeName = (field.Parent as TypeDeclarationSyntax)?.Identifier.Text;

            foreach (var variable in field.Declaration.Variables)
            {
                if (variable.Initializer?.Value == null)
                    continue;

                bindings.Add((variable.Identifier.Text, variable.Initializer.Value));

                if (!string.IsNullOrWhiteSpace(typeName))
                    bindings.Add(($"{typeName}.{variable.Identifier.Text}", variable.Initializer.Value));
            }
        }

        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        var progress = true;

        while (progress)
        {
            progress = false;

            foreach (var (key, expression) in bindings)
            {
                if (resolved.ContainsKey(key))
                    continue;

                var value = ResolveStringExpression(expression, resolved);
                if (value == null)
                    continue;

                resolved[key] = value;
                progress = true;
            }
        }

        return resolved;
    }

    private static string? ResolveStringExpression(ExpressionSyntax expression, Dictionary<string, string> stringValues)
    {
        return expression switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression)
                => literal.Token.ValueText,
            IdentifierNameSyntax identifier
                => stringValues.GetValueOrDefault(identifier.Identifier.Text),
            MemberAccessExpressionSyntax memberAccess
                => stringValues.GetValueOrDefault(memberAccess.ToString()),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression)
                => ResolveBinaryString(binary, stringValues),
            InterpolatedStringExpressionSyntax interpolated
                => ResolveInterpolatedString(interpolated, stringValues),
            ParenthesizedExpressionSyntax parenthesized
                => ResolveStringExpression(parenthesized.Expression, stringValues),
            _ => null,
        };
    }

    private static string? ResolveBinaryString(BinaryExpressionSyntax binary, Dictionary<string, string> stringValues)
    {
        var left = ResolveStringExpression(binary.Left, stringValues);
        var right = ResolveStringExpression(binary.Right, stringValues);

        return left == null || right == null ? null : left + right;
    }

    private static string? ResolveInterpolatedString(
        InterpolatedStringExpressionSyntax interpolated,
        Dictionary<string, string> stringValues)
    {
        var parts = new List<string>();

        foreach (var content in interpolated.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    parts.Add(text.TextToken.ValueText);
                    break;
                case InterpolationSyntax interpolation:
                {
                    var resolved = ResolveStringExpression(interpolation.Expression, stringValues);
                    if (resolved == null)
                        return null;

                    parts.Add(resolved);
                    break;
                }
                default:
                    return null;
            }
        }

        return string.Concat(parts);
    }

    private static bool IsSupportedHandler(ExpressionSyntax handler)
        => handler is ParenthesizedLambdaExpressionSyntax or SimpleLambdaExpressionSyntax;

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
