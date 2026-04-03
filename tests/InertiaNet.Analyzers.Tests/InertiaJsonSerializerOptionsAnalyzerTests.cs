using FluentAssertions;
using InertiaNet.Analyzers.Tests.TestHelpers;

namespace InertiaNet.Analyzers.Tests;

public class InertiaJsonSerializerOptionsAnalyzerTests
{
    [Fact]
    public async Task ShouldReportDirectPolicyAssignments_OnInertiaOptionsJsonSerializerOptions()
    {
        const string source = """
using System.Text.Json;
using InertiaNet.Core;

class Demo
{
    void Configure(InertiaOptions options)
    {
        options.JsonSerializerOptions = new JsonSerializerOptions();
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    }
}
""";

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(source, new InertiaJsonSerializerOptionsAnalyzer());

        diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "INERTIA002");
    }

    [Fact]
    public async Task ShouldReportObjectInitializerPolicyAssignments_OnInertiaOptionsJsonSerializerOptions()
    {
        const string source = """
using System.Text.Json;
using InertiaNet.Core;

class Demo
{
    void Configure(InertiaOptions options)
    {
        options.JsonSerializerOptions = new JsonSerializerOptions
        {
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
    }
}
""";

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(source, new InertiaJsonSerializerOptionsAnalyzer());

        diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "INERTIA002");
    }

    [Fact]
    public async Task ShouldIgnoreNullPolicyAssignments()
    {
        const string source = """
using InertiaNet.Core;

class Demo
{
    void Configure(InertiaOptions options)
    {
        options.JsonSerializerOptions = null;
    }
}
""";

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(source, new InertiaJsonSerializerOptionsAnalyzer());

        diagnostics.Should().BeEmpty();
    }
}
