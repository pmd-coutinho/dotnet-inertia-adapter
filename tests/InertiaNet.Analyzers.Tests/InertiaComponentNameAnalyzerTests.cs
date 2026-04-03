using FluentAssertions;
using InertiaNet.Analyzers.Tests.TestHelpers;

namespace InertiaNet.Analyzers.Tests;

public class InertiaComponentNameAnalyzerTests
{
    [Fact]
    public async Task ShouldReportInvalidComponentNames_ForMinimalApiAndServiceCalls()
    {
        const string source = """
using InertiaNet.Core;
using InertiaNet.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

class Demo
{
    void Test(IInertiaService inertia, IEndpointRouteBuilder endpoints)
    {
        inertia.Render("/Users/Index");
        InertiaResults.Inertia("Users\\Index");
        endpoints.MapInertia("/about", "About/");
    }
}
""";

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(source, new InertiaComponentNameAnalyzer());

        diagnostics.Should().HaveCount(3);
        diagnostics.Should().OnlyContain(diagnostic => diagnostic.Id == "INERTIA001");
    }

    [Fact]
    public async Task ShouldIgnoreValidComponentNames()
    {
        const string source = """
using InertiaNet.Core;
using InertiaNet.Extensions;

class Demo
{
    void Test(IInertiaService inertia)
    {
        inertia.Render("Users/Index");
        InertiaResults.Inertia("Errors/ServerError");
    }
}
""";

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(source, new InertiaComponentNameAnalyzer());

        diagnostics.Should().BeEmpty();
    }
}
