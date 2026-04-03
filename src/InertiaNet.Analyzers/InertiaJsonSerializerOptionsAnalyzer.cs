using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace InertiaNet.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InertiaJsonSerializerOptionsAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> PolicyPropertyNames = ["PropertyNamingPolicy", "DictionaryKeyPolicy"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => [InertiaDiagnostics.JsonSerializerPolicyIgnored];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, Microsoft.CodeAnalysis.CSharp.SyntaxKind.SimpleAssignmentExpression);
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not AssignmentExpressionSyntax assignment)
            return;

        string? targetPropertyName = null;
        Location? location = null;

        if (assignment.Left is MemberAccessExpressionSyntax leftMemberAccess &&
            context.SemanticModel.GetSymbolInfo(leftMemberAccess.Name).Symbol is IPropertySymbol targetProperty &&
            PolicyPropertyNames.Contains(targetProperty.Name) &&
            !IsNullLiteral(assignment.Right) &&
            leftMemberAccess.Expression is MemberAccessExpressionSyntax serializerOptionsAccess &&
            IsInertiaJsonSerializerOptionsAccess(context.SemanticModel, serializerOptionsAccess))
        {
            targetPropertyName = targetProperty.Name;
            location = leftMemberAccess.Name.GetLocation();
        }
        else if (assignment.Left is IdentifierNameSyntax identifier &&
                 PolicyPropertyNames.Contains(identifier.Identifier.Text) &&
                 !IsNullLiteral(assignment.Right) &&
                 IsInsideInertiaJsonSerializerOptionsInitializer(context.SemanticModel, assignment))
        {
            targetPropertyName = identifier.Identifier.Text;
            location = identifier.GetLocation();
        }

        if (targetPropertyName is null || location is null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            InertiaDiagnostics.JsonSerializerPolicyIgnored,
            location,
            targetPropertyName));
    }

    private static bool IsInertiaJsonSerializerOptionsAccess(SemanticModel semanticModel, MemberAccessExpressionSyntax memberAccess)
    {
        if (semanticModel.GetSymbolInfo(memberAccess.Name).Symbol is not IPropertySymbol propertySymbol)
            return false;

        return propertySymbol.Name == "JsonSerializerOptions"
            && propertySymbol.ContainingType.ToDisplayString() == "InertiaNet.Core.InertiaOptions";
    }

    private static bool IsInsideInertiaJsonSerializerOptionsInitializer(SemanticModel semanticModel, AssignmentExpressionSyntax assignment)
    {
        var outerAssignment = assignment.Ancestors()
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(candidate =>
                candidate.Right.Span.Contains(assignment.Span) &&
                candidate.Left is MemberAccessExpressionSyntax);

        if (outerAssignment?.Left is not MemberAccessExpressionSyntax outerMemberAccess)
            return false;

        return outerMemberAccess.Name.Identifier.Text == "JsonSerializerOptions"
            && IsInertiaJsonSerializerOptionsAccess(semanticModel, outerMemberAccess);
    }

    private static bool IsNullLiteral(ExpressionSyntax expression)
        => expression is LiteralExpressionSyntax literal && literal.Token.Value is null;
}
