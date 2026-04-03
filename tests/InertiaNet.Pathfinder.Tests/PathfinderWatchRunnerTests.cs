using FluentAssertions;

namespace InertiaNet.Pathfinder.Tests;

public class PathfinderWatchRunnerTests
{
    [Theory]
    [InlineData("/repo/src/Program.cs", true)]
    [InlineData("/repo/src/Features/Users.cs", true)]
    [InlineData("/repo/src/obj/Debug/net10.0/Generated.cs", false)]
    [InlineData("/repo/src/bin/Debug/net10.0/Program.cs", false)]
    [InlineData("/repo/src/appsettings.json", false)]
    [InlineData("", false)]
    public void ShouldProcessPath_ShouldFilterExpectedPaths(string path, bool expected)
    {
        PathfinderWatchRunner.ShouldProcessPath(path).Should().Be(expected);
    }
}
