using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace InertiaNet.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InertiaPageFileAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] DefaultExtensions = ["js", "jsx", "ts", "tsx", "vue", "svelte"];
    private static readonly string[] ConventionalPagePaths =
    [
        "src/pages",
        "src/Pages",
        "Pages",
        "ClientApp/pages",
        "ClientApp/Pages",
        "resources/js/Pages",
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => [InertiaDiagnostics.MissingPageFile];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(startContext =>
        {
            var configuration = PageValidationConfiguration.Create(startContext.Compilation);
            if (!configuration.Enabled)
                return;

            startContext.RegisterSyntaxNodeAction(nodeContext => AnalyzeInvocation(nodeContext, configuration), Microsoft.CodeAnalysis.CSharp.SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, PageValidationConfiguration configuration)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
            return;

        if (!InertiaInvocationHelpers.TryGetLiteralComponentName(context, invocation, out var componentName, out var location))
            return;

        if (!InertiaComponentNameAnalyzer_IsValidComponentName(componentName))
            return;

        var projectRoot = FindProjectRoot(invocation.SyntaxTree.FilePath);
        if (projectRoot == null)
            return;

        var pageRoots = configuration.PagePaths.Count > 0
            ? configuration.PagePaths.Select(path => Path.IsPathRooted(path) ? path : Path.Combine(projectRoot, path)).ToArray()
            : ConventionalPagePaths.Select(path => Path.Combine(projectRoot, path)).Where(Directory.Exists).ToArray();

        if (pageRoots.Length == 0)
            return;

        if (ComponentExists(componentName, pageRoots, configuration.Extensions))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            InertiaDiagnostics.MissingPageFile,
            location,
            componentName,
            string.Join(", ", pageRoots.Select(path => ToRelativeDisplayPath(projectRoot, path)))));
    }

    private static bool ComponentExists(string componentName, IReadOnlyList<string> pageRoots, IReadOnlyList<string> extensions)
    {
        var relativePath = componentName.Replace('/', Path.DirectorySeparatorChar);

        foreach (var pageRoot in pageRoots)
        {
            foreach (var extension in extensions)
            {
                if (File.Exists(Path.Combine(pageRoot, $"{relativePath}.{extension}")))
                    return true;
            }
        }

        return false;
    }

    private static string? FindProjectRoot(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        var directory = Path.GetDirectoryName(filePath);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly).Length > 0)
                return directory;

            directory = Directory.GetParent(directory)?.FullName;
        }

        return Path.GetDirectoryName(filePath);
    }

    private static string ToRelativeDisplayPath(string root, string fullPath)
    {
        var normalizedRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(fullPath);

        if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return normalizedPath.Replace('\\', '/');

        return normalizedPath.Substring(normalizedRoot.Length).Replace('\\', '/');
    }

    private static string EnsureTrailingSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static bool InertiaComponentNameAnalyzer_IsValidComponentName(string componentName)
    {
        if (string.IsNullOrWhiteSpace(componentName))
            return false;

        if (componentName.Contains('\\'))
            return false;

        if (componentName.StartsWith("/", StringComparison.Ordinal) || componentName.EndsWith("/", StringComparison.Ordinal))
            return false;

        return !componentName.Contains("//", StringComparison.Ordinal);
    }

    private sealed class PageValidationConfiguration
    {
        public bool Enabled { get; private set; }
        public IReadOnlyList<string> PagePaths { get; private set; } = [];
        public IReadOnlyList<string> Extensions { get; private set; } = DefaultExtensions;

        public static PageValidationConfiguration Create(Compilation compilation)
        {
            var enabled = false;
            var pagePaths = new List<string>();
            var extensions = new List<string>();

            foreach (var tree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();

                foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                {
                    if (assignment.Left is not MemberAccessExpressionSyntax memberAccess ||
                        memberAccess.Expression is not MemberAccessExpressionSyntax pagesAccess ||
                        pagesAccess.Name.Identifier.Text != "Pages")
                    {
                        continue;
                    }

                    switch (memberAccess.Name.Identifier.Text)
                    {
                        case "EnsurePagesExist":
                            var boolValue = semanticModel.GetConstantValue(assignment.Right);
                            if (boolValue.HasValue && boolValue.Value is true)
                                enabled = true;
                            break;
                        case "Paths":
                            pagePaths.AddRange(ExtractStringValues(assignment.Right));
                            break;
                        case "Extensions":
                            extensions.AddRange(ExtractStringValues(assignment.Right));
                            break;
                    }
                }
            }

            return new PageValidationConfiguration
            {
                Enabled = enabled,
                PagePaths = pagePaths.Distinct(StringComparer.Ordinal).ToArray(),
                Extensions = extensions.Count > 0 ? extensions.Distinct(StringComparer.Ordinal).ToArray() : DefaultExtensions,
            };
        }

        private static IEnumerable<string> ExtractStringValues(ExpressionSyntax expression)
        {
            switch (expression)
            {
                case CollectionExpressionSyntax collection:
                    foreach (var element in collection.Elements.OfType<ExpressionElementSyntax>())
                    {
                        if (element.Expression is LiteralExpressionSyntax literal && literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression))
                            yield return literal.Token.ValueText;
                    }
                    yield break;
                case ArrayCreationExpressionSyntax arrayCreation when arrayCreation.Initializer != null:
                    foreach (var value in ExtractFromInitializer(arrayCreation.Initializer))
                        yield return value;
                    yield break;
                case ImplicitArrayCreationExpressionSyntax implicitArray:
                    foreach (var value in ExtractFromInitializer(implicitArray.Initializer))
                        yield return value;
                    yield break;
                case InitializerExpressionSyntax initializer:
                    foreach (var value in ExtractFromInitializer(initializer))
                        yield return value;
                    yield break;
            }
        }

        private static IEnumerable<string> ExtractFromInitializer(InitializerExpressionSyntax initializer)
        {
            foreach (var expression in initializer.Expressions)
            {
                if (expression is LiteralExpressionSyntax literal && literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression))
                    yield return literal.Token.ValueText;
            }
        }
    }
}
