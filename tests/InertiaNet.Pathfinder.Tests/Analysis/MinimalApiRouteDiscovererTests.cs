using FluentAssertions;
using InertiaNet.Pathfinder.Analysis;
using Microsoft.CodeAnalysis.CSharp;

namespace InertiaNet.Pathfinder.Tests.Analysis;

public class MinimalApiRouteDiscovererTests
{
    [Fact]
    public void Discover_ShouldHandleSupportedMinimalApiPatterns()
    {
        const string source = """
const string ApiPrefix = "/api";
const string PostsPrefix = "/posts";
const string ShowTemplate = "/{id:int}/{slug?}";

var posts = app.MapGroup(ApiPrefix).MapGroup(PostsPrefix);

posts.MapGet(ShowTemplate, (int id, string? slug = null) => TypedResults.Ok())
    .WithName("Posts.Show")
    .RequireHost("https://tenant.example.com");

posts.MapPut("/{id:int}", (int id, [FromBody] UpdatePostRequest request) => TypedResults.Ok());
posts.MapDelete("/{id:int}", DeletePost);
posts.MapPost("/", PostHandlers.Store);

public record UpdatePostRequest(string Title);
public record CreatePostRequest(string Title);

static IResult DeletePost(int id) => TypedResults.Ok();
static class PostHandlers
{
    public static IResult Store([FromBody] CreatePostRequest request) => TypedResults.Ok();
}
""";

        var tree = CSharpSyntaxTree.ParseText(source, path: "Program.cs");

        var routes = MinimalApiRouteDiscoverer.Discover(tree);

        routes.Should().HaveCount(4);

        var show = routes.Single(route => route.RouteName == "Posts.Show");
        show.HttpMethods.Should().Equal("get", "head");
        show.UrlTemplate.Should().Be("/api/posts/{id}/{slug?}");
        show.Domain.Should().Be("tenant.example.com");
        show.Scheme.Should().Be("https");
        show.Parameters.Should().ContainSingle(parameter => parameter.Name == "id" && parameter.ClrTypeName == "int" && !parameter.IsOptional);
        show.Parameters.Should().ContainSingle(parameter => parameter.Name == "slug" && parameter.ClrTypeName == "string" && parameter.IsOptional);

        var update = routes.Single(route => route.HttpMethods.SequenceEqual(["put"]));
        update.UrlTemplate.Should().Be("/api/posts/{id}");
        update.BodyTypeName.Should().Be("UpdatePostRequest");
        update.Parameters.Should().ContainSingle(parameter => parameter.Name == "id");

        var delete = routes.Single(route => route.HttpMethods.SequenceEqual(["delete"]));
        delete.UrlTemplate.Should().Be("/api/posts/{id}");
        delete.Parameters.Should().ContainSingle(parameter => parameter.Name == "id");

        var store = routes.Single(route => route.HttpMethods.SequenceEqual(["post"]));
        store.UrlTemplate.Should().Be("/api/posts/");
        store.BodyTypeName.Should().Be("CreatePostRequest");
    }
}
