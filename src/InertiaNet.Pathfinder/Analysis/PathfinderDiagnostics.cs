namespace InertiaNet.Pathfinder.Analysis;

internal static class PathfinderDiagnostics
{
    private static readonly List<string> Messages = [];

    public static IReadOnlyList<string> Current => Messages;

    public static void Clear() => Messages.Clear();

    public static void Report(string filePath, int lineNumber, string message)
        => Messages.Add($"{filePath}:{lineNumber}: {message}");
}
