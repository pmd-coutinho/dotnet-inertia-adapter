using FluentAssertions;
using InertiaNet.Pathfinder.Analysis;
using Microsoft.CodeAnalysis.CSharp;

namespace InertiaNet.Pathfinder.Tests.Analysis;

public class ControllerRouteDiscovererTests
{
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
        show.HttpMethods.Should().Equal("get", "head");
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
