using System.Text.Json;

namespace InertiaNet.Pathfinder.Analysis;

internal sealed record PathfinderDiagnostic(string Code, string File, int Line, string Message, string Severity = "warning")
{
    public override string ToString() => $"{File}({Line}): {Severity} {Code}: {Message}";
}

internal static class PathfinderDiagnostics
{
    private static readonly AsyncLocal<List<PathfinderDiagnostic>?> State = new();
    public static IReadOnlyList<PathfinderDiagnostic> Entries => State.Value is { } entries ? entries : [];
    public static IReadOnlyList<string> Current => Entries.Select(entry => entry.ToString()).ToArray();
    public static void Clear() => State.Value = [];
    public static void Report(string filePath, int lineNumber, string message, string code = "PATHFINDER100", string severity = "warning")
        => (State.Value ??= []).Add(new(code, filePath, lineNumber, message, severity));

    public static void Write(TextWriter writer, bool json)
    {
        var entries = Entries.OrderBy(entry => entry.File, StringComparer.Ordinal).ThenBy(entry => entry.Line)
            .ThenBy(entry => entry.Code, StringComparer.Ordinal).ThenBy(entry => entry.Message, StringComparer.Ordinal).ToArray();
        if (json) writer.WriteLine(JsonSerializer.Serialize(entries, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        else foreach (var entry in entries) writer.WriteLine(entry);
    }
}
