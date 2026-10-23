namespace InertiaNet.Pathfinder.Generation;

internal static class TypeReferenceCollector
{
    private static readonly HashSet<string> BuiltInTsTypes =
    [
        "string", "number", "boolean", "unknown", "null", "undefined", "any", "void", "never", "object",
        "Record", "File", "Blob", "Date", "bigint",
    ];

    public static HashSet<string> Collect(string tsType)
    {
        var references = new HashSet<string>();
        CollectInto(tsType, references);
        return references;
    }

    public static void CollectInto(string tsType, HashSet<string> references)
    {
        foreach (var part in SplitTopLevel(tsType, '|'))
        {
            CollectSingle(part.Trim(), references);
        }
    }

    public static string RemoveNull(string tsType)
        => string.Join(" | ", SplitTopLevel(tsType, '|').Select(part => part.Trim()).Where(part => part != "null"));

    public static bool HasNull(string tsType) => SplitTopLevel(tsType, '|').Any(part => part.Trim() == "null");

    public static string Replace(string tsType, IReadOnlyDictionary<string, string> replacements)
        => System.Text.RegularExpressions.Regex.Replace(tsType,
            "\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|[\\p{L}_$][\\p{L}\\p{N}_$]*", match =>
            {
                if (match.Value.StartsWith('\"') || match.Value.StartsWith('\'')) return match.Value;
                // Object keys are labels, not type references.
                var remaining = tsType.AsSpan(match.Index + match.Length).TrimStart();
                if (remaining.StartsWith(":" ) || remaining.StartsWith("?:")) return match.Value;
                return replacements.TryGetValue(match.Value, out var replacement) ? replacement : match.Value;
            });

    private static void CollectSingle(string tsType, HashSet<string> references)
    {
        if (string.IsNullOrWhiteSpace(tsType))
            return;

        while (tsType.EndsWith("[]", StringComparison.Ordinal))
            tsType = tsType[..^2].Trim();

        if (tsType.StartsWith('(') && tsType.EndsWith(')'))
        {
            CollectInto(tsType[1..^1], references);
            return;
        }
        if (tsType.StartsWith('\"') || tsType.StartsWith('\'')) return;

        if (BuiltInTsTypes.Contains(tsType))
            return;

        if (tsType.StartsWith('{') && tsType.EndsWith('}'))
        {
            foreach (var property in SplitTopLevel(tsType[1..^1], ';'))
            {
                var colonIndex = FindTopLevel(property, ':');
                if (colonIndex < 0)
                    continue;

                CollectInto(property[(colonIndex + 1)..].Trim(), references);
            }

            return;
        }

        var genericStart = FindTopLevel(tsType, '<');
        if (genericStart >= 0 && tsType.EndsWith('>'))
        {
            var outerType = tsType[..genericStart].Trim();
            if (!BuiltInTsTypes.Contains(outerType))
                references.Add(outerType);

            foreach (var argument in SplitTopLevel(tsType[(genericStart + 1)..^1], ','))
            {
                CollectInto(argument.Trim(), references);
            }

            return;
        }

        references.Add(tsType);
    }

    private static IEnumerable<string> SplitTopLevel(string value, char separator)
    {
        if (string.IsNullOrEmpty(value))
            yield break;

        var start = 0;
        var depth = 0;
        char quote = '\0';

        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (quote != '\0') { if (current == '\\') index++; else if (current == quote) quote = '\0'; continue; }
            if (current is '\"' or '\'') { quote = current; continue; }
            switch (current)
            {
                case '<':
                case '{':
                case '[':
                case '(':
                    depth++;
                    break;
                case '>':
                case '}':
                case ']':
                case ')':
                    depth--;
                    break;
                default:
                    if (current == separator && depth == 0)
                    {
                        yield return value[start..index];
                        start = index + 1;
                    }
                    break;
            }
        }

        yield return value[start..];
    }

    private static int FindTopLevel(string value, char target)
    {
        var depth = 0;
        char quote = '\0';

        for (var index = 0; index < value.Length; index++)
        {
            if (quote != '\0') { if (value[index] == '\\') index++; else if (value[index] == quote) quote = '\0'; continue; }
            if (value[index] is '\"' or '\'') { quote = value[index]; continue; }
            if (value[index] == target && depth == 0) return index;
            switch (value[index])
            {
                case '<':
                case '{':
                case '[':
                case '(':
                    depth++;
                    break;
                case '>':
                case '}':
                case ']':
                case ')':
                    depth--;
                    break;
            }
        }

        return -1;
    }
}
