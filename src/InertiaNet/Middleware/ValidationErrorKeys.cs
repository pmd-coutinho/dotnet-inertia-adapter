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
    /// Resolves key naming from the configured options. The member naming policy follows
    /// <see cref="InertiaOptions.JsonSerializerOptions"/> and falls back to camelCase, the adapter's default
    /// prop naming. Non-numeric indexers use the configured dictionary key policy (none by default).
    /// </summary>
    public static ValidationErrorKeys From(InertiaOptions? options, ValidationErrorKeyFormat? format = null)
        => new(options?.JsonSerializerOptions?.PropertyNamingPolicy ?? JsonNamingPolicy.CamelCase,
            options?.JsonSerializerOptions?.DictionaryKeyPolicy,
            format ?? options?.ValidationErrorKeyFormat ?? ValidationErrorKeyFormat.Brackets);

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
    /// other indexers are dictionary keys and use the dictionary key policy. A leading <c>$</c> JSON path
    /// root from System.Text.Json input formatter errors is removed.
    /// </summary>
    public string Normalize(string key)
    {
        if (Format == ValidationErrorKeyFormat.Preserve) return key;

        var builder = new StringBuilder(key.Length);
        var first = true;
        foreach (var (text, indexer) in Parse(key))
        {
            if (!indexer)
            {
                if (!first) builder.Append('.');
                builder.Append(text.Length == 0 ? text : PropertyNamingPolicy?.ConvertName(text) ?? text);
            }
            else
            {
                // A quoted indexer is a JSON property name from a System.Text.Json path and is kept as sent.
                var quoted = text.Length >= 2 && text[0] == '\'' && text[^1] == '\'';
                var value = IsIndex(text) || quoted ? text : DictionaryKeyPolicy?.ConvertName(text) ?? text;
                if (Format == ValidationErrorKeyFormat.Dots)
                {
                    if (!first) builder.Append('.');
                    builder.Append(quoted ? value[1..^1] : value);
                }
                else builder.Append('[').Append(value).Append(']');
            }
            first = false;
        }
        return builder.ToString();
    }

    private static bool IsIndex(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);

    private static List<(string Text, bool Indexer)> Parse(string key)
    {
        var segments = new List<(string Text, bool Indexer)>();
        var start = 0;
        if (key == "$") return [(string.Empty, false)];
        if (key.StartsWith("$.", StringComparison.Ordinal)) start = 2;
        else if (key.StartsWith("$[", StringComparison.Ordinal)) start = 1;

        var name = new StringBuilder();
        var nameOpen = true;
        for (var i = start; i < key.Length; i++)
        {
            var c = key[i];
            if (c == '.')
            {
                if (nameOpen) segments.Add((name.ToString(), false));
                name.Clear();
                nameOpen = true;
            }
            else if (c == '[' && key.IndexOf(']', i + 1) is var close and > 0)
            {
                if (nameOpen && name.Length > 0) segments.Add((name.ToString(), false));
                name.Clear();
                segments.Add((key[(i + 1)..close], true));
                nameOpen = false;
                i = close;
            }
            else
            {
                nameOpen = true;
                name.Append(c);
            }
        }
        if (nameOpen) segments.Add((name.ToString(), false));
        return segments;
    }
}
