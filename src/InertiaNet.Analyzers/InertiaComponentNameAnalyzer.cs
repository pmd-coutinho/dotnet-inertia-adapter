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

        if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
            return;

        var containingType = method.ReducedFrom?.ContainingType ?? method.ContainingType;
        var containingTypeName = containingType.ToDisplayString();

        var componentArgIndex = containingTypeName switch
        {
            "InertiaNet.Core.IInertiaService" when method.Name == "Render" => 0,
            "InertiaNet.Extensions.InertiaResults" when method.Name == "Inertia" => 0,
            "InertiaNet.Extensions.EndpointRouteBuilderExtensions" when method.Name == "MapInertia" => 1,
            "InertiaNet.Extensions.EndpointRouteBuilderExtensions" when method.Name == "MapInertiaFallback" => 0,
            "InertiaNet.Extensions.ControllerExtensions" when method.Name == "Inertia" => invocation.ArgumentList.Arguments.Count > 1 ? 1 : 0,
            _ => -1,
        };

        if (componentArgIndex < 0 || invocation.ArgumentList.Arguments.Count <= componentArgIndex)
            return;

        var componentExpression = invocation.ArgumentList.Arguments[componentArgIndex].Expression;
        var constantValue = context.SemanticModel.GetConstantValue(componentExpression);
        if (!constantValue.HasValue || constantValue.Value is not string componentName)
            return;

        if (IsValidComponentName(componentName))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            InertiaDiagnostics.InvalidComponentName,
            componentExpression.GetLocation(),
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
