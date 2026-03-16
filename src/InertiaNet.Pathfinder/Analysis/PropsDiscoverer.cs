using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using InertiaNet.Pathfinder.Generation;

namespace InertiaNet.Pathfinder.Analysis;

static class PropsDiscoverer
{
    /// <summary>
    /// Discovers Inertia page props from Render() calls across all syntax trees.
    /// Needs all trees to resolve generic type arguments.
    /// </summary>
    public static List<PagePropsInfo> Discover(SyntaxTree[] trees)
    {
        var pageProps = new List<PagePropsInfo>();
        var classDeclarations = new Dictionary<string, ClassDeclarationSyntax>();
        var recordDeclarations = new Dictionary<string, RecordDeclarationSyntax>();

        // Pre-scan: index all class and record declarations for type resolution
        foreach (var tree in trees)
        {
            var root = tree.GetRoot();
            foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                classDeclarations[classDecl.Identifier.Text] = classDecl;
            foreach (var recordDecl in root.DescendantNodes().OfType<RecordDeclarationSyntax>())
                recordDeclarations[recordDecl.Identifier.Text] = recordDecl;
        }

        foreach (var tree in trees)
        {
            var root = tree.GetRoot();

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var methodName = GetMethodName(invocation);
                if (methodName is not ("Render" or "Inertia"))
                    continue;

                // Find the component name (first string literal arg) and props (next arg after it)
                // Handles both:
                //   this.Inertia("Component", new { ... })           → args[0]=string, args[1]=props
                //   ControllerExtensions.Inertia(this, "Component", new { ... }) → args[0]=this, args[1]=string, args[2]=props
                //   inertia.Render("Component", new { ... })         → args[0]=string, args[1]=props
                var args = invocation.ArgumentList.Arguments;
                if (args.Count == 0) continue;

                string? componentName = null;
                int componentArgIndex = -1;

                for (var i = 0; i < args.Count; i++)
                {
                    if (args[i].Expression is LiteralExpressionSyntax literal &&
                        literal.IsKind(SyntaxKind.StringLiteralExpression))
                    {
                        componentName = literal.Token.ValueText;
                        componentArgIndex = i;
                        break;
                    }
                }

                if (componentName == null) continue;

                var propsArgIndex = componentArgIndex + 1;

                // Check for generic type argument: Render<MyProps>("Component")
                var genericName = GetGenericTypeArgument(invocation);
                if (genericName != null)
                {
                    var props = ResolvePropsFromType(genericName, classDeclarations, recordDeclarations);
                    if (props.Count > 0)
                    {
                        pageProps.Add(new PagePropsInfo(componentName, props));
                        continue;
                    }
                }

                // Check for anonymous object: Render("Component", new { ... })
                if (args.Count > propsArgIndex && args[propsArgIndex].Expression is AnonymousObjectCreationExpressionSyntax anonObj)
                {
                    var props = ResolvePropsFromAnonymousObject(anonObj);
                    if (props.Count > 0)
                        pageProps.Add(new PagePropsInfo(componentName, props));
                    continue;
                }

                // Check for named object: Render("Component", new SomeType { ... })
                if (args.Count > propsArgIndex && args[propsArgIndex].Expression is ObjectCreationExpressionSyntax objCreation)
                {
                    var typeName = GetTypeNameFromExpression(objCreation.Type);
                    if (typeName != null)
                    {
                        var props = ResolvePropsFromType(typeName, classDeclarations, recordDeclarations);
                        if (props.Count > 0)
                            pageProps.Add(new PagePropsInfo(componentName, props));
                    }
                }
            }
        }

