using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace InertiaNet.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InertiaComponentNameAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => [InertiaDiagnostics.InvalidComponentName];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
            return;

        if (!InertiaInvocationHelpers.TryGetLiteralComponentName(context, invocation, out var componentName, out var location))
            return;

        if (IsValidComponentName(componentName))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            InertiaDiagnostics.InvalidComponentName,
            location,
            componentName));
    }

    private static bool IsValidComponentName(string componentName)
    {
        if (string.IsNullOrWhiteSpace(componentName))
            return false;

        if (componentName.Contains('\\'))
            return false;

        if (componentName.StartsWith("/", StringComparison.Ordinal) || componentName.EndsWith("/", StringComparison.Ordinal))
            return false;

        return !componentName.Contains("//", StringComparison.Ordinal);
    }
}
