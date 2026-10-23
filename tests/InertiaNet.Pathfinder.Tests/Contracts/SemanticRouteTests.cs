using FluentAssertions;
using InertiaNet.Pathfinder.Analysis;
using Microsoft.CodeAnalysis.CSharp;

namespace InertiaNet.Pathfinder.Tests.Contracts;

public class SemanticRouteTests
{
    [Fact]
    public void SemanticDiscovery_ResolvesCrossFileHandlersConstantsAliasesAndHttpMethodFields()
    {
        PathfinderDiagnostics.Clear();
        var routesTree = CSharpSyntaxTree.ParseText("""
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            var app = WebApplication.CreateBuilder().Build();
            var group = app.MapGroup(Handlers.Prefix);
            group.MapPost(Handlers.Template, Handlers.Store).WithName(Handlers.Name);
            app.MapMethods("/methods", new[] { HttpMethods.Head, HttpMethods.Options }, () => "ok");
            """, path: "Program.cs");
        var handlerTree = CSharpSyntaxTree.ParseText("""
            using Microsoft.AspNetCore.Mvc;
            using RouteId = System.Int32;
            public class Request { public string Title { get; set; } }
            public static class Handlers
            {
                public const string Prefix = "/api";
                public const string Template = "/posts/{id}";
                public const string Name = "Posts.Store";
                public static object Store(RouteId id, [FromBody] Request input) => input;
            }
            """, path: "Handlers.cs");
        var compilation = ProjectCompilationLoader.CreateForSources([routesTree, handlerTree]);
        var discovered = MinimalApiRouteDiscoverer.Discover(routesTree, compilation.GetSemanticModel(routesTree));
        discovered.Should().HaveCount(2, string.Join('\n', PathfinderDiagnostics.Current));
        discovered[0].UrlTemplate.Should().Be("/api/posts/{id}");
        discovered[0].RouteName.Should().Be("Posts.Store");
        discovered[1].HttpMethods.Should().Equal("head", "options");
        var contracts = new SemanticContractDiscoverer(compilation, new()).Discover([routesTree, handlerTree], discovered);
        contracts.Routes[0].Parameters.Single().TypeScriptType.Should().Be("number");
        contracts.Routes[0].BodyTypeScriptType.Should().Be("RequestInput");
        PathfinderDiagnostics.Entries.Should().BeEmpty();
    }

    [Fact]
    public void GroupVariables_WithTheSameNameInDifferentScopes_UseTheirOwnPrefixes()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            using Microsoft.AspNetCore.Builder;
            class Setup
            {
                void First(WebApplication app) { var group = app.MapGroup("/first"); group.MapGet("/", () => "one"); }
                void Second(WebApplication app) { var group = app.MapGroup("/second"); group.MapGet("/", () => "two"); }
            }
            """, path: "Routes.cs");
        var compilation = ProjectCompilationLoader.CreateForSources([tree]);
        var routes = MinimalApiRouteDiscoverer.Discover(tree, compilation.GetSemanticModel(tree));
        routes.Select(route => route.UrlTemplate).Should().Equal("/first/", "/second/");
    }
}