        // Deduplicate by component name (keep the one with the most props)
        return pageProps
            .GroupBy(p => p.ComponentName)
            .Select(g => g.OrderByDescending(p => p.Props.Count).First())
            .ToList();
    }

    private static string? GetMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name switch
            {
                GenericNameSyntax generic => generic.Identifier.Text,
                IdentifierNameSyntax id => id.Identifier.Text,
                _ => null
            },
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => null
        };
    }

    private static string? GetGenericTypeArgument(InvocationExpressionSyntax invocation)
    {
        GenericNameSyntax? genericName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess =>
                memberAccess.Name as GenericNameSyntax,
            _ => null
        };

        if (genericName?.TypeArgumentList.Arguments.Count == 1)
        {
            return GetTypeNameFromExpression(genericName.TypeArgumentList.Arguments[0]);
        }

        return null;
    }

    private static string? GetTypeNameFromExpression(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            QualifiedNameSyntax q => q.Right.Identifier.Text,
            _ => null
        };
    }

    private static List<PropField> ResolvePropsFromType(string typeName,
        Dictionary<string, ClassDeclarationSyntax> classes,
        Dictionary<string, RecordDeclarationSyntax> records)
    {
        var props = new List<PropField>();

        if (classes.TryGetValue(typeName, out var classDecl))
        {
            props.AddRange(ExtractPropertiesFromClass(classDecl));
        }
        else if (records.TryGetValue(typeName, out var recordDecl))
        {
            props.AddRange(ExtractPropertiesFromRecord(recordDecl));
        }

        return props;
    }

    private static List<PropField> ExtractPropertiesFromClass(ClassDeclarationSyntax classDecl)
    {
        var props = new List<PropField>();

        foreach (var prop in classDecl.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (!prop.Modifiers.Any(SyntaxKind.PublicKeyword)) continue;

            var name = NameUtils.ToCamelCase(prop.Identifier.Text);
            var (tsType, isNullable) = MapPropertyType(prop.Type);
            var isDeferred = IsWrappedInDeferType(prop.Type);
            var isOptional = isNullable || isDeferred ||
                             HasAttribute(prop.AttributeLists, "Optional", "OptionalAttribute");

            props.Add(new PropField(name, tsType, isOptional, isDeferred));
        }

        return props;
    }

    private static List<PropField> ExtractPropertiesFromRecord(RecordDeclarationSyntax recordDecl)
    {
        var props = new List<PropField>();

        // Record parameters (primary constructor)
        if (recordDecl.ParameterList != null)
        {
            foreach (var param in recordDecl.ParameterList.Parameters)
            {
                if (param.Type == null) continue;
                var name = NameUtils.ToCamelCase(param.Identifier.Text);
                var (tsType, isNullable) = MapPropertyType(param.Type);
                var isDeferred = IsWrappedInDeferType(param.Type);
                props.Add(new PropField(name, tsType, isNullable || isDeferred, isDeferred));
            }
        }

        // Record properties
        foreach (var prop in recordDecl.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (!prop.Modifiers.Any(SyntaxKind.PublicKeyword)) continue;

            var name = NameUtils.ToCamelCase(prop.Identifier.Text);
            var (tsType, isNullable) = MapPropertyType(prop.Type);
            var isDeferred = IsWrappedInDeferType(prop.Type);
            props.Add(new PropField(name, tsType, isNullable || isDeferred, isDeferred));
        }

        return props;
    }

    private static List<PropField> ResolvePropsFromAnonymousObject(
        AnonymousObjectCreationExpressionSyntax anonObj)
    {
        var props = new List<PropField>();

        foreach (var init in anonObj.Initializers)
        {
            var name = init.NameEquals?.Name.Identifier.Text;
            if (name == null) continue;

            var tsType = InferTypeFromExpression(init.Expression);
            var isDeferred = IsExpressionDeferred(init.Expression);

            props.Add(new PropField(
                NameUtils.ToCamelCase(name),
                tsType,
                isDeferred,
                isDeferred));
        }

        return props;
    }

    private static (string tsType, bool isNullable) MapPropertyType(TypeSyntax type)
    {
        switch (type)
        {
            case NullableTypeSyntax nullable:
                var (innerType, _) = MapPropertyType(nullable.ElementType);
                return ($"{innerType} | null", true);

            case PredefinedTypeSyntax predefined:
                return (TypeMapper.ToTypeScript(predefined.Keyword.Text), false);

            case ArrayTypeSyntax array:
                var (elementType, _) = MapPropertyType(array.ElementType);
                return ($"{elementType}[]", false);

            case GenericNameSyntax generic:
                var genericName = generic.Identifier.Text;
                return MapGenericType(genericName, generic.TypeArgumentList);

            case IdentifierNameSyntax id:
                var idName = id.Identifier.Text;
                var mapped = TypeMapper.ToTypeScript(idName);
                // If TypeMapper returned "string | number" it means it didn't recognize it — use the type name
                if (mapped == "string | number")
                    return (idName, false);
                return (mapped, false);

            default:
                return ("unknown", false);
        }
    }

    private static (string tsType, bool isNullable) MapGenericType(string genericName,
        TypeArgumentListSyntax typeArgs)
    {
        // Unwrap Func<T>, Lazy<T> — these are prop wrappers
        if (genericName is "Func" or "Lazy" && typeArgs.Arguments.Count == 1)
            return MapPropertyType(typeArgs.Arguments[0]);

        // Collections → T[]
        if (genericName is "List" or "IList" or "ICollection" or "IEnumerable" or "IReadOnlyList"
                or "IReadOnlyCollection" or "HashSet" or "ISet" && typeArgs.Arguments.Count == 1)
        {
            var (inner, _) = MapPropertyType(typeArgs.Arguments[0]);
            return ($"{inner}[]", false);
        }

        // Dictionary → Record<K, V>
        if (genericName is "Dictionary" or "IDictionary" or "IReadOnlyDictionary" && typeArgs.Arguments.Count == 2)
        {
            var (keyType, _) = MapPropertyType(typeArgs.Arguments[0]);
            var (valType, _) = MapPropertyType(typeArgs.Arguments[1]);
            return ($"Record<{keyType}, {valType}>", false);
        }

        // Task<T>, ValueTask<T> — unwrap
        if (genericName is "Task" or "ValueTask" && typeArgs.Arguments.Count == 1)
            return MapPropertyType(typeArgs.Arguments[0]);

        // Nullable<T>
        if (genericName == "Nullable" && typeArgs.Arguments.Count == 1)
        {
            var (inner, _) = MapPropertyType(typeArgs.Arguments[0]);
            return ($"{inner} | null", true);
        }

        return ("unknown", false);
    }

    private static bool IsWrappedInDeferType(TypeSyntax type)
    {
        if (type is GenericNameSyntax generic)
        {
            var name = generic.Identifier.Text;
            return name is "DeferProp" or "OptionalProp";
        }
        return false;
    }

    private static bool IsExpressionDeferred(ExpressionSyntax expression)
    {
        // Check for Inertia.Defer(() => ...) or Inertia.Optional(() => ...) patterns
        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var methodName = memberAccess.Name.Identifier.Text;
            return methodName is "Defer" or "Optional" or "Lazy";
        }
        return false;
    }

    private static string InferTypeFromExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            LiteralExpressionSyntax literal => literal.Kind() switch
            {
                SyntaxKind.StringLiteralExpression => "string",
                SyntaxKind.NumericLiteralExpression => "number",
                SyntaxKind.TrueLiteralExpression => "boolean",
                SyntaxKind.FalseLiteralExpression => "boolean",
                SyntaxKind.NullLiteralExpression => "null",
                _ => "unknown"
            },
            AnonymousObjectCreationExpressionSyntax nested =>
                InferAnonymousObjectType(nested),
            ArrayCreationExpressionSyntax => "unknown[]",
            ImplicitArrayCreationExpressionSyntax => "unknown[]",
            CollectionExpressionSyntax => "unknown[]",
            InvocationExpressionSyntax invocation => InferFromInvocation(invocation),
            _ => "unknown"
        };
    }

    private static string InferAnonymousObjectType(AnonymousObjectCreationExpressionSyntax anonObj)
    {
        var fields = new List<string>();
        foreach (var init in anonObj.Initializers)
        {
            var name = init.NameEquals?.Name.Identifier.Text;
            if (name == null) continue;
            var tsType = InferTypeFromExpression(init.Expression);
            fields.Add($"{NameUtils.ToCamelCase(name)}: {tsType}");
        }
        return $"{{ {string.Join("; ", fields)} }}";
    }

    private static string InferFromInvocation(InvocationExpressionSyntax invocation)
    {
        // Try to detect Defer/Optional wrappers
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var methodName = memberAccess.Name.Identifier.Text;
            if (methodName is "Defer" or "Optional" or "Lazy")
                return "unknown"; // Deferred — type will be resolved later
        }
        return "unknown";
    }

    private static bool HasAttribute(SyntaxList<AttributeListSyntax> attrLists, params string[] names)
    {
        return attrLists.SelectMany(al => al.Attributes)
            .Any(a =>
            {
                var attrName = a.Name switch
                {
                    IdentifierNameSyntax id => id.Identifier.Text,
                    QualifiedNameSyntax q => q.Right.Identifier.Text,
                    _ => a.Name.ToString()
                };
                return names.Contains(attrName);
            });
    }
}
