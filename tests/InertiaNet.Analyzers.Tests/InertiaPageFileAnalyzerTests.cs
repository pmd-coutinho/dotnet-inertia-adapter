using FluentAssertions;
using InertiaNet.Analyzers.Tests.TestHelpers;

namespace InertiaNet.Analyzers.Tests;

public class InertiaPageFileAnalyzerTests
{
    [Fact]
    public async Task ShouldReportMissingPageFile_WhenPageValidationIsEnabled()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("inertia-page-analyzer-");

        try
        {
            File.WriteAllText(Path.Combine(tempDirectory.FullName, "Demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

            var sourcePath = Path.Combine(tempDirectory.FullName, "Program.cs");
            var source = """
using InertiaNet.Core;

class Demo
{
    void Configure(InertiaOptions options, IInertiaService inertia)
    {
        options.Pages.EnsurePagesExist = true;
        options.Pages.Paths = ["src/pages"];

        inertia.Render("Users/Index");
    }
}
""";

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync([(sourcePath, source)], new InertiaPageFileAnalyzer());

            diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "INERTIA003");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ShouldNotReport_WhenConfiguredPageFileExists()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("inertia-page-analyzer-");

        try
        {
            File.WriteAllText(Path.Combine(tempDirectory.FullName, "Demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            Directory.CreateDirectory(Path.Combine(tempDirectory.FullName, "src", "pages", "Users"));
            File.WriteAllText(Path.Combine(tempDirectory.FullName, "src", "pages", "Users", "Index.tsx"), "export default {}\n");

            var sourcePath = Path.Combine(tempDirectory.FullName, "Program.cs");
            var source = """
using InertiaNet.Core;

class Demo
{
    void Configure(InertiaOptions options, IInertiaService inertia)
    {
        options.Pages.EnsurePagesExist = true;
        options.Pages.Paths = ["src/pages"];

        inertia.Render("Users/Index");
    }
}
""";

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync([(sourcePath, source)], new InertiaPageFileAnalyzer());

            diagnostics.Should().BeEmpty();
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ShouldNotReport_WhenPageValidationIsDisabled()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("inertia-page-analyzer-");

        try
        {
            File.WriteAllText(Path.Combine(tempDirectory.FullName, "Demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

            var sourcePath = Path.Combine(tempDirectory.FullName, "Program.cs");
            var source = """
using InertiaNet.Core;

class Demo
{
    void Configure(InertiaOptions options, IInertiaService inertia)
    {
        inertia.Render("Users/Index");
    }
}
""";

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync([(sourcePath, source)], new InertiaPageFileAnalyzer());

            diagnostics.Should().BeEmpty();
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }
}
