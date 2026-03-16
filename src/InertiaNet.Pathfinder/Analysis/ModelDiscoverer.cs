using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using InertiaNet.Pathfinder.Generation;

namespace InertiaNet.Pathfinder.Analysis;

static class ModelDiscoverer
{
    private static readonly HashSet<string> PrimitiveTypes = new()
    {
        "string", "int", "long", "short", "byte", "float", "double", "decimal",
        "bool", "Guid", "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly",
        "String", "Int32", "Int64", "Int16", "Byte", "Single", "Double", "Decimal",
        "Boolean",
    };

    /// <summary>
    /// Discovers model types referenced by page props.
    /// Only generates types for models that are actually used in Inertia props.
    /// </summary>
    public static List<ModelInfo> Discover(SyntaxTree[] trees, List<PagePropsInfo> pageProps)
    {
        var models = new List<ModelInfo>();
        var classDeclarations = new Dictionary<string, ClassDeclarationSyntax>();
        var recordDeclarations = new Dictionary<string, RecordDeclarationSyntax>();
        var discovered = new HashSet<string>();

        // Index all declarations
        foreach (var tree in trees)
        {
            var root = tree.GetRoot();
            foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                classDeclarations[classDecl.Identifier.Text] = classDecl;
            foreach (var recordDecl in root.DescendantNodes().OfType<RecordDeclarationSyntax>())
                recordDeclarations[recordDecl.Identifier.Text] = recordDecl;
        }

        // Collect all type names referenced by props
        var referencedTypes = new HashSet<string>();
        foreach (var page in pageProps)
        {
            foreach (var prop in page.Props)
                CollectTypeReferences(prop.TypeScriptType, referencedTypes);
        }

        // Resolve each referenced type
        foreach (var typeName in referencedTypes)
        {
            if (discovered.Contains(typeName)) continue;
            if (PrimitiveTypes.Contains(typeName)) continue;
            if (TypeMapper.ToTypeScript(typeName) != "string | number") continue; // Already a known type

            if (classDeclarations.TryGetValue(typeName, out var classDecl))
            {
                var model = ExtractModelFromClass(classDecl);
                if (model != null)
                {
                    models.Add(model);
                    discovered.Add(typeName);
                }
            }
            else if (recordDeclarations.TryGetValue(typeName, out var recordDecl))
            {
                var model = ExtractModelFromRecord(recordDecl);
                if (model != null)
                {
                    models.Add(model);
                    discovered.Add(typeName);
                }
            }
        }

        return models;
    }

    private static void CollectTypeReferences(string tsType, HashSet<string> references)
    {
        // Extract type names from TypeScript type strings
        // e.g., "User" from "User", "Post" from "Post[]", "User" from "Record<string, User>"
        var cleaned = tsType
            .Replace("[]", "")
            .Replace(" | null", "")
            .Replace(" | undefined", "");

        // Skip built-in TS types
        if (cleaned is "string" or "number" or "boolean" or "unknown" or "null" or "undefined" or "any")
            return;

        // Handle Record<K, V> and other generics
        if (cleaned.Contains('<'))
        {
            var inner = cleaned[(cleaned.IndexOf('<') + 1)..cleaned.LastIndexOf('>')];
            foreach (var part in inner.Split(','))
                CollectTypeReferences(part.Trim(), references);
            return;
        }

        // Handle inline object types
        if (cleaned.StartsWith('{')) return;

        references.Add(cleaned);
    }

    private static ModelInfo? ExtractModelFromClass(ClassDeclarationSyntax classDecl)
    {
        var properties = new List<ModelProperty>();

        foreach (var prop in classDecl.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (!prop.Modifiers.Any(SyntaxKind.PublicKeyword)) continue;

            var (tsType, isNullable, isCollection) = MapModelPropertyType(prop.Type);
            properties.Add(new ModelProperty(
                NameUtils.ToCamelCase(prop.Identifier.Text),
                tsType,
                isNullable,
                isCollection));
        }

        if (properties.Count == 0) return null;

        return new ModelInfo(
            GetFullName(classDecl),
            classDecl.Identifier.Text,
            properties);
    }

