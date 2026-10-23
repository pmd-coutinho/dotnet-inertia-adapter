using System.Text.Json;
using FluentAssertions;
using InertiaNet.Pathfinder.Tests.TestHelpers;

namespace InertiaNet.Pathfinder.Tests.Delivery;

public class PathfinderCliTests
{
    [Theory]
    [InlineData("--unknown")]
    [InlineData("--project")]
    [InlineData("--actions", "maybe")]
    [InlineData("--debounce", "0")]
    [InlineData("--check", "--watch")]
    public async Task InvalidArguments_ReturnStructuredFailure(params string[] arguments)
    {
        using var project = new TemporaryProject();
        var result = await TemporaryProject.RunAsync(project.Root,
            [TemporaryProject.ToolAssembly, "--diagnostics-json", .. arguments]);
        result.ExitCode.Should().Be(2, result.Output + result.Error);
        var diagnostics = JsonDocument.Parse(result.Error).RootElement;
        diagnostics[0].GetProperty("code").GetString().Should().Be("PATHFINDER900");
        diagnostics[0].GetProperty("severity").GetString().Should().Be("error");
    }

    [Fact]
    public async Task QuietAndStrict_DiagnosticsRemainVisible_AndLastGoodOutputIsPreserved()
    {
        using var project = new TemporaryProject();
        project.File("Program.cs", "app.MapGet(\"/users\", () => \"users\").WithName(\"users.index\");");
        var args = new[] { TemporaryProject.ToolAssembly, "-p", project.Root, "-o", project.PathFor("output"), "--quiet" };
        var initial = await TemporaryProject.RunAsync(project.Root, args);
        initial.ExitCode.Should().Be(0, initial.Output + initial.Error);
        initial.Output.Should().BeEmpty();
        var output = Directory.GetFiles(project.PathFor("output"), "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        project.File("Program.cs", "app.MapGet(\"/users\", ExistingHandler).WithName(\"users.index\");");
        var strict = await TemporaryProject.RunAsync(project.Root, [.. args, "--strict", "--diagnostics-json"]);
        strict.ExitCode.Should().Be(1, strict.Output + strict.Error);
        strict.Output.Should().BeEmpty();
        JsonDocument.Parse(strict.Error).RootElement.EnumerateArray().Should().Contain(
            item => item.GetProperty("code").GetString() == "PATHFINDER101");
        foreach (var (path, bytes) in output) File.ReadAllBytes(path).Should().Equal(bytes);
        var permissive = await TemporaryProject.RunAsync(project.Root, args);
        permissive.ExitCode.Should().Be(0, permissive.Error);
        permissive.Error.Should().Contain("warning PATHFINDER101");
    }

    [Fact]
    public async Task ContractFile_IsAnInput_AndProfileChangesInvalidateCheckWithoutWriting()
    {
        using var project = new TemporaryProject();
        project.File("Program.cs", """
            class Props { public string DisplayName { get; set; } }
            class Page { void Index() { inertia.Render("Home", new Props()); } }
            """);
        var file = project.File("wire.json", """{"output":{"naming":"snake_case"}}""");
        var args = new[] { TemporaryProject.ToolAssembly, "-p", project.Root, "-o", project.PathFor("output"), "--contracts", file, "--quiet" };
        var initial = await TemporaryProject.RunAsync(project.Root, args);
        initial.ExitCode.Should().Be(0, initial.Error);
        var page = project.PathFor("output/types/Home.ts");
        File.ReadAllText(page).Should().Contain("display_name");
        File.ReadAllLines(project.PathFor("output/.pathfinder-inputs")).Should().Contain("wire.json");
        var current = await TemporaryProject.RunAsync(project.Root, [..args, "--check"]);
        current.ExitCode.Should().Be(0, current.Error);
        File.WriteAllText(file, """{"output":{"naming":"preserve"}}""");
        var stale = await TemporaryProject.RunAsync(project.Root, [..args, "--check"]);
        stale.ExitCode.Should().Be(1, stale.Error);
        File.ReadAllText(page).Should().Contain("display_name");
        var regenerated = await TemporaryProject.RunAsync(project.Root, args);
        regenerated.ExitCode.Should().Be(0, regenerated.Error);
        File.ReadAllText(page).Should().Contain("DisplayName");
    }

    [Fact]
    public async Task Check_ReturnsStaleExitCode_AndDoesNotWrite()
    {
        using var project = new TemporaryProject();
        project.File("Program.cs", "app.MapGet(\"/\", () => \"hello\");");
        var result = await TemporaryProject.RunAsync(project.Root, TemporaryProject.ToolAssembly,
            "-p", project.Root, "-o", project.PathFor("output"), "--check", "--quiet", "--diagnostics-json");
        result.ExitCode.Should().Be(1, result.Error);
        Directory.Exists(project.PathFor("output")).Should().BeFalse();
        JsonDocument.Parse(result.Error).RootElement[0].GetProperty("code").GetString().Should().Be("PATHFINDER300");
    }
}
