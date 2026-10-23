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
        var protocolOptions = options;
        if (options.Converters.OfType<LargeIntegerConverterFactory>().Any(converter => converter.Policy != LargeIntegerSerialization.Number))
        {
            protocolOptions = new JsonSerializerOptions(options);
            for (var index = protocolOptions.Converters.Count - 1; index >= 0; index--)
                if (protocolOptions.Converters[index] is LargeIntegerConverterFactory) protocolOptions.Converters.RemoveAt(index);
        }
        writer.WriteStartObject();

        writer.WriteString("component", value.Component);
        WriteDictionary(writer, "props", value.Props, options);
        writer.WriteString("url", value.Url);

        WriteOptional(writer, "version", value.Version, protocolOptions);
        WriteOptional(writer, "sharedProps", value.SharedProps, protocolOptions);
        WriteOptional(writer, "mergeProps", value.MergeProps, protocolOptions);
        WriteOptional(writer, "prependProps", value.PrependProps, protocolOptions);
        WriteOptional(writer, "deepMergeProps", value.DeepMergeProps, protocolOptions);
        WriteOptional(writer, "matchPropsOn", value.MatchPropsOn, protocolOptions);
        WriteOptionalDictionary(writer, "deferredProps", value.DeferredProps, protocolOptions);
        WriteOptional(writer, "rescuedProps", value.RescuedProps, protocolOptions);
        WriteOptionalDictionary(writer, "scrollProps", value.ScrollProps, protocolOptions);
        WriteOptionalDictionary(writer, "onceProps", value.OnceProps, protocolOptions);
        WriteOptionalDictionary(writer, "flash", value.Flash, options);
        WriteOptional(writer, "clearHistory", value.ClearHistory, protocolOptions);
        WriteOptional(writer, "encryptHistory", value.EncryptHistory, protocolOptions);
        WriteOptional(writer, "preserveFragment", value.PreserveFragment, protocolOptions);
        WriteOptional(writer, "preserveBigIntegers", value.PreserveBigIntegers, protocolOptions);

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
        => CreateOptions(options, encoder: null);

    public static JsonSerializerOptions GetTagHelperOptions(InertiaOptions? options)
        => CreateOptions(options, JavaScriptEncoder.Default);

    private static JsonSerializerOptions CreateOptions(
        InertiaOptions? options,
        JavaScriptEncoder? encoder)
    {
        var userOptions = options?.JsonSerializerOptions;
        var resolved = userOptions is null
            ? new JsonSerializerOptions(Default)
            : new JsonSerializerOptions(userOptions);

        if (encoder is not null)
            resolved.Encoder = encoder;

        if (!resolved.Converters.Any(c => c is InertiaPageJsonConverter))
            resolved.Converters.Insert(0, new InertiaPageJsonConverter());

        // Explicit application converters retain precedence over this numeric policy.
        resolved.Converters.Add(new LargeIntegerConverterFactory(options?.LargeIntegerSerialization ?? LargeIntegerSerialization.Number));

        return resolved;
    }
}
