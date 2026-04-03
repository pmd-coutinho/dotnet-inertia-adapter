using System.Runtime.CompilerServices;
using FluentAssertions;

namespace InertiaNet.Pathfinder.Tests.TestHelpers;

internal static class SnapshotAssert
{
    public static void MatchesFile(string actual, string relativeSnapshotPath, [CallerFilePath] string sourceFile = "")
    {
        var snapshotPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, relativeSnapshotPath));
        var expected = File.ReadAllText(snapshotPath);

        Normalize(actual).Should().Be(Normalize(expected), "snapshot mismatch for {0}", Path.GetFileName(snapshotPath));
    }

    private static string Normalize(string value)
        => value.Replace("\r\n", "\n").TrimEnd();
}
