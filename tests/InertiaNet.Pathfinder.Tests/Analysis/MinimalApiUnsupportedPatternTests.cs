using FluentAssertions;
using InertiaNet.Pathfinder.Analysis;
using Microsoft.CodeAnalysis.CSharp;

namespace InertiaNet.Pathfinder.Tests.Analysis;

public class MinimalApiUnsupportedPatternTests
{
    [Fact]
    public void Discover_ShouldReportAndSkip_NonLiteralRouteTemplates()
    {
        PathfinderDiagnostics.Clear();

        const string source = """
var template = GetTemplate();
app.MapGet(template, (int id) => TypedResults.Ok());

static string GetTemplate() => "/posts/{id:int}";
""";

        var tree = CSharpSyntaxTree.ParseText(source, path: "Program.cs");

        var routes = MinimalApiRouteDiscoverer.Discover(tree);

        routes.Should().BeEmpty();
        PathfinderDiagnostics.Current.Should().ContainSingle(message =>
            message.Contains("route template could not be resolved statically", StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_ShouldReportAndSkip_MethodGroupHandlers()
    {
        PathfinderDiagnostics.Clear();

        const string source = """
app.MapGet("/posts/{id:int}", GetPost);

static IResult GetPost(int id) => TypedResults.Ok();
""";

        var tree = CSharpSyntaxTree.ParseText(source, path: "Program.cs");

        var routes = MinimalApiRouteDiscoverer.Discover(tree);

        routes.Should().BeEmpty();
        PathfinderDiagnostics.Current.Should().ContainSingle(message =>
            message.Contains("only lambda handlers are currently supported", StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_ShouldReportAndSkip_InlineMapGroupChains()
    {
        PathfinderDiagnostics.Clear();

        const string source = """
app.MapGroup(GetPrefix()).MapGet("/posts", () => TypedResults.Ok());

static string GetPrefix() => "/api";
""";

        var tree = CSharpSyntaxTree.ParseText(source, path: "Program.cs");

        var routes = MinimalApiRouteDiscoverer.Discover(tree);

        routes.Should().BeEmpty();
        PathfinderDiagnostics.Current.Should().ContainSingle(message =>
            message.Contains("inline MapGroup chain could not be resolved statically", StringComparison.Ordinal));
    }
}
