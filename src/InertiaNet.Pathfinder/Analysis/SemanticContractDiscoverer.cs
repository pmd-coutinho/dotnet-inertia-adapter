using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace InertiaNet.Pathfinder.Analysis;

internal sealed record SemanticContracts(List<EnumInfo> Enums, List<PagePropsInfo> Pages, List<ModelInfo> Models,
    List<RouteInfo> Routes, List<PropField> SharedProps);

/// <summary>Uses symbol identity and supported serializer/binder metadata instead of short-name syntax indexes.</summary>
internal sealed class SemanticContractDiscoverer
{
    private const string Output = "output", Input = "input", Query = "query", Form = "form";
    private readonly CSharpCompilation _compilation;
    private readonly PathfinderConfig _config;
    private readonly SerializationProfile _profile;
    private readonly SerializationProfile _inputProfile;
    private readonly List<PageUse> _pages = [];
    private readonly List<(string Key, ExpressionSyntax Value)> _shares = [];
    private readonly List<(ITypeSymbol Type, string Usage)> _seeds = [];
    private readonly Dictionary<string, (INamedTypeSymbol Type, string Usage)> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ModelInfo> _models = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EnumInfo> _enums = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly HashSet<string> _building = new(StringComparer.Ordinal);
    private readonly Dictionary<SyntaxTree, SemanticModel> _semantics = [];
    private sealed record PageUse(string Component, ITypeSymbol? Type, ExpressionSyntax? Value, SyntaxNode Source);
    private sealed record Member(ISymbol Symbol, ITypeSymbol Type, string Name, bool Optional, bool OmitNull, bool Deferred, ExpressionSyntax? Value);

    public SemanticContractDiscoverer(CSharpCompilation compilation, PathfinderConfig config, SerializationProfile? profile = null,
        SerializationProfile? inputProfile = null)
    {
        _compilation = compilation;
        _config = config;
        _profile = profile ?? new();
        _inputProfile = inputProfile ?? new ContractConfiguration().Input;
        void Index(CSharpCompilation current)
        {
            foreach (var tree in current.SyntaxTrees) _semantics.TryAdd(tree, current.GetSemanticModel(tree));
            foreach (var reference in current.References.OfType<CompilationReference>())
                if (reference.Compilation is CSharpCompilation dependency) Index(dependency);
        }
        Index(compilation);
    }

