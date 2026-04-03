using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace InertiaNet.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PathfinderMinimalApiAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> SupportedMapMethods =
    ["MapGet", "MapPost", "MapPut", "MapPatch", "MapDelete"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => [InertiaDiagnostics.UnsupportedMinimalApiTemplate, InertiaDiagnostics.UnsupportedMinimalApiHandler];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, Microsoft.CodeAnalysis.CSharp.SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation ||
            invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        if (!SupportedMapMethods.Contains(memberAccess.Name.Identifier.Text))
            return;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 2)
            return;

        var routeTemplate = context.SemanticModel.GetConstantValue(args[0].Expression);
        if (!routeTemplate.HasValue || routeTemplate.Value is not string)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InertiaDiagnostics.UnsupportedMinimalApiTemplate,
                args[0].GetLocation()));
        }

        var handler = args[1].Expression;
        if (handler is not ParenthesizedLambdaExpressionSyntax and not SimpleLambdaExpressionSyntax)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InertiaDiagnostics.UnsupportedMinimalApiHandler,
                args[1].GetLocation()));
        }
    }
}
