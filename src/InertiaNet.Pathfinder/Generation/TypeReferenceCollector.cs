namespace InertiaNet.Pathfinder.Generation;

internal static class TypeReferenceCollector
{
    private static readonly HashSet<string> BuiltInTsTypes =
    [
        "string", "number", "boolean", "unknown", "null", "undefined", "any", "void", "never", "object",
        "Record",
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

    private static void CollectSingle(string tsType, HashSet<string> references)
    {
        if (string.IsNullOrWhiteSpace(tsType))
            return;

        while (tsType.EndsWith("[]", StringComparison.Ordinal))
            tsType = tsType[..^2].Trim();

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

        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
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

        for (var index = 0; index < value.Length; index++)
        {
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
                default:
                    if (value[index] == target && depth == 0)
                        return index;
                    break;
            }
        }

        return -1;
    }
}
