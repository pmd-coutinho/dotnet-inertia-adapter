using FluentAssertions;
using InertiaNet.Analyzers.Tests.TestHelpers;

namespace InertiaNet.Analyzers.Tests;

public class PathfinderMinimalApiAnalyzerTests
{
    [Fact]
    public async Task ShouldReportUnsupportedDynamicTemplates_AndMethodGroupHandlers()
    {
        const string source = """
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

class Demo
{
    void Test(WebApplication app)
    {
        var template = GetTemplate();
        app.MapGet(template, GetUser);
    }

    string GetTemplate() => "/users/{id:int}";
    static IResult GetUser(int id) => Results.Ok();
}
""";

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(source, new PathfinderMinimalApiAnalyzer());

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
    }
}
""";

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(source, new PathfinderMinimalApiAnalyzer());

        diagnostics.Should().BeEmpty();
    }
}
