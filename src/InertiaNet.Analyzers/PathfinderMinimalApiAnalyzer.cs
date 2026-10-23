using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace InertiaNet.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PathfinderMinimalApiAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> SupportedMapMethods =
    ["MapGet", "MapPost", "MapPut", "MapPatch", "MapDelete", "MapMethods", "MapInertia"];

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

        if (memberAccess.Name.Identifier.Text == "MapInertia") return;
        var handlerIndex = memberAccess.Name.Identifier.Text == "MapMethods" ? 2 : 1;
        if (args.Count <= handlerIndex) return;
        var handler = args[handlerIndex].Expression;
        if (!IsSupportedHandler(context.SemanticModel, handler))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InertiaDiagnostics.UnsupportedMinimalApiHandler,
                args[handlerIndex].GetLocation()));
        }
    }

    private static bool IsSupportedHandler(SemanticModel semanticModel, ExpressionSyntax handler)
    {
        if (handler is ParenthesizedLambdaExpressionSyntax or SimpleLambdaExpressionSyntax)
            return true;

        var symbolInfo = semanticModel.GetSymbolInfo(handler);
        var candidates = semanticModel.GetMemberGroup(handler).OfType<IMethodSymbol>().ToArray();
        var symbol = symbolInfo.Symbol as IMethodSymbol ?? (candidates.Length == 1 ? candidates[0] : null);
        if (symbol == null)
            return false;

        return symbol.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax() is MethodDeclarationSyntax or LocalFunctionStatementSyntax);
    }
}