    public SemanticContracts Discover(IEnumerable<SyntaxTree> rootTrees, List<RouteInfo> routes)
    {
        var trees = rootTrees.ToArray();
        if (_config.ContractFile is null)
            foreach (var tree in trees)
                foreach (var node in tree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                             .Where(node => node.Name.Identifier.ValueText is "JsonSerializerOptions" or "SerializerOptions" or "ConfigureHttpJsonOptions" or "AddJsonOptions"))
                    Report(node, "Application JSON configuration may change the wire contract. Supply --contracts with independent output/input profiles.", "PATHFINDER228");
        foreach (var tree in trees)
        {
            var semantic = Semantic(tree);
            foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = MethodName(invocation);
                if (name is not ("Render" or "Inertia" or "Share" or "ShareOnce" or "ShareProps")) continue;
                if (!_config.GenerateProps) continue;
                if (!IsInertiaInvocation(invocation, semantic)) continue;
                var arguments = invocation.ArgumentList.Arguments;
                if (name is "Share" or "ShareOnce")
                {
                    if (arguments.Count > 1 && ConstantString(arguments[0].Expression, semantic) is { } key)
                    {
                        _shares.Add((key, arguments[1].Expression));
                        AddSeed(ExpressionType(arguments[1].Expression, semantic), Output);
                    }
                    continue;
                }
                if (name == "ShareProps")
                {
                    if (arguments.Count > 0 && ExpressionType(arguments[0].Expression, semantic) is { } type)
                    {
                        SeedProps(type, arguments[0].Expression);
                        _pages.Add(new("\0shared", type, arguments[0].Expression, invocation));
                    }
                    continue;
                }
                var componentIndex = -1;
                string? component = null;
                for (var index = 0; index < Math.Min(arguments.Count, 2); index++)
                {
                    component = ComponentName(arguments[index].Expression, semantic);
                    if (component is null) continue;
                    componentIndex = index;
                    break;
                }
                if (component is null) { Report(invocation, "Cannot resolve the component name statically; add an explicit InertiaComponent<TProps> declaration.", "PATHFINDER220"); continue; }
                var value = arguments.Count > componentIndex + 1 ? arguments[componentIndex + 1].Expression : null;
                ITypeSymbol? propsType = value is null ? null : ExpressionType(value, semantic);
                if (invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax generic } && generic.TypeArgumentList.Arguments.Count == 1)
                    propsType = semantic.GetTypeInfo(generic.TypeArgumentList.Arguments[0]).Type ?? propsType;
                if (propsType is null && semantic.GetTypeInfo(arguments[componentIndex].Expression).Type is INamedTypeSymbol { Name: "InertiaComponent", TypeArguments.Length: 1 } typedComponent)
                    propsType = typedComponent.TypeArguments[0];
                _pages.Add(new(component, propsType, value, invocation));
                if (propsType is not null) SeedProps(propsType, value);
            }
        }
        var routeHandlers = routes.Select(route => (Route: route, Parameters: HandlerParameters(route, trees))).ToArray();
        foreach (var (route, parameters) in routeHandlers)
            foreach (var parameter in parameters)
                if (ParameterUsage(parameter, route) is { } usage) AddSeed(parameter.Type, usage);
                else if (route.Parameters.Any(candidate => candidate.Name.Equals(BindingName(parameter, "FromRouteAttribute") ?? parameter.Name, StringComparison.OrdinalIgnoreCase)))
                    AddSeed(parameter.Type, Query);
        foreach (var tree in trees)
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<EnumDeclarationSyntax>())
                if (Semantic(tree).GetDeclaredSymbol(declaration) is INamedTypeSymbol type) AddSeed(type, Output);
        foreach (var (type, usage) in _seeds.ToArray()) Collect(type, usage);
        var entries = _types.Select(pair => (pair.Key, Preferred(pair.Value.Type, pair.Value.Usage))).ToList();
        entries.AddRange(_pages.Where(page => page.Component != "\0shared").Select(page =>
            ("page:" + page.Component, PageIdentifier(page.Component) + "Props")));
        foreach (var (identity, name) in ContractNames.Assign(entries)) _names[identity] = name;

        var pages = new List<PagePropsInfo>();
        var pageFiles = ContractNames.Assign(_pages.Where(page => page.Component != "\0shared").Select(page =>
            ("page:" + page.Component, page.Component.Replace('/', '.').Replace('\\', '.'))));
        foreach (var group in _pages.Where(page => page.Component != "\0shared").GroupBy(page => page.Component).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var alternatives = group.Select(page => Fields(page.Type, page.Value, Output)).ToArray();
            var fields = CombineFields(alternatives.Select(shape => shape.Fields).ToArray());
            var key = "page:" + group.Key;
            // Preserve the established dotted component filenames when they are safe and unique.
            var flat = group.Key.Replace('/', '.').Replace('\\', '.');
            var safeFlat = flat.Length > 0 && flat.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-')
                && flat is not ("." or ".." or "index" or "shared" or "page-map")
                && _pages.Where(page => page.Component != "\0shared").Select(page => page.Component).Distinct().Count(component => component.Replace('/', '.').Replace('\\', '.').Equals(flat, StringComparison.OrdinalIgnoreCase)) == 1;
            pages.Add(new(group.Key, fields, alternatives.Any(shape => shape.ExtensionData), _names[key], safeFlat ? flat : pageFiles[key]));
        }
        var shared = _pages.Where(page => page.Component == "\0shared").SelectMany(page => Fields(page.Type, page.Value, Output).Fields)
            .Select(field => field with { IsOptional = true }).ToList();
        foreach (var (key, value) in _shares)
            shared.Add(new(key, MapExpression(value, Output), true, IsDeferred(ExpressionType(value, Semantic(value.SyntaxTree)))));
        shared = CollapseDotFields(CombineFields([shared]));
        var updatedRoutes = routeHandlers.Select(pair => EnrichRoute(pair.Route, pair.Parameters)).ToList();
        // Retain enum constant exports even if no page references them.
        foreach (var (_, item) in _types.Where(pair => pair.Value.Type.TypeKind == TypeKind.Enum)) _ = Map(item.Type, item.Usage);
        return new(_enums.Values.OrderBy(item => item.FullName, StringComparer.Ordinal).ToList(), pages,
            _models.Values.OrderBy(item => item.FullName, StringComparer.Ordinal).ToList(), updatedRoutes, shared);
    }

    private SemanticModel Semantic(SyntaxTree tree)
        => _semantics.TryGetValue(tree, out var semantic) ? semantic : _compilation.GetSemanticModel(tree);

    private void AddSeed(ITypeSymbol? type, string usage) { if (type is not null) _seeds.Add((type, usage)); }
    private void SeedProps(ITypeSymbol type, ExpressionSyntax? expression)
    {
        if (type is INamedTypeSymbol dictionary && DictionaryValue(dictionary) is not null)
        {
            foreach (var (_, value) in DictionaryInitializers(expression)) AddSeed(ExpressionType(value, Semantic(value.SyntaxTree)), Output);
            return;
        }
        foreach (var member in Members(type, Output))
        {
            AddSeed(member.Type, Output);
            if (Attribute(member.Symbol, "InertiaPropTypeAttribute")?.ConstructorArguments.FirstOrDefault().Value is ITypeSymbol hint) AddSeed(hint, Output);
        }
    }
    private void Collect(ITypeSymbol type, string usage)
    {
        if (_types.Count >= 512) { Report(type, "Contract graph exceeds 512 types; recursive generic expansions require an explicit contract.", "PATHFINDER221"); return; }
        if (type is IArrayTypeSymbol array) { Collect(array.ElementType, usage); return; }
        if (type is not INamedTypeSymbol named || Primitive(named) is not null || named.TypeKind == TypeKind.Error) return;
        if (Override(named, usage) is not null) return;
        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) { Collect(named.TypeArguments[0], usage); return; }
        if (WrapperResult(named) is { } wrapped) { Collect(wrapped, usage); return; }
        if (DictionaryValue(named) is { } dictionary) { Collect(dictionary, usage); return; }
        if (CollectionElement(named) is { } element) { Collect(element, usage); return; }
        if (IsFrameworkOpaque(named) || IsInertiaWrapper(named)) return;
        if (!_config.GenerateModels && named.TypeKind != TypeKind.Enum) return;
        var key = Key(named, usage);
        if (_types.ContainsKey(key)) return;
        _types[key] = (named, usage);
        if (named.TypeKind == TypeKind.Enum) return;
        foreach (var member in Members(named, usage)) Collect(member.Type, usage);
    }

    private (List<PropField> Fields, bool ExtensionData) Fields(ITypeSymbol? type, ExpressionSyntax? expression, string usage)
    {
        if (type is null || type.TypeKind == TypeKind.Error)
        {
            if (expression is not null) Report(expression, "Cannot infer the props object; use a typed DTO or an explicit component contract.", "PATHFINDER221");
            return ([], false);
        }
        if (Attribute(type, "JsonConverterAttribute") is not null)
        {
            Report(type, "A props-object converter owns its JSON shape; static property inference is unavailable.", "PATHFINDER223");
            return ([], true);
        }
        if (type is INamedTypeSymbol dictionary && DictionaryValue(dictionary) is not null)
        {
            if (dictionary.ContainingNamespace.ToDisplayString() != "System.Collections.Generic") return ([], true);
            var entries = DictionaryInitializers(expression).ToArray();
            if (entries.Length == 0) return ([], true);
            var count = expression switch { ObjectCreationExpressionSyntax creation => creation.Initializer?.Expressions.Count, ImplicitObjectCreationExpressionSyntax creation => creation.Initializer?.Expressions.Count, _ => null };
            var complete = count == entries.Length;
            var properties = entries.Select(entry => new PropField(entry.Key, MapExpression(entry.Value, usage),
                IsDeferred(ExpressionType(entry.Value, Semantic(entry.Value.SyntaxTree))), IsDeferred(ExpressionType(entry.Value, Semantic(entry.Value.SyntaxTree))))).ToList();
            if (!complete) properties = properties.Select(field => field with { TypeScriptType = "unknown" }).ToList();
            return (CollapseDotFields(CombineFields([properties])), !complete);
        }
        var fields = new List<PropField>();
        var extension = false;
        foreach (var member in Members(type, usage))
        {
            if (Attribute(member.Symbol, "JsonExtensionDataAttribute") is not null && usage is Output or Input) { extension = true; continue; }
            var explicitType = Attribute(member.Symbol, "InertiaPropTypeAttribute")?.ConstructorArguments.FirstOrDefault().Value as ITypeSymbol;
            var memberValue = MemberValue(member, expression);
            var handling = Attribute(member.Symbol, "JsonNumberHandlingAttribute") ?? Attribute(type, "JsonNumberHandlingAttribute");
            var numberHandling = handling?.ConstructorArguments.FirstOrDefault().Value is int flags ? (JsonNumberHandling?)flags : null;
            var mapped = explicitType is not null ? Map(explicitType, usage)
                : memberValue is not null && (IsInertiaWrapper(member.Type) || member.Type.TypeKind == TypeKind.Delegate)
                    ? MapExpression(memberValue, usage) : Map(member.Type, usage, numberHandling);
            var converter = Attribute(member.Symbol, "JsonConverterAttribute");
            if (converter is not null && explicitType is null && usage is Output or Input && Override(member.Type, usage) is null)
                mapped = ConverterType(member.Type, converter, member.Symbol);
            if (usage != Output && Attribute(member.Symbol, "RequiredAttribute") is not null) mapped = WithoutNull(mapped);
            if (member.OmitNull) mapped = WithoutNull(mapped);
            if (fields.Any(field => field.Name == member.Name))
            {
                Report(member.Symbol, $"Several CLR members serialize as '{member.Name}'. Use distinct JSON names.", "PATHFINDER222", "error");
                continue;
            }
            fields.Add(new(member.Name, mapped, member.Optional, member.Deferred, IsLiteral: true));
        }
        return (fields, extension);
    }

    private string Map(ITypeSymbol type, string usage, JsonNumberHandling? numberHandling = null)
    {
        if (type is IArrayTypeSymbol array)
            return Nullable(type, array.ElementType.SpecialType == SpecialType.System_Byte ? "string" : ArrayOf(Map(array.ElementType, usage, numberHandling)));
        if (type is not INamedTypeSymbol named)
        {
            if (type.TypeKind is TypeKind.Error or TypeKind.TypeParameter) Report(type, $"Cannot resolve contract type '{type.ToDisplayString()}'; emitting unknown.", "PATHFINDER221");
            return "unknown";
        }
        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) return Union(Map(named.TypeArguments[0], usage, numberHandling), "null");
        if (Override(named, usage) is { } overridden) return Nullable(type, overridden);
        if (usage != Output && named.ToDisplayString().TrimEnd('?') == "System.Numerics.BigInteger")
        {
            Report(named, "BigInteger input requires an application JSON converter/binder and an explicit input type override; only page output has an InertiaNet numeric policy.", "PATHFINDER223");
            return "unknown";
        }
        if (Primitive(named) is { } primitive)
        {
            if (primitive == "number" && usage is Output or Input)
            {
                var policy = Profile(usage).LargeIntegers;
                if (IsLargeInteger(type))
                    primitive = policy == LargeIntegerHandling.String ? "string" : policy == LargeIntegerHandling.BigInt && usage == Output ? "number | bigint" : primitive;
                var handling = numberHandling ?? Profile(usage).NumberHandling;
                if (usage == Output && handling.HasFlag(JsonNumberHandling.WriteAsString)) primitive = "string";
                if (usage == Input && handling.HasFlag(JsonNumberHandling.AllowReadingFromString) && primitive != "string") primitive += " | string";
                if (IsFloatingPoint(type) && handling.HasFlag(JsonNumberHandling.AllowNamedFloatingPointLiterals)) primitive += " | \"NaN\" | \"Infinity\" | \"-Infinity\"";
            }
            if (IsLargeInteger(type) && usage is Query or Form)
                primitive = "number | string" + (_profile.LargeIntegers == LargeIntegerHandling.BigInt ? " | bigint" : "");
            return Nullable(type, primitive);
        }
        if (named.TypeKind == TypeKind.Error) { Report(type, $"Cannot resolve contract type '{type.ToDisplayString()}'; restore references or add an explicit contract.", "PATHFINDER221"); return "unknown"; }
        if (WrapperResult(named) is { } wrapped) return Map(wrapped, usage, numberHandling);
        if (named.ContainingNamespace.ToDisplayString() == "Microsoft.AspNetCore.Http" && named.Name is "IFormFile" or "IFormFileCollection")
            return usage == Form ? Nullable(type, named.Name == "IFormFile" ? "File | Blob" : "(File | Blob)[]")
                : usage == Input ? IsNullable(type) ? "null" : "never" : "unknown";
        if (DictionaryValue(named) is { } dictionary) return Nullable(type, $"Record<string, {Map(dictionary, usage, numberHandling)}>");
        if (CollectionElement(named) is { } element) return Nullable(type, ArrayOf(Map(element, usage, numberHandling)));
        if (IsInertiaWrapper(named))
        {
            Report(type, $"Wrapper '{named.Name}' does not expose a value type here; use [InertiaPropType(typeof(...))] or an inferable factory expression.", "PATHFINDER224");
            return "unknown";
        }
        if (IsFrameworkOpaque(named)) return "unknown";
        if (named.TypeKind == TypeKind.Enum) return MapEnum(named, usage);
        if (usage is Output or Input && Attribute(named, "JsonConverterAttribute") is { } converter) return ConverterType(named, converter, named);
        if (Attribute(named, "JsonPolymorphicAttribute") is not null || Attribute(named, "JsonDerivedTypeAttribute") is not null)
        {
            Report(named, $"Polymorphic contract '{named.Name}' needs an explicit union schema; emitting unknown.", "PATHFINDER223");
            return "unknown";
        }
        if (!_config.GenerateModels) return "unknown";
        var key = Key(named, usage);
        if (_building.Count >= 64 || _models.Count >= 512 && !_models.ContainsKey(key))
        {
            Report(named, "Contract expansion exceeded the supported depth/type limit; use an explicit finite DTO contract.", "PATHFINDER221");
            return "unknown";
        }
        if (!_names.TryGetValue(key, out var name))
        {
            // Expression-level inference can reveal a callback result not present in its object-typed signature.
            name = ContractNames.Identifier(Preferred(named, usage)) + "_" + ContractNames.Hash(key);
            var suffix = 0;
            var preferred = name;
            while (_names.Values.Contains(name, StringComparer.OrdinalIgnoreCase)) name = preferred + "_" + ++suffix;
            _names[key] = name;
        }
        if (!_models.ContainsKey(key) && _building.Add(key))
        {
            var shape = Fields(named, null, usage);
            var byName = Members(named, usage).GroupBy(member => member.Name).ToDictionary(group => group.Key, group => group.First());
            var properties = shape.Fields.Select(field => new ModelProperty(field.Name, field.TypeScriptType,
                field.TypeScriptType.Contains("null", StringComparison.Ordinal), field.TypeScriptType.EndsWith("[]", StringComparison.Ordinal), field.IsOptional,
                usage == Output || !byName.TryGetValue(field.Name, out var member) ? null : Validation(member.Symbol))).ToList();
            _models[key] = new(ContractNames.FullName(named), name, properties, shape.ExtensionData, usage);
            _building.Remove(key);
        }
        return Nullable(type, name);
    }

    private string MapExpression(ExpressionSyntax expression, string usage)
    {
        var semantic = Semantic(expression.SyntaxTree);
        if (expression.IsKind(SyntaxKind.NullLiteralExpression)) return "null";
        if (expression is InvocationExpressionSyntax invocation)
        {
            var name = MethodName(invocation);
            var method = semantic.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if ((IsInertiaInvocation(invocation, semantic) && name is "Defer" or "Optional" or "Always" or "Once" or "Merge" or "DeepMerge" or "Scroll")
                || name == "FromResult" && method?.ContainingType.ToDisplayString() == "System.Threading.Tasks.Task")
            {
                var value = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                if (value is not null) return MapExpression(value, usage);
            }
            // Fluent modifiers retain the value type of the originating wrapper.
            if (semantic.GetTypeInfo(invocation).Type is INamedTypeSymbol wrapper && IsInertiaWrapper(wrapper)
                && invocation.Expression is MemberAccessExpressionSyntax member) return MapExpression(member.Expression, usage);
        }
        if (expression is LambdaExpressionSyntax lambda)
        {
            var returns = lambda.Body is ExpressionSyntax body ? [body]
                : lambda.Body.DescendantNodes().OfType<ReturnStatementSyntax>().Where(statement => statement.Ancestors().OfType<LambdaExpressionSyntax>().FirstOrDefault() == lambda)
                    .Select(statement => statement.Expression).OfType<ExpressionSyntax>().ToArray();
            return returns.Length == 0 ? "unknown" : string.Join(" | ", returns.Select(value => MapExpression(value, usage)).Distinct(StringComparer.Ordinal));
        }
        var type = ExpressionType(expression, semantic);
        return type is null ? "unknown" : Map(type, usage);
    }

    private IEnumerable<(string Key, ExpressionSyntax Value)> DictionaryInitializers(ExpressionSyntax? expression)
    {
        var initializer = expression switch
        {
            ObjectCreationExpressionSyntax creation => creation.Initializer,
            ImplicitObjectCreationExpressionSyntax creation => creation.Initializer,
            _ => null,
        };
        if (initializer is null) yield break;
        foreach (var item in initializer.Expressions)
        {
            var pair = item switch
            {
                AssignmentExpressionSyntax { Left: ImplicitElementAccessSyntax access } assignment when access.ArgumentList.Arguments.Count == 1
                    => (Key: access.ArgumentList.Arguments[0].Expression, Value: assignment.Right),
                InitializerExpressionSyntax elements when elements.Expressions.Count == 2 => (Key: elements.Expressions[0], Value: elements.Expressions[1]),
                _ => default,
            };
            if (pair.Key is not null && ConstantString(pair.Key, Semantic(pair.Key.SyntaxTree)) is { } key) yield return (key, pair.Value);
        }
    }

    private ExpressionSyntax? MemberValue(Member member, ExpressionSyntax? expression)
    {
        if (expression is AnonymousObjectCreationExpressionSyntax anonymous)
        {
            var semantic = Semantic(anonymous.SyntaxTree);
            var initializer = anonymous.Initializers.FirstOrDefault(value => (value.NameEquals?.Name.Identifier.ValueText
                ?? semantic.GetSymbolInfo(value.Expression).Symbol?.Name) == member.Symbol.Name);
            if (initializer is not null) return initializer.Expression;
        }
        var initializerExpression = expression switch
        {
            ObjectCreationExpressionSyntax creation => creation.Initializer,
            ImplicitObjectCreationExpressionSyntax creation => creation.Initializer,
            _ => null,
        };
        var assigned = initializerExpression?.Expressions.OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(value => value.Left is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == member.Symbol.Name);
        return assigned?.Right ?? member.Value;
    }

    private string MapEnum(INamedTypeSymbol type, string usage)
    {
        var profile = Profile(usage);
        var json = usage is Output or Input;
        var converter = Attribute(type, "JsonConverterAttribute");
        var stringEnumConverter = json && (profile.StringEnums || IsStringEnumConverter(converter));
        var largePolicy = json && IsLargeInteger(type.EnumUnderlyingType!) && !stringEnumConverter ? profile.LargeIntegers : LargeIntegerHandling.Number;
        var stringValues = stringEnumConverter || largePolicy == LargeIntegerHandling.String;
        if (json && converter is not null && !IsStringEnumConverter(converter)) return ConverterType(type, converter, type);
        var allowIntegers = !stringValues || IsStringEnumConverter(converter) || profile.EnumAllowIntegers;
        if (!_config.GenerateEnums) return !json ? "string | number" : largePolicy == LargeIntegerHandling.String ? "string"
            : largePolicy == LargeIntegerHandling.BigInt ? "number | bigint" : stringValues ? allowIntegers ? "string | number" : "string" : "number";
        var key = Key(type, usage);
        if (!_names.TryGetValue(key, out var name)) _names[key] = name = ContractNames.Identifier(type.Name) + "_" + ContractNames.Hash(key);
        if (!_enums.ContainsKey(key))
        {
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>().Where(field => field.IsConst && !field.HasConstantValue))
                Report(field, $"Cannot resolve enum constant '{type.Name}.{field.Name}'.", "PATHFINDER104");
            var members = type.GetMembers().OfType<IFieldSymbol>().Where(field => field.HasConstantValue).Select(field =>
            {
                var label = Attribute(field, "JsonStringEnumMemberNameAttribute")?.ConstructorArguments.FirstOrDefault().Value as string
                    ?? (profile.EnumNamingPolicy?.ConvertName(field.Name) ?? field.Name);
                var digits = Convert.ToString(field.ConstantValue, CultureInfo.InvariantCulture);
                var bigInt = largePolicy == LargeIntegerHandling.BigInt && System.Numerics.BigInteger.TryParse(digits, out var integer)
                    && System.Numerics.BigInteger.Abs(integer) > 9007199254740991L;
                return new EnumMember(field.Name, stringEnumConverter ? label : digits, bigInt);
            }).ToList();
            var flags = Attribute(type, "FlagsAttribute") is not null;
            _enums[key] = new(type.ToDisplayString(), name, members, stringValues,
                !json ? "string | number" : largePolicy == LargeIntegerHandling.BigInt ? "number | bigint" : largePolicy == LargeIntegerHandling.String ? "string" : stringValues && flags ? allowIntegers ? "string | number" : "string" : allowIntegers ? "number" : null);
        }
        return name;
    }

    private string ConverterType(ITypeSymbol type, AttributeData converter, ISymbol source)
    {
        if (IsStringEnumConverter(converter) && (type.TypeKind == TypeKind.Enum || type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }))
            return Nullable(type, "string | number");
        Report(source, $"Custom converter on '{source.Name}' owns the wire shape; emitting unknown.", "PATHFINDER223");
        return "unknown";
    }

    private IEnumerable<Member> Members(ITypeSymbol type, string usage)
    {
        var profile = Profile(usage);
        if (type is not INamedTypeSymbol named) yield break;
        var inherited = new Dictionary<string, ISymbol>(StringComparer.Ordinal);
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        void Add(INamedTypeSymbol? current)
        {
            if (current is null || current.SpecialType == SpecialType.System_Object || !visited.Add(current)) return;
            Add(current.BaseType);
            if (current.TypeKind == TypeKind.Interface) foreach (var parent in current.Interfaces) Add(parent);
            foreach (var member in current.GetMembers().Where(member => !member.IsStatic && !member.IsImplicitlyDeclared || member is IPropertySymbol { IsImplicitlyDeclared: true }))
                if (member is IPropertySymbol or IFieldSymbol) inherited[member.Name] = member;
        }
        Add(named);
        foreach (var symbol in inherited.Values)
        {
            var include = Attribute(symbol, "JsonIncludeAttribute") is not null;
            ITypeSymbol memberType;
            if (symbol is IPropertySymbol property)
            {
                if (property.IsIndexer || property.GetMethod is null || (property.GetMethod.DeclaredAccessibility != Accessibility.Public && !include)) continue;
                if (usage != Output && property.SetMethod?.DeclaredAccessibility != Accessibility.Public && !include
                    && !named.InstanceConstructors.Any(ctor => ctor.DeclaredAccessibility == Accessibility.Public && ctor.Parameters.Any(parameter => parameter.Name.Equals(property.Name, StringComparison.OrdinalIgnoreCase)))) continue;
                if (usage == Output && profile.IgnoreReadOnlyProperties && property.SetMethod is null && CollectionElement(property.Type as INamedTypeSymbol) is null && DictionaryValue(property.Type as INamedTypeSymbol) is null) continue;
                memberType = property.Type;
            }
            else if (symbol is IFieldSymbol field)
            {
                if (usage is Query or Form || (!include && (!profile.IncludeFields || field.DeclaredAccessibility != Accessibility.Public))) continue;
                if (usage == Output && profile.IgnoreReadOnlyFields && field.IsReadOnly) continue;
                memberType = field.Type;
            }
            else continue;
            var ignore = Attribute(symbol, "JsonIgnoreAttribute");
            var ignoreCondition = ignore is null ? profile.IgnoreCondition : (JsonIgnoreCondition)(NamedValue(ignore, "Condition") as int? ?? 1);
            if (usage is Output or Input && ignore is not null && ignoreCondition == JsonIgnoreCondition.Always) continue;
            if (usage is Query or Form && Attribute(symbol, "BindNeverAttribute") is not null) continue;
            if (usage is Query or Form)
            {
                var sources = symbol.GetAttributes().Where(attribute => attribute.AttributeClass?.ContainingNamespace.ToDisplayString() == "Microsoft.AspNetCore.Mvc").Select(attribute => attribute.AttributeClass?.Name)
                    .Where(name => name is "FromQueryAttribute" or "FromFormAttribute" or "FromRouteAttribute" or "FromBodyAttribute" or "FromHeaderAttribute" or "FromServicesAttribute").ToArray();
                if (sources.Length > 1)
                {
                    Report(symbol, "Several binding sources are declared on one member. Use a dedicated DTO for each binding contract.", "PATHFINDER227", "error");
                    continue;
                }
                if (sources.Length == 1 && sources[0] != (usage == Query ? "FromQueryAttribute" : "FromFormAttribute")) continue;
            }
            var name = usage is Query or Form
                ? BindingName(symbol, usage == Query ? "FromQueryAttribute" : "FromFormAttribute") ?? BindingName(symbol, "ModelBinderAttribute")
                    ?? BindingName(symbol, "BindPropertyAttribute") ?? JsonNamingPolicy.CamelCase.ConvertName(symbol.Name)
                : Attribute(symbol, "JsonPropertyNameAttribute")?.ConstructorArguments.FirstOrDefault().Value as string ?? profile.Name(symbol.Name);
            var nullable = IsNullable(memberType);
            var deferred = IsDeferred(memberType);
            var optional = usage == Output
                ? deferred || IsOnce(memberType) || ignoreCondition == JsonIgnoreCondition.WhenWritingDefault
                    || (nullable && ignoreCondition == JsonIgnoreCondition.WhenWritingNull)
                : !IsRequired(symbol, memberType);
            var syntax = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            var expression = syntax switch
            {
                PropertyDeclarationSyntax propertySyntax => propertySyntax.Initializer?.Value ?? propertySyntax.ExpressionBody?.Expression,
                AnonymousObjectMemberDeclaratorSyntax anonymous => anonymous.Expression,
                VariableDeclaratorSyntax variable => variable.Initializer?.Value,
                _ => null,
            };
            yield return new(symbol, memberType, name, optional, usage == Output && nullable && ignoreCondition is JsonIgnoreCondition.WhenWritingDefault or JsonIgnoreCondition.WhenWritingNull, deferred, expression);
        }
    }

    private RouteInfo EnrichRoute(RouteInfo route, IParameterSymbol[] parameters)
    {
        var routeParameters = route.Parameters.Select(parameter =>
        {
            var bound = parameters.FirstOrDefault(candidate => (BindingName(candidate, "FromRouteAttribute") ?? candidate.Name).Equals(parameter.Name, StringComparison.OrdinalIgnoreCase));
            if (bound is null) return parameter;
            var mapped = IsScalar(bound.Type) ? Map(bound.Type, Query) : "string";
            return parameter with { TypeScriptType = parameter.IsOptional ? mapped : WithoutNull(mapped) };
        }).ToArray();
        var body = parameters.FirstOrDefault(parameter => ParameterUsage(parameter, route) == Input);
        var query = parameters.Where(parameter => ParameterUsage(parameter, route) == Query).ToArray();
        var form = parameters.Where(parameter => ParameterUsage(parameter, route) == Form).ToArray();
        string? ParameterShape(IParameterSymbol[] values, string usage)
            => values.Length == 0 ? null : values.Length == 1 && IsBindingObject(values[0].Type)
                && BindingName(values[0], usage == Query ? "FromQueryAttribute" : "FromFormAttribute") is null ? Map(values[0].Type, usage)
                : "{ " + string.Join("; ", values.Select(parameter => JsonSerializer.Serialize(BindingName(parameter, usage == Query ? "FromQueryAttribute" : "FromFormAttribute") ?? parameter.Name)
                    + (parameter.IsOptional || IsExplicitlyNullable(parameter.Type) ? "?" : "") + ": " + Map(parameter.Type, usage))) + " }";
        var bodyType = body is null ? null : IsExplicitlyNullable(body.Type) ? Map(body.Type, Input) : WithoutNull(Map(body.Type, Input));
        return route with { Parameters = routeParameters, BodyTypeName = bodyType ?? route.BodyTypeName, BodyTypeScriptType = bodyType,
            QueryTypeScriptType = ParameterShape(query, Query), FormTypeScriptType = ParameterShape(form, Form) };
    }

    private IParameterSymbol[] HandlerParameters(RouteInfo route, SyntaxTree[] trees)
    {
        var tree = trees.FirstOrDefault(tree => tree.FilePath == route.SourceFile);
        if (tree is null || route.SourceLine is null) return [];
        var semantic = Semantic(tree);
        var root = tree.GetRoot();
        if (route.ControllerFullName != "__MinimalApi__")
        {
            var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault(method => method.Identifier.ValueText == route.ActionName
                && (route.SourceSpanStart is { } start ? method.SpanStart == start : method.GetLocation().GetLineSpan().StartLinePosition.Line + 1 == route.SourceLine));
            return declaration is not null && semantic.GetDeclaredSymbol(declaration) is IMethodSymbol method ? method.Parameters.ToArray() : [];
        }
        var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().FirstOrDefault(invocation => (route.SourceSpanStart is { } start ? invocation.SpanStart == start : invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1 == route.SourceLine)
            && MethodName(invocation) is "MapGet" or "MapPost" or "MapPut" or "MapPatch" or "MapDelete" or "MapMethods");
        if (invocation is null) return [];
        var index = MethodName(invocation) == "MapMethods" ? 2 : 1;
        if (invocation.ArgumentList.Arguments.Count <= index) return [];
        var expression = invocation.ArgumentList.Arguments[index].Expression;
        if (semantic.GetSymbolInfo(expression).Symbol is IMethodSymbol symbol) return symbol.Parameters.ToArray();
        var methods = semantic.GetMemberGroup(expression).OfType<IMethodSymbol>().ToArray();
        if (methods.Length == 1) return methods[0].Parameters.ToArray();
        return expression is LambdaExpressionSyntax lambda && semantic.GetSymbolInfo(lambda).Symbol is IMethodSymbol lambdaSymbol ? lambdaSymbol.Parameters.ToArray() : [];
    }

    private string? ParameterUsage(IParameterSymbol parameter, RouteInfo route)
    {
        var binding = Attribute(parameter, "ModelBinderAttribute");
        if (NamedValue(binding, "BinderType") is not null || parameter.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "Microsoft.AspNetCore.Http.AsParametersAttribute"))
        {
            Report(parameter, "Custom binders and AsParameters require an explicit expanded endpoint contract; request-shape inference is unavailable here.", "PATHFINDER229");
            return null;
        }
        if (Attribute(parameter, "FromBodyAttribute") is not null) return Input;
        if (Attribute(parameter, "FromQueryAttribute") is not null) return Query;
        if (Attribute(parameter, "FromFormAttribute") is not null) return Form;
        if (Attribute(parameter, "FromServicesAttribute") is not null || Attribute(parameter, "FromHeaderAttribute") is not null
            || Attribute(parameter, "FromRouteAttribute") is not null || parameter.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute")) return null;
        if (route.Parameters.Any(candidate => candidate.Name.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase))) return null;
        var type = parameter.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();
        if (type is "Microsoft.AspNetCore.Http.IFormFile" or "Microsoft.AspNetCore.Http.IFormFileCollection") return Form;
        if (type is "Microsoft.AspNetCore.Http.HttpContext" or "Microsoft.AspNetCore.Http.HttpRequest" or "Microsoft.AspNetCore.Http.HttpResponse"
            or "System.Threading.CancellationToken" or "System.Security.Claims.ClaimsPrincipal" or "System.IServiceProvider" or "InertiaNet.Core.IInertiaService"
            || type.StartsWith("Microsoft.Extensions.Logging.", StringComparison.Ordinal)) return null;
        if (route.ControllerFullName == "__MinimalApi__" && IsScalar(parameter.Type)) return Query;
        if (!IsScalar(parameter.Type))
            Report(parameter, "An unannotated complex parameter may bind from JSON, services or a custom binder. Declare FromBody, FromQuery, FromForm or FromServices explicitly for a generated request contract.", "PATHFINDER229");
        return null;
    }
    private static bool IsRequired(ISymbol symbol, ITypeSymbol type)
        => Attribute(symbol, "RequiredAttribute") is not null || Attribute(symbol, "JsonRequiredAttribute") is not null
            || symbol is IPropertySymbol { IsRequired: true } || (type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.NotAnnotated);
    private static bool IsNullable(ITypeSymbol type) => type.NullableAnnotation == NullableAnnotation.Annotated
        || type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.None
        || type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
    private static bool IsExplicitlyNullable(ITypeSymbol type) => type.NullableAnnotation == NullableAnnotation.Annotated
        || type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
    private static bool IsDeferred(ITypeSymbol? type) => type is not null && IsInertiaWrapper(type) && type.Name is "DeferProp" or "OptionalProp";
    private static bool IsOnce(ITypeSymbol type) => IsInertiaWrapper(type) && type.Name == "OnceProp";
    private static bool IsInertiaWrapper(ITypeSymbol type) => type.ContainingNamespace?.ToDisplayString() == "InertiaNet.Props"
        || type.TypeKind == TypeKind.Error && type.Name is "DeferProp" or "OptionalProp" or "AlwaysProp" or "MergeProp" or "OnceProp" or "ScrollProp";
    private static bool IsFrameworkOpaque(INamedTypeSymbol type) => type.ContainingNamespace.ToDisplayString() is "System.Text.Json" or "System.Text.Json.Nodes" && type.Name is "JsonElement" or "JsonDocument" or "JsonNode" or "JsonObject" or "JsonArray"
        || type.TypeKind == TypeKind.Delegate && type.DelegateInvokeMethod is null;
    private static string? Primitive(INamedTypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_String or SpecialType.System_Char => "string", SpecialType.System_Boolean => "boolean",
        SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
            or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Decimal or SpecialType.System_Double or SpecialType.System_Single => "number",
        SpecialType.System_Object => "unknown",
        _ => type.ToDisplayString().TrimEnd('?') == "System.Numerics.BigInteger" ? "number" : type.ToDisplayString().TrimEnd('?') is "System.Guid" or "System.DateTime" or "System.DateTimeOffset" or "System.DateOnly" or "System.TimeOnly" or "System.TimeSpan" or "System.Uri" or "System.Version" ? "string" : null,
    };
    private static bool IsLargeInteger(ITypeSymbol type) => type.SpecialType is SpecialType.System_Int64 or SpecialType.System_UInt64
        || type.ToDisplayString().TrimEnd('?') == "System.Numerics.BigInteger";
    private static bool IsNumber(ITypeSymbol type) => type is INamedTypeSymbol named && (Primitive(named) == "number"
        || named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && IsNumber(named.TypeArguments[0]));
    private static bool IsFloatingPoint(ITypeSymbol type) => type.SpecialType is SpecialType.System_Double or SpecialType.System_Single
        || type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable && IsFloatingPoint(nullable.TypeArguments[0]);
    private static bool IsScalar(ITypeSymbol type) => type is INamedTypeSymbol named && (Primitive(named) is not null || type.TypeKind == TypeKind.Enum
        || named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && IsScalar(named.TypeArguments[0]));
    private static bool IsBindingObject(ITypeSymbol type) => type is INamedTypeSymbol named && !IsScalar(type)
        && DictionaryValue(named) is null && CollectionElement(named) is null
        && !(named.ContainingNamespace.ToDisplayString() == "Microsoft.AspNetCore.Http" && named.Name is "IFormFile" or "IFormFileCollection");
    private static ITypeSymbol? WrapperResult(INamedTypeSymbol type)
        => type.TypeKind == TypeKind.Delegate ? type.DelegateInvokeMethod?.ReturnType
            : type.IsGenericType && (type.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" && type.Name is "Task" or "ValueTask"
                || type.ContainingNamespace.ToDisplayString() == "System" && type.Name == "Lazy") ? type.TypeArguments[0] : null;
    private static ITypeSymbol? CollectionElement(INamedTypeSymbol? type)
        => type is null || type.SpecialType == SpecialType.System_String ? null
            : type.AllInterfaces.Append(type).FirstOrDefault(candidate => candidate.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)?.TypeArguments[0];
    private static ITypeSymbol? DictionaryValue(INamedTypeSymbol? type)
        => type?.AllInterfaces.Append(type).FirstOrDefault(candidate => candidate.IsGenericType && candidate.Arity == 2
            && candidate.ContainingNamespace.ToDisplayString() == "System.Collections.Generic" && candidate.Name is "IDictionary" or "IReadOnlyDictionary")?.TypeArguments[1];
    private SerializationProfile Profile(string usage) => usage == Input ? _inputProfile : _profile;
    private string? Override(ITypeSymbol type, string usage)
        => usage is Output or Input && Profile(usage).TypeOverrides.TryGetValue(type.ToDisplayString().Replace("global::", "", StringComparison.Ordinal).TrimEnd('?'), out var value) ? value : null;
    private static string Key(INamedTypeSymbol type, string usage) => (type.TypeKind == TypeKind.Enum ? "enum:" + usage : usage) + ":" + ContractNames.Identity(type);
    private static string Preferred(INamedTypeSymbol type, string usage)
        => type.Name + (type.TypeArguments.Length == 0 ? "" : "_" + string.Join('_', type.TypeArguments.Select(argument => argument.Name)))
            + (usage == Output ? "" : usage == Input ? "Input" : usage == Query ? "Query" : "Form");
    private static string PageIdentifier(string component) => string.Concat(component.Split('/', '\\', '.', '-', ' ').Select(part => part.Length == 0 ? "" : char.ToUpperInvariant(part[0]) + part[1..]));
    private static string Nullable(ITypeSymbol type, string value) => IsNullable(type) ? Union(value, "null") : value;
    private static string Union(string left, string right) => right == "null" && InertiaNet.Pathfinder.Generation.TypeReferenceCollector.HasNull(left) ? left : left + " | " + right;
    private static string WithoutNull(string value) => InertiaNet.Pathfinder.Generation.TypeReferenceCollector.RemoveNull(value);
    private static string ArrayOf(string type) => type.Contains(" | ", StringComparison.Ordinal) ? $"({type})[]" : type + "[]";
    private static string? BindingName(ISymbol symbol, string attribute) => NamedValue(Attribute(symbol, attribute), "Name") as string;
    private static AttributeData? Attribute(ISymbol symbol, string name) => symbol.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.Name == name && attribute.AttributeClass.ContainingNamespace.ToDisplayString() == AttributeNamespace(name));
    private static string AttributeNamespace(string name) => name.StartsWith("Json", StringComparison.Ordinal) ? "System.Text.Json.Serialization"
        : name == "FlagsAttribute" ? "System" : name == "InertiaPropTypeAttribute" ? "InertiaNet.Core"
        : name is "BindNeverAttribute" ? "Microsoft.AspNetCore.Mvc.ModelBinding"
        : name.StartsWith("From", StringComparison.Ordinal) || name is "ModelBinderAttribute" or "BindPropertyAttribute" ? "Microsoft.AspNetCore.Mvc"
        : "System.ComponentModel.DataAnnotations";
    private static object? NamedValue(AttributeData? attribute, string key) => attribute?.NamedArguments.FirstOrDefault(pair => pair.Key == key).Value.Value;
    private static bool IsStringEnumConverter(AttributeData? attribute)
        => attribute?.ConstructorArguments.FirstOrDefault().Value is INamedTypeSymbol { Name: "JsonStringEnumConverter" } converter
            && converter.ContainingNamespace.ToDisplayString() == "System.Text.Json.Serialization";
    private static string? ConstantString(ExpressionSyntax expression, SemanticModel semantic) => semantic.GetConstantValue(expression).Value as string;
    private static string? MethodName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        SimpleNameSyntax name => name.Identifier.ValueText,
        _ => null,
    };
    private static bool IsInertiaInvocation(InvocationExpressionSyntax invocation, SemanticModel semantic)
    {
        if (semantic.GetSymbolInfo(invocation).Symbol is IMethodSymbol method)
            return method.ContainingNamespace.ToDisplayString() is var space && (space == "InertiaNet" || space.StartsWith("InertiaNet.", StringComparison.Ordinal));
        // Source-directory mode can be useful before references are restored. Keep
        // unresolved conventional calls, while resolved unrelated Render methods stay excluded.
        return invocation.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "inertia" or "Inertia" or "_inertia" } }
            or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax };
    }
    private static ITypeSymbol? ExpressionType(ExpressionSyntax expression, SemanticModel semantic)
        => semantic.GetTypeInfo(expression).Type ?? semantic.GetTypeInfo(expression).ConvertedType;
    private string? ComponentName(ExpressionSyntax expression, SemanticModel semantic)
    {
        if (ConstantString(expression, semantic) is { } literal) return literal;
        if (ExpressionType(expression, semantic) is not INamedTypeSymbol { Name: "InertiaComponent" }) return null;
        var declaration = semantic.GetSymbolInfo(expression).Symbol?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var value = declaration switch { VariableDeclaratorSyntax variable => variable.Initializer?.Value, PropertyDeclarationSyntax property => property.Initializer?.Value ?? property.ExpressionBody?.Expression, _ => expression };
        var arguments = value switch { ObjectCreationExpressionSyntax creation => creation.ArgumentList?.Arguments, ImplicitObjectCreationExpressionSyntax creation => creation.ArgumentList.Arguments, _ => null };
        if (arguments is not { Count: > 0 }) return null;
        var argument = arguments.Value[0].Expression;
        var model = _compilation.SyntaxTrees.Contains(argument.SyntaxTree) ? Semantic(argument.SyntaxTree) : semantic;
        return ConstantString(argument, model);
    }

    private static List<PropField> CombineFields(List<PropField>[] alternatives)
    {
        var result = new List<PropField>();
        foreach (var group in alternatives.SelectMany(fields => fields).GroupBy(field => (field.Name, Literal: field.IsLiteral && field.Name.Contains('.'))).OrderBy(group => group.Key.Name, StringComparer.Ordinal))
            result.Add(new(group.Key.Name, string.Join(" | ", group.Select(field => field.TypeScriptType).Distinct(StringComparer.Ordinal)),
                group.Any(field => field.IsOptional) || alternatives.Any(fields => fields.All(field => field.Name != group.Key.Name)), group.Any(field => field.IsDeferred), group.Key.Literal));
        return result;
    }
    private static List<PropField> CollapseDotFields(List<PropField> fields)
    {
        var result = fields.Where(field => field.IsLiteral || !field.Name.Contains('.')).ToList();
        foreach (var group in fields.Where(field => !field.IsLiteral && field.Name.Contains('.')).GroupBy(field => field.Name[..field.Name.IndexOf('.')]))
        {
            var children = CollapseDotFields(group.Select(field => field with { Name = field.Name[(field.Name.IndexOf('.') + 1)..] }).ToList());
            var value = "{ " + string.Join("; ", children.Select(field => JsonSerializer.Serialize(field.Name) + (field.IsOptional ? "?" : "") + ": " + field.TypeScriptType)) + " }";
            var existing = result.FirstOrDefault(field => field.Name == group.Key);
            if (existing is not null) result.Remove(existing);
            result.Add(new(group.Key, existing is null ? value : existing.TypeScriptType + " | " + value,
                children.All(field => field.IsOptional) && existing?.IsOptional != false, children.Any(field => field.IsDeferred) || existing?.IsDeferred == true));
        }
        return result;
    }
    private IReadOnlyDictionary<string, object?>? Validation(ISymbol symbol)
    {
        var rules = new Dictionary<string, object?>();
        foreach (var attribute in symbol.GetAttributes())
        {
            var args = attribute.ConstructorArguments;
            switch (attribute.AttributeClass?.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations" ? attribute.AttributeClass.Name : null)
            {
                case "RequiredAttribute": rules["required"] = true; rules["allowEmptyStrings"] = NamedValue(attribute, "AllowEmptyStrings") as bool? ?? false; break;
                case "StringLengthAttribute": rules["maxLength"] = args.FirstOrDefault().Value; rules["minLength"] = NamedValue(attribute, "MinimumLength") ?? 0; break;
                case "MaxLengthAttribute": rules["maxLength"] = args.FirstOrDefault().Value; break;
                case "MinLengthAttribute": rules["minLength"] = args.FirstOrDefault().Value; break;
                case "RangeAttribute": rules["min"] = args.Length == 3 ? args[1].Value : args[0].Value; rules["max"] = args.Length == 3 ? args[2].Value : args[1].Value; break;
                case "EmailAddressAttribute": rules["format"] = "email"; break;
                case "UrlAttribute": rules["format"] = "url"; break;
                case "RegularExpressionAttribute": rules["pattern"] = args.FirstOrDefault().Value; rules["patternLanguage"] = "dotnet"; break;
                default:
                    for (var type = attribute.AttributeClass?.BaseType; type is not null; type = type.BaseType)
                        if (type.ToDisplayString() == "System.ComponentModel.DataAnnotations.ValidationAttribute") { Report(symbol, $"Validation attribute '{attribute.AttributeClass!.Name}' is not translated; the server remains authoritative.", "PATHFINDER226"); break; }
                    break;
            }
        }
        return rules.Count == 0 ? null : rules;
    }
    private void Report(ISymbol symbol, string message, string code, string severity = "warning")
    {
        var location = symbol.Locations.FirstOrDefault(location => location.IsInSource)?.GetLineSpan();
        Report(location?.Path ?? symbol.ContainingAssembly?.Name ?? "contracts", (location?.StartLinePosition.Line ?? 0) + 1, message, code, severity);
    }
    private void Report(SyntaxNode source, string message, string code, string severity = "warning")
    {
        var location = source.GetLocation().GetLineSpan();
        Report(location.Path, location.StartLinePosition.Line + 1, message, code, severity);
    }
    private void Report(string file, int line, string message, string code, string severity)
    {
        if (_reported.Add(file + ":" + line + ":" + code + ":" + message)) PathfinderDiagnostics.Report(file, line, message, code, severity);
    }
}
