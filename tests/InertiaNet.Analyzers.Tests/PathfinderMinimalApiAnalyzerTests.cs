using FluentAssertions;
using InertiaNet.Analyzers.Tests.TestHelpers;

namespace InertiaNet.Analyzers.Tests;

public class PathfinderMinimalApiAnalyzerTests
{
    [Fact]
    public async Task ShouldReportUnsupportedDynamicTemplates_AndMethodGroupHandlers()
    {
        const string programSource = """
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

class Demo
{
    void Test(WebApplication app)
    {
        var template = GetTemplate();
        app.MapGet(template, ExternalHandlers.GetUser);
    }

    string GetTemplate() => "/users/{id:int}";
}
""";

        const string handlersSource = """
using Microsoft.AspNetCore.Http;

static class ExternalHandlers
{
    public static IResult GetUser(int id) => Results.Ok();
}
""";

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            [
                ("/tmp/Program.cs", programSource),
                ("/tmp/ExternalHandlers.cs", handlersSource),
            ],
            new PathfinderMinimalApiAnalyzer());

        diagnostics.Select(diagnostic => diagnostic.Id)
            .Should().BeEquivalentTo(["PATHFINDER001", "PATHFINDER002"]);
    }

    [Fact]
    public async Task ShouldIgnoreSupportedMinimalApiPatterns()
    {
        const string source = """
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

class Demo
{
    void Test(WebApplication app)
    {
        const string ApiPrefix = "/api";
        const string UsersPrefix = "/users";
        const string Template = "/{id:int}";

        var users = app.MapGroup(ApiPrefix).MapGroup(UsersPrefix);
        users.MapGet(Template, (int id) => Results.Ok());
        users.MapDelete(Template, DeleteUser);
        users.MapPost("/", UserHandlers.Store);
    }

    static IResult DeleteUser(int id) => Results.Ok();

    static class UserHandlers
    {
        public static IResult Store() => Results.Ok();
    }
}
""";

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(source, new PathfinderMinimalApiAnalyzer());

        diagnostics.Should().BeEmpty();
    }
}
