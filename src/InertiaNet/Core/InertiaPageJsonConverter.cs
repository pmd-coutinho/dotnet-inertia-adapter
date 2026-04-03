using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InertiaNet.Core;

internal sealed class InertiaPageJsonConverter : JsonConverter<InertiaPage>
{
    public override InertiaPage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<InertiaPage>(ref reader, InertiaJsonOptions.Default)
            ?? throw new JsonException("Failed to deserialize Inertia page payload.");

    public override void Write(Utf8JsonWriter writer, InertiaPage value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteString("component", value.Component);
        WriteDictionary(writer, "props", value.Props, options);
        writer.WriteString("url", value.Url);

        WriteOptional(writer, "version", value.Version, options);
        WriteOptional(writer, "sharedProps", value.SharedProps, options);
        WriteOptional(writer, "mergeProps", value.MergeProps, options);
        WriteOptional(writer, "prependProps", value.PrependProps, options);
        WriteOptional(writer, "deepMergeProps", value.DeepMergeProps, options);
        WriteOptionalDictionary(writer, "matchPropsOn", value.MatchPropsOn, options);
        WriteOptionalDictionary(writer, "deferredProps", value.DeferredProps, options);
        WriteOptionalDictionary(writer, "scrollProps", value.ScrollProps, options);
        WriteOptionalDictionary(writer, "onceProps", value.OnceProps, options);
        WriteOptionalDictionary(writer, "flash", value.Flash, options);
        WriteOptional(writer, "clearHistory", value.ClearHistory, options);
        WriteOptional(writer, "encryptHistory", value.EncryptHistory, options);
        WriteOptional(writer, "preserveFragment", value.PreserveFragment, options);

        writer.WriteEndObject();
    }

    private static void WriteDictionary(
        Utf8JsonWriter writer,
        string propertyName,
        IReadOnlyDictionary<string, object?> value,
        JsonSerializerOptions options)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();

        foreach (var (key, entryValue) in value)
        {
            writer.WritePropertyName(key);
            JsonSerializer.Serialize(writer, entryValue, options);
        }

        writer.WriteEndObject();
    }

    private static void WriteOptionalDictionary<TValue>(
        Utf8JsonWriter writer,
        string propertyName,
        IReadOnlyDictionary<string, TValue>? value,
        JsonSerializerOptions options)
    {
        if (value is null)
            return;

        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();

        foreach (var (key, entryValue) in value)
        {
            writer.WritePropertyName(key);
            JsonSerializer.Serialize(writer, entryValue, options);
        }

        writer.WriteEndObject();
    }

    private static void WriteOptional<T>(
        Utf8JsonWriter writer,
        string propertyName,
        T value,
        JsonSerializerOptions options)
    {
        if (value is null)
            return;

        writer.WritePropertyName(propertyName);
        JsonSerializer.Serialize(writer, value, options);
    }
}

/// <summary>Shared JSON serializer options for Inertia page object serialization.</summary>
internal static class InertiaJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static JsonSerializerOptions GetOptions(InertiaOptions? options)
        => CreateOptions(options?.JsonSerializerOptions, encoder: null);

    public static JsonSerializerOptions GetTagHelperOptions(InertiaOptions? options)
        => CreateOptions(options?.JsonSerializerOptions, JavaScriptEncoder.UnsafeRelaxedJsonEscaping);

    private static JsonSerializerOptions CreateOptions(
        JsonSerializerOptions? userOptions,
        JavaScriptEncoder? encoder)
    {
        var resolved = userOptions is null
            ? new JsonSerializerOptions(Default)
            : new JsonSerializerOptions(userOptions);

        if (encoder is not null)
            resolved.Encoder = encoder;

        if (!resolved.Converters.Any(c => c is InertiaPageJsonConverter))
            resolved.Converters.Insert(0, new InertiaPageJsonConverter());

        return resolved;
    }
}
