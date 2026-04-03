using FluentAssertions;
using InertiaNet.Pathfinder.Analysis;
using InertiaNet.Pathfinder.Generation;
using InertiaNet.Pathfinder.Tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp;

namespace InertiaNet.Pathfinder.Tests.Generation;

public class PathfinderGoldenFileTests
{
    [Fact]
    public void ActionWriter_ShouldMatchGoldenSnapshot()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("pathfinder-golden-actions-");

        try
        {
            ActionFileWriter.Write(
                tempDirectory.FullName,
                controllerFullName: "Demo.Controllers.PostsController",
                controllerShortName: "PostsController",
                actions:
                [
                    new RouteInfo(
                        ControllerFullName: "Demo.Controllers.PostsController",
                        ControllerShortName: "PostsController",
                        ActionName: "Show",
                        HttpMethods: ["get", "head"],
                        UrlTemplate: "/posts/{id}/{slug?}",
                        Parameters:
                        [
                            new RouteParameter("id", "int", false),
                            new RouteParameter("slug", "string", true),
                        ],
                        RouteName: null),
                    new RouteInfo(
                        ControllerFullName: "Demo.Controllers.PostsController",
                        ControllerShortName: "PostsController",
                        ActionName: "Update",
                        HttpMethods: ["put"],
                        UrlTemplate: "/posts/{id}",
                        Parameters: [new RouteParameter("id", "int", false)],
                        RouteName: null,
                        BodyTypeName: "UpdatePostRequest")
                ],
                models:
                [
                    new ModelInfo("Demo.UpdatePostRequest", "UpdatePostRequest", []),
                ]);

            var content = File.ReadAllText(Path.Combine(tempDirectory.FullName, "actions", "posts.ts"));
            SnapshotAssert.MatchesFile(content, "../Snapshots/actions.posts.ts");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void RuntimeWriter_ShouldMatchGoldenSnapshot()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("pathfinder-golden-runtime-");

        try
        {
            RuntimeFileWriter.Write(tempDirectory.FullName);

            var content = File.ReadAllText(Path.Combine(tempDirectory.FullName, "index.ts"));
            SnapshotAssert.MatchesFile(content, "../Snapshots/runtime.index.ts");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void MinimalApiActionWriter_ShouldMatchGoldenSnapshot_ForStaticTemplatesAndInlineGroups()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("pathfinder-golden-minimalapi-");

        try
        {
            const string source = """
const string ApiPrefix = "/api";
const string PostsPrefix = "/posts";
const string ShowTemplate = "/{id:int}/{slug?}";

var posts = app.MapGroup(ApiPrefix).MapGroup(PostsPrefix);
posts.MapGet(ShowTemplate, (int id, string? slug = null) => TypedResults.Ok()).WithName("Posts.Show");
posts.MapPost("/", ([FromBody] CreatePostRequest request) => TypedResults.Ok());

public record CreatePostRequest(string Title);
""";

            var tree = CSharpSyntaxTree.ParseText(source, path: "Program.cs");
            var routes = MinimalApiRouteDiscoverer.Discover(tree);
            var models = ModelDiscoverer.Discover([tree], [], routes);

            ActionFileWriter.Write(
                tempDirectory.FullName,
                controllerFullName: "MinimalApi",
                controllerShortName: "MinimalApi",
                actions: routes,
                models: models);

            var content = File.ReadAllText(Path.Combine(tempDirectory.FullName, "actions", "minimalapi.ts"));
            SnapshotAssert.MatchesFile(content, "../Snapshots/actions.minimalapi.ts");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void RouteWriter_ShouldMatchGoldenSnapshot()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("pathfinder-golden-routes-");

        try
        {
            RouteFileWriter.Write(
                tempDirectory.FullName,
                allRoutes:
                [
                    new RouteInfo(
                        ControllerFullName: "Demo.Controllers.PostsController",
                        ControllerShortName: "PostsController",
                        ActionName: "Show",
                        HttpMethods: ["get", "head"],
                        UrlTemplate: "/posts/{id}/{slug?}",
                        Parameters:
                        [
                            new RouteParameter("id", "int", false),
                            new RouteParameter("slug", "string", true),
                        ],
                        RouteName: "Posts.Show"),
                    new RouteInfo(
                        ControllerFullName: "Demo.Controllers.PostsController",
                        ControllerShortName: "PostsController",
                        ActionName: "Update",
                        HttpMethods: ["put"],
                        UrlTemplate: "/posts/{id}",
                        Parameters: [new RouteParameter("id", "int", false)],
                        RouteName: "Posts.Update",
                        BodyTypeName: "UpdatePostRequest")
                ],
                models:
                [
                    new ModelInfo("Demo.UpdatePostRequest", "UpdatePostRequest", []),
                ]);

            var content = File.ReadAllText(Path.Combine(tempDirectory.FullName, "routes", "Posts", "index.ts"));
            SnapshotAssert.MatchesFile(content, "../Snapshots/routes.Posts.index.ts");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ModelDiscoverer_ShouldDiscoverTransitiveModelsReferencedByPageProps()
    {
        const string source = """
namespace Demo.Models;

public record ShowProps(Post Post);
public record Post(string Title, User Author);
public record User(string Name, Profile Profile);
public record Profile(string Bio);
""";

        var tree = CSharpSyntaxTree.ParseText(source, path: "Models.cs");
        var pageProps = new List<PagePropsInfo>
        {
            new("Posts/Show",
            [
                new PropField("post", "Post", false, false),
            ])
        };

        var models = ModelDiscoverer.Discover([tree], pageProps);

        models.Select(model => model.ShortName)
            .Should().BeEquivalentTo(["Post", "User", "Profile"]);
    }

    [Fact]
    public void ModelDiscoverer_ShouldDiscoverBodyDtoModels_FromRoutes()
    {
        const string source = """
namespace Demo.Models;

public record UpdatePostRequest(string Title, User Author);
public record User(string Name);
""";

        var tree = CSharpSyntaxTree.ParseText(source, path: "Models.cs");
        var routes = new List<RouteInfo>
        {
            new(
                ControllerFullName: "Demo.Controllers.PostsController",
                ControllerShortName: "PostsController",
                ActionName: "Update",
                HttpMethods: ["put"],
                UrlTemplate: "/posts/{id}",
                Parameters: [new RouteParameter("id", "int", false)],
                RouteName: null,
                BodyTypeName: "UpdatePostRequest")
        };

        var models = ModelDiscoverer.Discover([tree], [], routes);

        models.Select(model => model.ShortName)
            .Should().BeEquivalentTo(["UpdatePostRequest", "User"]);
    }

    [Fact]
    public void ActionWriter_ShouldFallbackUnknown_ForUnresolvedBodyTypes()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("pathfinder-golden-unknown-body-");

        try
        {
            ActionFileWriter.Write(
                tempDirectory.FullName,
                controllerFullName: "Demo.Controllers.PostsController",
                controllerShortName: "PostsController",
                actions:
                [
                    new RouteInfo(
                        ControllerFullName: "Demo.Controllers.PostsController",
                        ControllerShortName: "PostsController",
                        ActionName: "Store",
                        HttpMethods: ["post"],
                        UrlTemplate: "/posts",
                        Parameters: [],
                        RouteName: null,
                        BodyTypeName: "CreatePostRequest")
                ],
                models: []);

            var content = File.ReadAllText(Path.Combine(tempDirectory.FullName, "actions", "posts.ts"));

            content.Should().Contain("export type PostsStorePayload = unknown");
            content.Should().Contain("store.body = undefined as unknown as unknown");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ModelWriter_ShouldMatchGoldenSnapshot_WithNestedImports()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("pathfinder-golden-models-");

        try
        {
            ModelFileWriter.Write(tempDirectory.FullName,
            [
                new ModelInfo("Demo.Profile", "Profile",
                [
                    new ModelProperty("bio", "string", false, false),
                ]),
                new ModelInfo("Demo.User", "User",
                [
                    new ModelProperty("name", "string", false, false),
                    new ModelProperty("profile", "Profile", false, false),
                ]),
                new ModelInfo("Demo.Post", "Post",
                [
                    new ModelProperty("title", "string", false, false),
                    new ModelProperty("author", "User", false, false),
                    new ModelProperty("reviewers", "User[]", false, true),
                ])
            ]);

            var userContent = File.ReadAllText(Path.Combine(tempDirectory.FullName, "models", "User.ts"));
            var postContent = File.ReadAllText(Path.Combine(tempDirectory.FullName, "models", "Post.ts"));

            SnapshotAssert.MatchesFile(userContent, "../Snapshots/models.User.ts");
            SnapshotAssert.MatchesFile(postContent, "../Snapshots/models.Post.ts");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }
}
