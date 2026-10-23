using System.Net;
using FluentAssertions;
using InertiaNet.Pathfinder.Analysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.TestHost;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaNet.Pathfinder.Tests.Analysis;

public class RoutingConformanceTests
{
    public static IEnumerable<object[]> LinkCases()
    {
        yield return ["/items/{id:int?}", new RouteValueDictionary(), "/items"];
        yield return ["/items/{id:int=1}", new RouteValueDictionary(), "/items"];
        yield return ["/items/{id:int=1}", new RouteValueDictionary { ["id"] = 1 }, "/items/1"];
        yield return ["/items/{id:int=1}", new RouteValueDictionary { ["id"] = "1" }, "/items"];
        yield return ["/items/{id:int=1}", new RouteValueDictionary { ["id"] = 2 }, "/items/2"];
        yield return ["/items/files/{**path}", new RouteValueDictionary { ["path"] = "a/b?c#d" }, "/items/files/a/b%3Fc%23d"];
        yield return ["/items/single/{*path}", new RouteValueDictionary { ["path"] = "a/b" }, "/items/single/a%2Fb"];
        yield return ["/items/file/{name}.{extension?}", new RouteValueDictionary { ["name"] = "report" }, "/items/file/report"];
        yield return ["/items/flag/{enabled:bool}", new RouteValueDictionary { ["enabled"] = false }, "/items/flag/False"];
    }

    [Theory]
    [MemberData(nameof(LinkCases))]
    public void FrameworkLinkGeneration_ConfirmsTheGeneratedUrlFixtures(string template, RouteValueDictionary values, string expected)
    {
        var endpoint = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse(template), 0);
        endpoint.Metadata.Add(new EndpointNameMetadata("fixture"));
        using var services = new ServiceCollection().AddLogging().AddRouting()
            .AddSingleton<EndpointDataSource>(new DefaultEndpointDataSource(endpoint.Build())).BuildServiceProvider();
        services.GetRequiredService<LinkGenerator>().GetPathByName("fixture", values).Should().Be(expected);
    }

    [Fact]
    public void Discovery_UsesTemplateParametersAndBindingAliases()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            [Route("items")]
            public class ItemsController : Controller {
                [HttpGet("{locale}/{id}")] public object Show([FromRoute(Name="id")] int value) => null;
                [HttpGet("{id:int?}")] public object Optional(int? id = null) => null;
                [HttpGet("~/absolute")] public object Absolute() => null;
            }
            """);
        var routes = ControllerRouteDiscoverer.Discover(tree);
        var show = routes.Single(route => route.ActionName == "Show");
        show.Parameters.Select(parameter => parameter.Name).Should().Equal("locale", "id");
        show.Parameters.Single(parameter => parameter.Name == "id").ClrTypeName.Should().Be("int");
        routes.Single(route => route.ActionName == "Optional").Parameters[0].IsOptional.Should().BeTrue();
        routes.Single(route => route.ActionName == "Absolute").UrlTemplate.Should().Be("/absolute");
    }

    [Fact]
    public void Discovery_IncludesMapInertiaAndExplicitHttpMethods()
    {
        var routes = MinimalApiRouteDiscoverer.Discover(CSharpSyntaxTree.ParseText("""
            app.MapInertia("/about/{locale}", "About").WithName("About");
            app.MapMethods("/methods", ["HEAD", "OPTIONS"], () => "ok");
            """));
        routes.Should().HaveCount(2);
        routes[0].ComponentName.Should().Be("About");
        routes[0].Parameters[0].Name.Should().Be("locale");
        routes[1].HttpMethods.Should().Equal("head", "options");
    }

    [Fact]
    public async Task NativeFormOverride_IsAppliedBeforeRouting()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.UseHttpMethodOverride(new HttpMethodOverrideOptions { FormFieldName = "_method" });
        app.UseRouting();
        app.MapPatch("/items/{id:int}", (int id) => Results.Ok(new { id }));
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.PostAsync("/items/42", new FormUrlEncodedContent(new Dictionary<string, string> { ["_method"] = "PATCH" }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"id\":42}");
    }

    [Fact]
    public async Task AspNetQueryBinding_AcceptsGeneratedBooleanArraysAndNestedFields()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(PathfinderQueryProbeController).Assembly);
        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/pathfinder-query?active=true&ids=1&ids=2&filter.name=Ana");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"active\":true,\"ids\":[1,2],\"filter\":{\"name\":\"Ana\"}}");
    }
}

[ApiController, Route("pathfinder-query")]
public class PathfinderQueryProbeController : ControllerBase
{
    [HttpGet] public object Get([FromQuery] QueryFixture query) => query;
}
public class QueryFixture
{
    public bool Active { get; set; }
    public int[] Ids { get; set; } = [];
    public QueryFilter Filter { get; set; } = new();
}
public class QueryFilter { public string? Name { get; set; } }
