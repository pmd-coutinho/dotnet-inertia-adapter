using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace InertiaNet.Pathfinder.Analysis;

static class MinimalApiRouteDiscoverer
{
    private static readonly Dictionary<string, string[]> MapMethodToHttp = new()
    {
        ["MapGet"] = ["get"],
        ["MapInertia"] = ["get"],
        ["MapMethods"] = [],
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

    public static List<RouteInfo> Discover(SyntaxTree tree, SemanticModel? semantic = null)
    {
        var routes = new List<RouteInfo>();
        var root = tree.GetRoot();
        var stringValues = DiscoverStringValues(root, semantic);

        // Track MapGroup variable assignments: variable name → prefix
        var groupPrefixes = DiscoverGroupPrefixes(root, stringValues, semantic);

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                continue;

            var methodName = memberAccess.Name.Identifier.Text;

            if (!MapMethodToHttp.TryGetValue(methodName, out var httpMethods))
                continue;

            var args = invocation.ArgumentList.Arguments;
            if (args.Count < (methodName == "MapMethods" ? 3 : 2))
                continue;

            var lineSpan = invocation.GetLocation().GetLineSpan();
            var sourceFile = lineSpan.Path;
            var sourceLine = lineSpan.StartLinePosition.Line + 1;

            // First arg: route template
            var routeTemplate = semantic?.GetConstantValue(args[0].Expression).Value as string
                ?? ResolveStringExpression(args[0].Expression, stringValues);
            if (routeTemplate == null)
            {
                PathfinderDiagnostics.Report(
                    sourceFile,
                    sourceLine,
                    $"Skipped minimal API route '{methodName}' because the route template could not be resolved statically.");
                continue;
            }

            // Resolve group prefix from the expression the method is called on
            var prefix = ResolvePrefix(memberAccess.Expression, groupPrefixes, stringValues, semantic);
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

            string? component = null;
            RouteParameter[] parameters;
            string? bodyTypeName = null;
            if (methodName == "MapInertia")
            {
                component = ResolveStringExpression(args[1].Expression, stringValues, semantic);
                parameters = RouteParameterDiscovery.Discover([], templateParts);
            }
            else
            {
                if (methodName == "MapMethods")
                {
                    httpMethods = ResolveHttpMethods(args[1].Expression, semantic);
                    if (httpMethods.Length == 0)
                    {
                        PathfinderDiagnostics.Report(sourceFile, sourceLine, "Skipped MapMethods route because its HTTP methods could not be resolved statically.", "PATHFINDER102");
                        continue;
                    }
                }
                var handlerExpression = args[methodName == "MapMethods" ? 2 : 1].Expression;
                var handler = ResolveSemanticHandler(handlerExpression, semantic) ?? ResolveHandler(handlerExpression, root);
                if (handler == null)
                {
                    PathfinderDiagnostics.Report(sourceFile, sourceLine,
                        $"Skipped minimal API route '{routeTemplate}' because the handler could not be resolved to a supported lambda or method group.", "PATHFINDER101");
                    continue;
                }
                parameters = ExtractHandlerParameters(handler.Parameters, templateParamNames, templateParts);
                bodyTypeName = ExtractBodyTypeName(handler.Parameters);
            }

            // Check for .WithName("...") fluent call
            var routeName = FindWithName(invocation, semantic);

            // Check for .RequireHost("...") fluent call
            var hostValue = FindRequireHost(invocation, semantic);
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
                Scheme: scheme,
                ComponentName: component,
                SourceSpanStart: invocation.SpanStart));
        }

        return routes;
    }

    private static string[] ResolveHttpMethods(ExpressionSyntax expression, SemanticModel? semantic)
    {
        var elements = expression switch
        {
            ImplicitArrayCreationExpressionSyntax array => array.Initializer.Expressions.ToArray(),
            ArrayCreationExpressionSyntax array => array.Initializer?.Expressions.ToArray() ?? [],
            CollectionExpressionSyntax collection => collection.Elements.OfType<ExpressionElementSyntax>().Select(element => element.Expression).ToArray(),
            _ => [],
        };
        string? Method(ExpressionSyntax element)
        {
            if (element is LiteralExpressionSyntax literal) return literal.Token.ValueText.ToLowerInvariant();
            if (semantic?.GetConstantValue(element).Value is string value) return value.ToLowerInvariant();
            var symbol = semantic?.GetSymbolInfo(element).Symbol;
            return symbol?.ContainingType?.ToDisplayString() == "Microsoft.AspNetCore.Http.HttpMethods" ? symbol.Name.ToLowerInvariant() : null;
        }
        if (elements.Length == 0 || elements.Any(element => Method(element) is null)) return [];
        var methods = elements.Select(element => Method(element)!).Distinct().ToArray();
        return methods.All(method => method is "get" or "head" or "post" or "put" or "patch" or "delete" or "options" or "trace" or "connect") ? methods : [];
    }

    private static Dictionary<string, string> DiscoverGroupPrefixes(SyntaxNode root, Dictionary<string, string> stringValues, SemanticModel? semantic)
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

            var groupPrefix = ResolveStringExpression(args[0].Expression, stringValues, semantic);
            if (groupPrefix == null)
                continue;

            // Resolve parent prefix
            var parentPrefix = ResolvePrefix(memberAccess.Expression, prefixes, stringValues, semantic);
            if (parentPrefix != null)
                groupPrefix = parentPrefix.TrimEnd('/') + "/" + groupPrefix.TrimStart('/');

            // Find the variable this is assigned to
            var variableName = IsNestedMapGroupInvocation(invocation) ? null : GetAssignedVariable(invocation, semantic);
            if (variableName != null)
                prefixes[variableName] = groupPrefix;
        }

        return prefixes;
    }

    private static string? GetAssignedVariable(SyntaxNode node, SemanticModel? semantic)
    {
        var parent = node.Parent;

        // var x = app.MapGroup(...)
        while (parent != null)
        {
            if (parent is VariableDeclaratorSyntax declarator)
                return GroupKey(declarator.Identifier.ValueText, semantic?.GetDeclaredSymbol(declarator));

            if (parent is AssignmentExpressionSyntax assignment &&
                assignment.Left is IdentifierNameSyntax id)
                return GroupKey(id.Identifier.ValueText, semantic?.GetSymbolInfo(id).Symbol);

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

    private static string GroupKey(string name, ISymbol? symbol)
        => symbol?.Locations.FirstOrDefault(location => location.IsInSource) is { } location
            ? name + "@" + location.SourceTree?.FilePath + ":" + location.SourceSpan.Start : name;

    private static bool IsNestedMapGroupInvocation(InvocationExpressionSyntax invocation)
        => invocation.Parent is MemberAccessExpressionSyntax parentMember &&
           parentMember.Name.Identifier.Text == "MapGroup" &&
           parentMember.Parent is InvocationExpressionSyntax;

    private static string? ResolvePrefix(
        ExpressionSyntax expression,
        Dictionary<string, string> groupPrefixes,
        Dictionary<string, string> stringValues, SemanticModel? semantic = null)
    {
        if (expression is IdentifierNameSyntax identifier)
        {
            return groupPrefixes.GetValueOrDefault(GroupKey(identifier.Identifier.ValueText, semantic?.GetSymbolInfo(identifier).Symbol))
                ?? stringValues.GetValueOrDefault(identifier.Identifier.Text);
        }

        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text == "MapGroup" &&
            invocation.ArgumentList.Arguments.Count > 0)
        {
            var prefix = ResolvePrefix(memberAccess.Expression, groupPrefixes, stringValues, semantic);
            var segment = ResolveStringExpression(invocation.ArgumentList.Arguments[0].Expression, stringValues, semantic);

            if (segment == null)
                return null;

            return prefix == null
                ? segment
                : prefix.TrimEnd('/') + "/" + segment.TrimStart('/');
        }

        return null;
    }

    private static Dictionary<string, string> DiscoverStringValues(SyntaxNode root, SemanticModel? semantic)
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

                var value = ResolveStringExpression(expression, resolved, semantic);
                if (value == null)
                    continue;

                resolved[key] = value;
                progress = true;
            }
        }

        return resolved;
    }

    private static string? ResolveStringExpression(ExpressionSyntax expression, Dictionary<string, string> stringValues, SemanticModel? semantic = null)
    {
        if (semantic?.GetConstantValue(expression).Value is string value) return value;
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

    private static ResolvedHandler? ResolveHandler(ExpressionSyntax handler, SyntaxNode root)
    {
        return handler switch
        {
            ParenthesizedLambdaExpressionSyntax lambda => new ResolvedHandler(lambda.ParameterList.Parameters),
            SimpleLambdaExpressionSyntax simple => new ResolvedHandler([simple.Parameter]),
            IdentifierNameSyntax identifier => ResolveMethodGroup(root, identifier.Identifier.Text),
            MemberAccessExpressionSyntax memberAccess => ResolveMemberMethodGroup(root, memberAccess),
            _ => null,
        };
    }

    private static ResolvedHandler? ResolveSemanticHandler(ExpressionSyntax handler, SemanticModel? semantic)
    {
        var method = semantic?.GetSymbolInfo(handler).Symbol as IMethodSymbol;
        if (method is null && semantic is not null)
        {
            var candidates = semantic.GetMemberGroup(handler).OfType<IMethodSymbol>().ToArray();
            if (candidates.Length == 1) method = candidates[0];
        }
        if (method is null) return null;
        var declaration = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        return declaration switch
        {
            MethodDeclarationSyntax syntax => new(syntax.ParameterList.Parameters),
            LocalFunctionStatementSyntax syntax => new(syntax.ParameterList.Parameters),
            _ => null,
        };
    }

    private static RouteParameter[] ExtractHandlerParameters(IEnumerable<ParameterSyntax> paramList,
        HashSet<string> templateParamNames, List<RouteTemplateParser.TemplatePart> templateParts)
        => RouteParameterDiscovery.Discover(paramList, templateParts);

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

    private static string? FindWithName(InvocationExpressionSyntax invocation, SemanticModel? semantic)
        => FindFluentString(invocation, "WithName", semantic);

    private static string? FindRequireHost(InvocationExpressionSyntax invocation, SemanticModel? semantic)
        => FindFluentString(invocation, "RequireHost", semantic);

    private static string? FindFluentString(InvocationExpressionSyntax invocation, string method, SemanticModel? semantic)
    {
        for (var parent = invocation.Parent; parent is MemberAccessExpressionSyntax or InvocationExpressionSyntax or ParenthesizedExpressionSyntax; parent = parent.Parent)
        {
            if (parent is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } candidate
                || member.Name.Identifier.ValueText != method || candidate.ArgumentList.Arguments.Count == 0) continue;
            var value = ResolveStringExpression(candidate.ArgumentList.Arguments[0].Expression, [], semantic);
            if (value is null)
            {
                var line = candidate.GetLocation().GetLineSpan();
                PathfinderDiagnostics.Report(line.Path, line.StartLinePosition.Line + 1, $"Cannot resolve {method} metadata statically.", "PATHFINDER100");
            }
            return value;
        }
        return null;
    }

    private static string? ExtractBodyTypeName(IEnumerable<ParameterSyntax> paramList)
    {
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

    private static ResolvedHandler? ResolveMethodGroup(SyntaxNode root, string methodName)
    {
        var localFunctions = root.DescendantNodes().OfType<LocalFunctionStatementSyntax>()
            .Where(local => local.Identifier.Text == methodName)
            .ToList();

        if (localFunctions.Count == 1)
            return new ResolvedHandler(localFunctions[0].ParameterList.Parameters);

        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.Text == methodName)
            .ToList();

        if (methods.Count == 1)
            return new ResolvedHandler(methods[0].ParameterList.Parameters);

        return null;
    }

    private static ResolvedHandler? ResolveMemberMethodGroup(SyntaxNode root, MemberAccessExpressionSyntax memberAccess)
    {
        if (memberAccess.Expression is not IdentifierNameSyntax container)
            return null;

        var matchingType = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(type => type.Identifier.Text == container.Identifier.Text);
        if (matchingType == null)
            return null;

        var methods = matchingType.Members.OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.Text == memberAccess.Name.Identifier.Text)
            .ToList();

        if (methods.Count == 1)
            return new ResolvedHandler(methods[0].ParameterList.Parameters);

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

    private sealed record ResolvedHandler(SeparatedSyntaxList<ParameterSyntax> Parameters);
}
