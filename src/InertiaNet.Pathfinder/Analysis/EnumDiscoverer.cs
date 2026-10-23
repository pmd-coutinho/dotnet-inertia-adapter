using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace InertiaNet.Pathfinder.Analysis;

static class EnumDiscoverer
{
    private static readonly Lazy<MetadataReference[]> References = new(() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? typeof(object).Assembly.Location)
            .Split(Path.PathSeparator).Distinct(StringComparer.Ordinal)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToArray());

    public static List<EnumInfo> Discover(SyntaxTree tree) => Discover([tree]);

    public static List<EnumInfo> Discover(IEnumerable<SyntaxTree> trees)
    {
        var compilation = CSharpCompilation.Create("Pathfinder.EnumContracts", trees, References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var enums = new List<EnumInfo>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var semantic = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<EnumDeclarationSyntax>())
            {
                if (semantic.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol) continue;
                var stringValues = symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.Name == "JsonConverterAttribute"
                    && attribute.ConstructorArguments.Any(argument => argument.Value?.ToString()?.Contains("JsonStringEnumConverter", StringComparison.Ordinal) == true));
                var members = new List<EnumMember>();
                foreach (var member in declaration.Members)
                {
                    if (semantic.GetDeclaredSymbol(member) is not IFieldSymbol { HasConstantValue: true } field)
                    {
                        var span = member.GetLocation().GetLineSpan();
                        PathfinderDiagnostics.Report(span.Path, span.StartLinePosition.Line + 1,
                            $"Cannot resolve enum constant '{symbol.Name}.{member.Identifier.ValueText}'.", "PATHFINDER104");
                        continue;
                    }
                    var stringName = field.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.Name == "JsonStringEnumMemberNameAttribute")
                        ?.ConstructorArguments.FirstOrDefault().Value as string ?? field.Name;
                    members.Add(new EnumMember(field.Name, stringValues ? stringName : Convert.ToString(field.ConstantValue, CultureInfo.InvariantCulture)));
                }
                enums.Add(new EnumInfo(symbol.ToDisplayString(), symbol.Name, members, stringValues));
            }
        }
        return enums;
    }
}
