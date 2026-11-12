using System.Text;
using System.Text.Json;
using InertiaNet.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InertiaNet.Middleware;

/// <summary>
/// Normalizes ASP.NET Core validation error keys into the field paths used by Inertia forms.
/// Every error-producing path (validation forwarding and Precognition) uses this type.
/// </summary>
internal readonly record struct ValidationErrorKeys(
    JsonNamingPolicy? PropertyNamingPolicy,
    JsonNamingPolicy? DictionaryKeyPolicy,
    ValidationErrorKeyFormat Format)
{
    /// <summary>
    /// Resolves key naming from the configured options, using the same serializer options props are written
    /// with: <see cref="InertiaOptions.JsonSerializerOptions"/> when set, otherwise the adapter defaults
    /// (camelCase members, no dictionary key policy). Custom options with a <see langword="null"/>
    /// <see cref="JsonSerializerOptions.PropertyNamingPolicy"/> leave member segments unrenamed, as props are.
    /// Non-numeric indexers use the dictionary key policy of the same options.
    /// </summary>
    public static ValidationErrorKeys From(InertiaOptions? options, ValidationErrorKeyFormat? format = null)
    {
        var json = options?.JsonSerializerOptions ?? InertiaJsonOptions.Default;
        return new(json.PropertyNamingPolicy, json.DictionaryKeyPolicy,
            format ?? options?.ValidationErrorKeyFormat ?? ValidationErrorKeyFormat.Dots);
    }

    public static ValidationErrorKeys From(HttpContext context, ValidationErrorKeyFormat? format = null)
        => From(context.RequestServices.GetService<IOptions<InertiaOptions>>()?.Value, format);

    /// <summary>Normalizes the keys and merges the messages of keys that normalize to the same path.</summary>
    public Dictionary<string, string[]> Normalize(IEnumerable<KeyValuePair<string, string[]>> errors)
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (key, messages) in errors)
        {
            var normalized = Normalize(key);
            result[normalized] = result.TryGetValue(normalized, out var existing) ? [.. existing, .. messages] : messages;
        }
        return result;
    }

    /// <summary>
    /// Normalizes one key. Member segments use the naming policy; numeric indexers are kept verbatim;
    /// other indexers are dictionary keys and use the dictionary key policy. Quoted indexers
    /// (<c>$['first name']</c>) are JSON property names from System.Text.Json and are written as member
    /// segments without renaming. A leading <c>$</c> JSON path root is removed, and an empty indexer
    /// (<c>Items[]</c>) is kept in <see cref="ValidationErrorKeyFormat.Brackets"/> and dropped in
    /// <see cref="ValidationErrorKeyFormat.Dots"/>.
    /// </summary>
    public string Normalize(string key)
    {
        if (Format == ValidationErrorKeyFormat.Preserve) return key;

        var builder = new StringBuilder(key.Length);
        var first = true;
        foreach (var (text, kind) in Parse(key))
        {
            switch (kind)
            {
                case SegmentKind.Member:
                    if (!first) builder.Append('.');
                    builder.Append(text.Length == 0 ? text : PropertyNamingPolicy?.ConvertName(text) ?? text);
                    break;
                case SegmentKind.Quoted:
                    if (!first) builder.Append('.');
                    builder.Append(text);
                    break;
                case SegmentKind.Indexer:
                    var value = IsIndex(text) ? text : DictionaryKeyPolicy?.ConvertName(text) ?? text;
                    if (Format == ValidationErrorKeyFormat.Dots)
                    {
                        if (!first) builder.Append('.');
                        builder.Append(value);
                    }
                    else builder.Append('[').Append(value).Append(']');
                    break;
                case SegmentKind.EmptyIndexer:
                    if (Format == ValidationErrorKeyFormat.Dots) continue;
                    builder.Append("[]");
                    break;
            }
            first = false;
        }
        return builder.ToString();
    }

    /// <summary>
    /// Returns the form used to compare a key with a <c>Precognition-Validate-Only</c> field: indexers become
    /// plain segments, the <c>$</c> root and indexer quotes are removed, and every segment, indexers included,
    /// goes through the member naming policy. Segments are compared ignoring case. Applying the same function
    /// to a normalized key and to the field the client echoes back guarantees that every returned key selects
    /// itself, whichever bracket or dot spelling and casing the client uses.
    /// </summary>
    public string[] MatchSegments(string key)
    {
        var policy = PropertyNamingPolicy;
        return [.. Parse(key).Where(segment => segment.Kind != SegmentKind.EmptyIndexer)
            .Select(segment => segment.Text.Length == 0 ? segment.Text : policy?.ConvertName(segment.Text) ?? segment.Text)];
    }

    private static bool IsIndex(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);

    private enum SegmentKind { Member, Indexer, Quoted, EmptyIndexer }

    private static List<(string Text, SegmentKind Kind)> Parse(string key)
    {
        var segments = new List<(string Text, SegmentKind Kind)>();
        var start = 0;
        if (key == "$") return [(string.Empty, SegmentKind.Member)];
        if (key.StartsWith("$.", StringComparison.Ordinal)) start = 2;
        else if (key.StartsWith("$[", StringComparison.Ordinal)) start = 1;

        var name = new StringBuilder();
        var nameOpen = true;
        for (var i = start; i < key.Length; i++)
        {
            var c = key[i];
            if (c == '.')
            {
                if (nameOpen) segments.Add((name.ToString(), SegmentKind.Member));
                name.Clear();
                nameOpen = true;
            }
            else if (c == '[' && FindIndexerEnd(key, i) is var (close, end, quoted) && close > 0)
            {
                if (nameOpen && name.Length > 0) segments.Add((name.ToString(), SegmentKind.Member));
                name.Clear();
                var text = quoted ? key[(i + 2)..close] : key[(i + 1)..close];
                segments.Add((text, quoted ? SegmentKind.Quoted : text.Length == 0 ? SegmentKind.EmptyIndexer : SegmentKind.Indexer));
                nameOpen = false;
                i = end;
            }
            else
            {
                nameOpen = true;
                name.Append(c);
            }
        }
        if (nameOpen) segments.Add((name.ToString(), SegmentKind.Member));
        return segments;
    }

    /// <summary>
    /// Finds the end of the indexer opened at <paramref name="open"/>. A quoted indexer (<c>['a]b']</c>) runs
    /// to the closing <c>']</c>, so brackets inside the quotes do not end it. Returns a close of -1 when the
    /// indexer is not terminated.
    /// </summary>
    private static (int Close, int End, bool Quoted) FindIndexerEnd(string key, int open)
    {
        if (open + 1 < key.Length && key[open + 1] == '\'')
        {
            var quote = key.IndexOf("']", open + 2, StringComparison.Ordinal);
            if (quote > 0) return (quote, quote + 1, true);
        }
        var close = key.IndexOf(']', open + 1);
        return (close, close, false);
    }
}
