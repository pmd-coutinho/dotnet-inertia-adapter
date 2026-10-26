using FluentAssertions;
using InertiaNet.Pathfinder.Analysis;
using Microsoft.CodeAnalysis.CSharp;

namespace InertiaNet.Pathfinder.Tests.Analysis;

public class ControllerRouteDiscovererTests
{
    [Fact]
    public void SemanticDiscovery_ResolvesCrossFileConstantsAndNamedConstructorArguments()
    {
        PathfinderDiagnostics.Clear();
        var constants = CSharpSyntaxTree.ParseText("""
            public static class Routes {
                public const string Prefix = "users";
                public const string Show = "{id:int}";
                public const string Name = "users.show";
            }
            """, path: "Routes.cs");
        var tree = CSharpSyntaxTree.ParseText("""
            using Microsoft.AspNetCore.Mvc;
            [Route(Routes.Prefix)]
            public class UsersController : Controller {
                [HttpGet(template: Routes.Show, Name = Routes.Name)]
                public object Show(int id) => null;
            }
            """, path: "UsersController.cs");
        var compilation = ProjectCompilationLoader.CreateForSources([tree, constants]);
        var routes = ControllerRouteDiscoverer.Discover(tree, compilation.GetSemanticModel(tree));
        routes.Should().ContainSingle().Which.UrlTemplate.Should().Be("/users/{id}");
        routes[0].RouteName.Should().Be("users.show");
        routes[0].Parameters.Should().ContainSingle().Which.ClrTypeName.Should().Be("int");
        PathfinderDiagnostics.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData("[Route(Unknown.Prefix)]", "[HttpGet]", 1)]
    [InlineData("[Route(\"users\")]", "[HttpGet(Unknown.Template)]", 3)]
    [InlineData("", "[HttpGet]", 3)]
    public void UnresolvedRoutes_AreOmittedWithSourceDiagnostics(string classRoute, string methodRoute, int line)
    {
        PathfinderDiagnostics.Clear();
        var tree = CSharpSyntaxTree.ParseText($"{classRoute}\npublic class UsersController {{\n    {methodRoute}\n    public object Index() => null;\n}}", path: "UsersController.cs");
        var compilation = ProjectCompilationLoader.CreateForSources([tree]);
        ControllerRouteDiscoverer.Discover(tree, compilation.GetSemanticModel(tree)).Should().BeEmpty();
        var diagnostic = PathfinderDiagnostics.Entries.Should().ContainSingle().Subject;
        diagnostic.Code.Should().Be("PATHFINDER102");
        diagnostic.File.Should().Be("UsersController.cs");
        diagnostic.Line.Should().Be(line);
        diagnostic.Message.Should().Contain("omitted");
    }

    [Theory]
    [InlineData("", "[HttpGet(\"\")]")]
    [InlineData("", "[HttpGet(\"/\")]")]
    [InlineData("[Route(\"\")]", "[HttpGet]")]
    [InlineData("[Route(\"/\")]", "[HttpGet]")]
    public void ExplicitRootRoutes_RemainValid(string classRoute, string methodRoute)
    {
        PathfinderDiagnostics.Clear();
        var tree = CSharpSyntaxTree.ParseText($"{classRoute} public class RootController {{ {methodRoute} public object Index() => null; }}");
        ControllerRouteDiscoverer.Discover(tree).Should().ContainSingle().Which.UrlTemplate.Should().Be("/");
        PathfinderDiagnostics.Entries.Should().BeEmpty();
    }

    [Fact]
    public void UnresolvedTemplates_DoNotSuppressIndependentAbsoluteOrSiblingRoutes()
    {
        PathfinderDiagnostics.Clear();
        var tree = CSharpSyntaxTree.ParseText("""
            [Route(Unknown.Prefix)]
            public class UsersController {
                [HttpGet] public object Missing() => null;
                [HttpGet("~/absolute")] public object Absolute() => null;
            }
            [Route("valid")]
            public class ValidController {
                [HttpGet(Unknown.Template), HttpGet("literal")]
                public object Index() => null;
            }
            """, path: "Controllers.cs");
        var routes = ControllerRouteDiscoverer.Discover(tree);
        routes.Select(route => route.UrlTemplate).Should().Equal("/absolute", "/valid/literal");
        PathfinderDiagnostics.Entries.Should().HaveCount(2);
    }

    [Fact]
    public void Discover_ShouldHandleSupportedMvcPatterns()
    {
        const string source = """
namespace Demo.Controllers;

[Area("Admin")]
[Host("https://admin.example.com")]
[Route("[area]/[controller]")]
public class PostsController : Controller
{
    [HttpGet("{id:int}/{slug?}", Name = "Posts.Show")]
    public IActionResult Show([FromRoute] int id, string? slug = null) => Ok();

    [Host("tenant.example.com")]
    [HttpPut("{id:int}")]
    public IActionResult Update(int id, [FromBody] UpdatePostRequest request) => Ok();
}

public record UpdatePostRequest(string Title);
""";

        var tree = CSharpSyntaxTree.ParseText(source, path: "Controllers/PostsController.cs");

        var routes = ControllerRouteDiscoverer.Discover(tree);

        routes.Should().HaveCount(2);

        var show = routes.Single(route => route.ActionName == "Show");
        show.HttpMethods.Should().Equal("get");
        show.UrlTemplate.Should().Be("/admin/posts/{id}/{slug?}");
        show.RouteName.Should().Be("Posts.Show");
        show.Domain.Should().Be("admin.example.com");
        show.Scheme.Should().Be("https");
        show.Parameters.Should().ContainSingle(parameter => parameter.Name == "id" && parameter.ClrTypeName == "int" && !parameter.IsOptional);
        show.Parameters.Should().ContainSingle(parameter => parameter.Name == "slug" && parameter.ClrTypeName == "string" && parameter.IsOptional);

        var update = routes.Single(route => route.ActionName == "Update");
        update.HttpMethods.Should().Equal("put");
        update.UrlTemplate.Should().Be("/admin/posts/{id}");
        update.BodyTypeName.Should().Be("UpdatePostRequest");
        update.Domain.Should().Be("tenant.example.com");
        update.Scheme.Should().BeNull();
        update.Parameters.Should().ContainSingle(parameter => parameter.Name == "id");
    }
}
