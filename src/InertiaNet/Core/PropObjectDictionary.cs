using System.Text.Json;
using System.Text.Json.Serialization;

namespace InertiaNet.Core;

/// <summary>Tracks serializer-owned names separately from API dot-path keys.</summary>
[JsonConverter(typeof(PropObjectDictionaryConverter))]
internal sealed class PropObjectDictionary : Dictionary<string, object?>
{
    public HashSet<string> LiteralKeys { get; } = new(StringComparer.Ordinal);
    public void Set(string key, object? value, bool literal)
    {
        this[key] = value;
        if (literal) LiteralKeys.Add(key); else LiteralKeys.Remove(key);
    }
    public static Dictionary<string, object?> Copy(IEnumerable<KeyValuePair<string, object?>> values)
    {
        if (values is not PropObjectDictionary original) return new(values);
        var copy = new PropObjectDictionary();
        foreach (var (key, value) in original) copy.Set(key, value, original.LiteralKeys.Contains(key));
        return copy;
    }
}

internal sealed class PropObjectDictionaryConverter : JsonConverter<PropObjectDictionary>
{
    public PropObjectDictionaryConverter() { }
    public override PropObjectDictionary Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, object?>>(ref reader, options) ?? [];
        var result = new PropObjectDictionary();
        foreach (var (key, value) in values) result.Set(key, value, true);
        return result;
    }
    public override void Write(Utf8JsonWriter writer, PropObjectDictionary value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (key, entry) in value)
        {
            writer.WritePropertyName(value.LiteralKeys.Contains(key) ? key : options.DictionaryKeyPolicy?.ConvertName(key) ?? key);
            JsonSerializer.Serialize(writer, entry, options);
        }
        writer.WriteEndObject();
    }
}
