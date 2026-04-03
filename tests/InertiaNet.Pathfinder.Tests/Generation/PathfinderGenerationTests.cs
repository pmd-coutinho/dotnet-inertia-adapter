using FluentAssertions;
using InertiaNet.Pathfinder;
using InertiaNet.Pathfinder.Analysis;
using InertiaNet.Pathfinder.Generation;

namespace InertiaNet.Pathfinder.Tests.Generation;

public class PathfinderGenerationTests
{
    [Fact]
    public void RuntimeWriter_ShouldEmitFormDataSupport_AndRequiredParameterValidation()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("pathfinder-runtime-");

        try
        {
            RuntimeFileWriter.Write(tempDirectory.FullName);

            var content = File.ReadAllText(Path.Combine(tempDirectory.FullName, "index.ts"));

            content.Should().Contain("export type FormDefinition<TMethod extends string = string> = { action: string; method: TMethod; data?: Record<string, string> }");
            content.Should().Contain("export function validateParameters(");
            content.Should().Contain("Missing required parameter");
            content.Should().Contain("Optional parameters for route '");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ActionWriter_ShouldGenerateSpoofedFormData_ForUnsupportedHtmlMethods()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("pathfinder-actions-");

        try
        {
            ActionFileWriter.Write(
                tempDirectory.FullName,
                controllerFullName: "MyApp.Controllers.PostsController",
                controllerShortName: "PostsController",
                actions:
                [
                    new RouteInfo(
                        ControllerFullName: "MyApp.Controllers.PostsController",
                        ControllerShortName: "PostsController",
                        ActionName: "Update",
                        HttpMethods: ["put"],
                        UrlTemplate: "/posts/{id}",
                        Parameters: [new RouteParameter("id", "int", false)],
                        RouteName: null)
                ]);

            var content = File.ReadAllText(Path.Combine(tempDirectory.FullName, "actions", "posts.ts"));

            content.Should().Contain("validateParameters(\"update\", update.definition.url, [\"id\"], args as Record<string, unknown>)");
            content.Should().Contain("action: update.url(args, options), method: \"post\", data: { _method: \"put\" },");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ActionWriter_ShouldEmitRequiredAndOptionalValidationArrays()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("pathfinder-actions-optional-");

        try
        {
            ActionFileWriter.Write(
                tempDirectory.FullName,
                controllerFullName: "MyApp.Controllers.PostsController",
                controllerShortName: "PostsController",
                actions:
                [
                    new RouteInfo(
                        ControllerFullName: "MyApp.Controllers.PostsController",
                        ControllerShortName: "PostsController",
                        ActionName: "Show",
                        HttpMethods: ["get"],
                        UrlTemplate: "/posts/{id}/{slug?}",
                        Parameters:
                        [
                            new RouteParameter("id", "int", false),
                            new RouteParameter("slug", "string", true),
                        ],
                        RouteName: null)
                ]);

            var content = File.ReadAllText(Path.Combine(tempDirectory.FullName, "actions", "posts.ts"));

            content.Should().Contain("validateParameters(\"show\", show.definition.url, [\"id\"], args as Record<string, unknown>, [\"slug\"])");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void RouteWriter_ShouldGenerateSpoofedFormData_ForNamedRoutes()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("pathfinder-routes-");

        try
        {
            RouteFileWriter.Write(
                tempDirectory.FullName,
                allRoutes:
                [
                    new RouteInfo(
                        ControllerFullName: "MyApp.Controllers.PostsController",
                        ControllerShortName: "PostsController",
                        ActionName: "Destroy",
                        HttpMethods: ["delete"],
                        UrlTemplate: "/posts/{id}",
                        Parameters: [new RouteParameter("id", "int", false)],
                        RouteName: "Posts.Destroy")
                ]);

            var content = File.ReadAllText(Path.Combine(tempDirectory.FullName, "routes", "Posts", "index.ts"));

            content.Should().Contain("validateParameters(\"Posts.Destroy\", destroy.definition.url, [\"id\"], args as Record<string, unknown>)");
            content.Should().Contain("action: destroy.url(args, options), method: \"post\", data: { _method: \"delete\" },");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void TypeScriptGenerator_ShouldCreateDistinctActionFiles_ForControllersWithSameShortName()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("pathfinder-collisions-");

        try
        {
            TypeScriptGenerator.Generate(
                routes:
                [
                    new RouteInfo(
                        ControllerFullName: "MyApp.Admin.Controllers.PostsController",
                        ControllerShortName: "PostsController",
                        ActionName: "Index",
                        HttpMethods: ["get"],
                        UrlTemplate: "/admin/posts",
                        Parameters: [],
                        RouteName: null),
                    new RouteInfo(
                        ControllerFullName: "MyApp.Public.Controllers.PostsController",
                        ControllerShortName: "PostsController",
                        ActionName: "Index",
                        HttpMethods: ["get"],
                        UrlTemplate: "/public/posts",
                        Parameters: [],
                        RouteName: null),
                ],
                enums: [],
                pageProps: [],
                models: [],
                config: new PathfinderConfig
                {
                    OutputPath = tempDirectory.FullName,
                    GenerateRoutes = false,
                    Quiet = true,
                });

            File.Exists(Path.Combine(tempDirectory.FullName, "actions", "admin", "posts.ts")).Should().BeTrue();
            File.Exists(Path.Combine(tempDirectory.FullName, "actions", "public", "posts.ts")).Should().BeTrue();
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void BarrelWriter_ShouldUseSafeExportNames_ForReservedWords()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("pathfinder-barrels-");

        try
        {
            var actionsDir = Path.Combine(tempDirectory.FullName, "actions");
            Directory.CreateDirectory(actionsDir);
            Directory.CreateDirectory(Path.Combine(actionsDir, "switch"));

            File.WriteAllText(Path.Combine(actionsDir, "class.ts"), "export default {}\n");
            File.WriteAllText(Path.Combine(actionsDir, "switch", "index.ts"), "export const value = 1\n");

            BarrelFileWriter.WriteAll(tempDirectory.FullName);

            var content = File.ReadAllText(Path.Combine(actionsDir, "index.ts"));
            content.Should().Contain("export { default as classMethod } from './class'");
            content.Should().Contain("export * as switchMethod from './switch'");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void RouteWriter_ShouldDisambiguateConflictingSafeExportNames_Deterministically()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("pathfinder-route-names-");

        try
        {
            RouteFileWriter.Write(
                tempDirectory.FullName,
                allRoutes:
                [
                    new RouteInfo(
                        ControllerFullName: "Demo.Controllers.AdminController",
                        ControllerShortName: "AdminController",
                        ActionName: "ClassMethod",
                        HttpMethods: ["get"],
                        UrlTemplate: "/admin/class-method",
                        Parameters: [],
                        RouteName: "Admin.ClassMethod"),
                    new RouteInfo(
                        ControllerFullName: "Demo.Controllers.AdminController",
                        ControllerShortName: "AdminController",
                        ActionName: "Class",
                        HttpMethods: ["get"],
                        UrlTemplate: "/admin/class",
                        Parameters: [],
                        RouteName: "Admin.Class")
                ]);

            var content = File.ReadAllText(Path.Combine(tempDirectory.FullName, "routes", "Admin", "index.ts"));

            content.Should().Contain("export const classMethod =");
            content.Should().Contain("export const classMethod1 =");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void TypeScriptGenerator_ShouldProduceDeterministicOutput_RegardlessOfInputOrder()
    {
        var firstDirectory = Directory.CreateTempSubdirectory("pathfinder-deterministic-a-");
        var secondDirectory = Directory.CreateTempSubdirectory("pathfinder-deterministic-b-");

        try
        {
            var routes = new List<RouteInfo>
            {
                new(
                    ControllerFullName: "Demo.Controllers.UsersController",
                    ControllerShortName: "UsersController",
                    ActionName: "Show",
                    HttpMethods: ["get", "head"],
                    UrlTemplate: "/users/{id}",
                    Parameters: [new RouteParameter("id", "int", false)],
                    RouteName: "Users.Show"),
                new(
                    ControllerFullName: "Demo.Controllers.UsersController",
                    ControllerShortName: "UsersController",
                    ActionName: "Store",
                    HttpMethods: ["post"],
                    UrlTemplate: "/users",
                    Parameters: [],
                    RouteName: "Users.Store",
                    BodyTypeName: "CreateUserRequest")
            };

            var enums = new List<EnumInfo>
            {
                new("Demo.OrderStatus", "OrderStatus",
                [
                    new EnumMember("Pending", "pending"),
                    new EnumMember("Paid", "paid")
                ])
            };

            var pageProps = new List<PagePropsInfo>
            {
                new("Users/Show",
                [
                    new PropField("user", "User", false, false),
                    new PropField("status", "OrderStatus", false, false),
                ])
            };

            var models = new List<ModelInfo>
            {
                new("Demo.User", "User",
                [
                    new ModelProperty("name", "string", false, false),
                ]),
                new("Demo.CreateUserRequest", "CreateUserRequest",
                [
                    new ModelProperty("name", "string", false, false),
                ])
            };

            TypeScriptGenerator.Generate(routes, enums, pageProps, models, new PathfinderConfig
            {
                OutputPath = firstDirectory.FullName,
                Quiet = true,
            });

            TypeScriptGenerator.Generate(
                routes.AsEnumerable().Reverse().ToList(),
                enums.AsEnumerable().Reverse().ToList(),
                pageProps.AsEnumerable().Reverse().ToList(),
                models.AsEnumerable().Reverse().ToList(),
                new PathfinderConfig
                {
                    OutputPath = secondDirectory.FullName,
                    Quiet = true,
                });

            ReadGeneratedFiles(firstDirectory.FullName).Should().Equal(ReadGeneratedFiles(secondDirectory.FullName));
        }
        finally
        {
            firstDirectory.Delete(recursive: true);
            secondDirectory.Delete(recursive: true);
        }
    }

    private static IEnumerable<KeyValuePair<string, string>> ReadGeneratedFiles(string root)
    {
        return Directory.GetFiles(root, "*.ts", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new KeyValuePair<string, string>(
                Path.GetRelativePath(root, path).Replace('\\', '/'),
                File.ReadAllText(path).Replace("\r\n", "\n")));
    }
}
