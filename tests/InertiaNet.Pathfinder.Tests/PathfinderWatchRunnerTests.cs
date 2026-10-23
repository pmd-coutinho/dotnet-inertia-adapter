using FluentAssertions;
using InertiaNet.Pathfinder.Tests.TestHelpers;

namespace InertiaNet.Pathfinder.Tests;

public class PathfinderWatchRunnerTests
{
    [Theory]
    [InlineData("/repo/src/Program.cs", true)]
    [InlineData("/repo/src/Features/Users.cs", true)]
    [InlineData("/repo/src/obj/Debug/net10.0/Generated.cs", false)]
    [InlineData("/repo/src/bin/Debug/net10.0/Program.cs", false)]
    [InlineData("/repo/src/appsettings.json", false)]
    [InlineData("/repo/Directory.Build.props", true)]
    [InlineData("/repo/app/App.csproj", true)]
    [InlineData("/repo/global.json", true)]
    [InlineData("", false)]
    public void ShouldProcessPath_ShouldFilterExpectedPaths(string path, bool expected)
    {
        PathfinderWatchRunner.ShouldProcessPath(path).Should().Be(expected);
    }

    [Fact]
    public async Task Watch_SeesLinkedSourcesProjectChangesAndDirectories_AndStopsOnCancellation()
    {
        using var project = new TemporaryProject();
        project.File("app/Program.cs", "class Program {}");
        var linked = project.File("shared/Linked.cs", "class Linked {}");
        var config = new PathfinderConfig { OutputPath = project.PathFor("app/generated"), Quiet = true, WatchDebounceMs = 20 };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var generated = new SemaphoreSlim(0);
        var watcher = PathfinderWatchRunner.RunAsync(project.PathFor("app"), config,
            () => generated.Release(), cancellation.Token, () => [project.PathFor("shared")]);
        try
        {
            await File.WriteAllTextAsync(linked, "class Linked { public int Id { get; set; } }");
            (await generated.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
            project.File("app/App.csproj", "<Project />");
            (await generated.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
            Directory.CreateDirectory(project.PathFor("app/Features"));
            project.File("app/Features/New.cs", "class New {}");
            (await generated.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        }
        finally
        {
            await cancellation.CancelAsync();
            await watcher.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
