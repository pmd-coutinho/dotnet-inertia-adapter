using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace InertiaNet.Analyzers.Tests.TestHelpers;

internal static class AnalyzerTestHost
{
    public static async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string source, params DiagnosticAnalyzer[] analyzers)
        => await GetDiagnosticsAsync([(string.Empty, source)], analyzers);

    public static async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(
        IEnumerable<(string path, string source)> sources,
        params DiagnosticAnalyzer[] analyzers)
    {
        var trees = sources
            .Select(source => CSharpSyntaxTree.ParseText(source.source, path: source.path))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: "AnalyzerTests",
            syntaxTrees: trees,
            references: GetReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create(analyzers)).GetAnalyzerDiagnosticsAsync();
        compilation.GetDiagnostics().Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error,
            "the analyzer test input should compile successfully");

        return diagnostics.OrderBy(diagnostic => diagnostic.Id, StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<MetadataReference> GetReferences()
    {
        var assemblies = new[]
        {
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(HttpContext).Assembly,
            typeof(Results).Assembly,
            typeof(WebApplication).Assembly,
            typeof(ControllerBase).Assembly,
            typeof(IEndpointRouteBuilder).Assembly,
            typeof(System.Text.Json.JsonSerializerOptions).Assembly,
            typeof(InertiaNet.Core.IInertiaService).Assembly,
        };

        return assemblies
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Concat(AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .Select(assembly => MetadataReference.CreateFromFile(assembly.Location)))
            .DistinctBy(reference => reference.Display, StringComparer.Ordinal);
    }
}