    private static ModelInfo? ExtractModelFromRecord(RecordDeclarationSyntax recordDecl)
    {
        var properties = new List<ModelProperty>();

        // Primary constructor parameters
        if (recordDecl.ParameterList != null)
        {
            foreach (var param in recordDecl.ParameterList.Parameters)
            {
                if (param.Type == null) continue;
                var (tsType, isNullable, isCollection) = MapModelPropertyType(param.Type);
                properties.Add(new ModelProperty(
                    NameUtils.ToCamelCase(param.Identifier.Text),
                    tsType,
                    isNullable,
                    isCollection));
            }
        }

        // Properties
        foreach (var prop in recordDecl.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (!prop.Modifiers.Any(SyntaxKind.PublicKeyword)) continue;
            var (tsType, isNullable, isCollection) = MapModelPropertyType(prop.Type);
            properties.Add(new ModelProperty(
                NameUtils.ToCamelCase(prop.Identifier.Text),
                tsType,
                isNullable,
                isCollection));
        }

        if (properties.Count == 0) return null;

        return new ModelInfo(
            GetFullName(recordDecl),
            recordDecl.Identifier.Text,
            properties);
    }

    private static (string tsType, bool isNullable, bool isCollection) MapModelPropertyType(TypeSyntax type)
    {
        switch (type)
        {
            case NullableTypeSyntax nullable:
                var (innerType, _, isInnerCollection) = MapModelPropertyType(nullable.ElementType);
                return ($"{innerType} | null", true, isInnerCollection);

            case PredefinedTypeSyntax predefined:
                return (MapClrType(predefined.Keyword.Text), false, false);

            case ArrayTypeSyntax array:
                var (elementType, _, _) = MapModelPropertyType(array.ElementType);
                return ($"{elementType}[]", false, true);

            case GenericNameSyntax generic:
                return MapGenericModelType(generic);

            case IdentifierNameSyntax id:
                return (MapClrType(id.Identifier.Text), false, false);

            default:
                return ("unknown", false, false);
        }
    }

    private static (string tsType, bool isNullable, bool isCollection) MapGenericModelType(GenericNameSyntax generic)
    {
        var name = generic.Identifier.Text;
        var typeArgs = generic.TypeArgumentList;

        // Collections
        if (name is "List" or "IList" or "ICollection" or "IEnumerable" or "IReadOnlyList"
                or "IReadOnlyCollection" or "HashSet" or "ISet" && typeArgs.Arguments.Count == 1)
        {
            var (inner, _, _) = MapModelPropertyType(typeArgs.Arguments[0]);
            return ($"{inner}[]", false, true);
        }

        // Dictionary
        if (name is "Dictionary" or "IDictionary" or "IReadOnlyDictionary" && typeArgs.Arguments.Count == 2)
        {
            var (keyType, _, _) = MapModelPropertyType(typeArgs.Arguments[0]);
            var (valType, _, _) = MapModelPropertyType(typeArgs.Arguments[1]);
            return ($"Record<{keyType}, {valType}>", false, false);
        }

        // Nullable<T>
        if (name == "Nullable" && typeArgs.Arguments.Count == 1)
        {
            var (inner, _, isCol) = MapModelPropertyType(typeArgs.Arguments[0]);
            return ($"{inner} | null", true, isCol);
        }

        // Task<T>, ValueTask<T>
        if (name is "Task" or "ValueTask" && typeArgs.Arguments.Count == 1)
            return MapModelPropertyType(typeArgs.Arguments[0]);

        return ("unknown", false, false);
    }

    private static string MapClrType(string clrType)
    {
        var mapped = TypeMapper.ToTypeScript(clrType);
        if (mapped != "string | number")
            return mapped;

        // Additional model-specific mappings
        return clrType switch
        {
            "DateTime" or "DateTimeOffset" or "DateOnly" or "TimeOnly" => "string",
            "TimeSpan" => "string",
            "Uri" => "string",
            "object" => "unknown",
            _ => clrType // Return the type name itself — will reference the model interface
        };
    }

    private static string GetFullName(MemberDeclarationSyntax decl)
    {
        var identifier = decl switch
        {
            ClassDeclarationSyntax c => c.Identifier.Text,
            RecordDeclarationSyntax r => r.Identifier.Text,
            _ => "Unknown"
        };

        var parts = new List<string> { identifier };
        var parent = decl.Parent;

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
}
