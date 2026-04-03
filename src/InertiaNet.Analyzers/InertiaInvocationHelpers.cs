using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace InertiaNet.Analyzers;

internal static class InertiaInvocationHelpers
{
    public static bool TryGetLiteralComponentName(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        out string componentName,
        out Location location)
    {
        componentName = string.Empty;
        location = invocation.GetLocation();

        if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
            return false;

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
            return false;

        var componentExpression = invocation.ArgumentList.Arguments[componentArgIndex].Expression;
        var constantValue = context.SemanticModel.GetConstantValue(componentExpression);
        if (!constantValue.HasValue || constantValue.Value is not string literalComponentName)
            return false;

        componentName = literalComponentName;
        location = componentExpression.GetLocation();
        return true;
    }
}
