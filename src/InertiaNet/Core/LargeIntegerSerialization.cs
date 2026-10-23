using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InertiaNet.Core;

/// <summary>Controls the wire representation of Int64, UInt64 and BigInteger values.</summary>
public enum LargeIntegerSerialization
{
    /// <summary>JSON numbers. JavaScript may round values outside its safe integer range.</summary>
    Number,
    /// <summary>Decimal strings, compatible with older Inertia clients.</summary>
    String,
    /// <summary>Safe JSON numbers and Inertia $bigint markers for larger values. Requires a client with BigInt support.</summary>
    BigInt,
}

internal sealed class LargeIntegerConverterFactory(LargeIntegerSerialization policy) : JsonConverterFactory
{
    internal LargeIntegerSerialization Policy => policy;
    public override bool CanConvert(Type type)
        => type == typeof(BigInteger) || policy != LargeIntegerSerialization.Number
            && (type == typeof(long) || type == typeof(ulong)
                || type.IsEnum && (Enum.GetUnderlyingType(type) == typeof(long) || Enum.GetUnderlyingType(type) == typeof(ulong)));

    public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options)
        => type.IsEnum ? (JsonConverter)Activator.CreateInstance(typeof(LargeEnumConverter<>).MakeGenericType(type), policy)!
            : type == typeof(long) ? new IntegerConverter<long>(policy)
            : type == typeof(ulong) ? new IntegerConverter<ulong>(policy)
            : new IntegerConverter<BigInteger>(policy);

    private static void WriteInteger(Utf8JsonWriter writer, BigInteger value, LargeIntegerSerialization policy, bool asString)
    {
        var digits = value.ToString(CultureInfo.InvariantCulture);
        if (asString || policy == LargeIntegerSerialization.String) writer.WriteStringValue(digits);
        else if (policy == LargeIntegerSerialization.BigInt && BigInteger.Abs(value) > 9007199254740991L)
        {
            writer.WriteStartObject();
            writer.WriteString("$bigint", digits);
            writer.WriteEndObject();
        }
        else writer.WriteRawValue(digits, skipInputValidation: false);
    }

    private sealed class IntegerConverter<T>(LargeIntegerSerialization policy) : JsonConverter<T> where T : struct, IBinaryInteger<T>
    {
        public override T Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            try
            {
                if (reader.TokenType == JsonTokenType.String && (policy == LargeIntegerSerialization.String
                    || options.NumberHandling.HasFlag(JsonNumberHandling.AllowReadingFromString)))
                    return T.Parse(reader.GetString()!, CultureInfo.InvariantCulture);
                using var json = JsonDocument.ParseValue(ref reader);
                var value = json.RootElement;
                if (policy == LargeIntegerSerialization.BigInt && value.ValueKind == JsonValueKind.Object
                    && value.TryGetProperty("$bigint", out var marker) && marker.ValueKind == JsonValueKind.String)
                    return T.Parse(marker.GetString()!, CultureInfo.InvariantCulture);
                if (value.ValueKind != JsonValueKind.Number) throw new JsonException("Expected an integer.");
                return T.Parse(value.GetRawText(), CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            { throw new JsonException("Invalid or out-of-range integer.", ex); }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            => WriteInteger(writer, BigInteger.CreateChecked(value), policy, options.NumberHandling.HasFlag(JsonNumberHandling.WriteAsString));

        public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            try { return T.Parse(reader.GetString()!, CultureInfo.InvariantCulture); }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            { throw new JsonException("Invalid integer dictionary key.", ex); }
        }

        public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            => writer.WritePropertyName(value.ToString(null, CultureInfo.InvariantCulture));
    }

    private sealed class LargeEnumConverter<T>(LargeIntegerSerialization policy) : JsonConverter<T> where T : struct, Enum
    {
        private static readonly bool Unsigned = Enum.GetUnderlyingType(typeof(T)) == typeof(ulong);
        public override T Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (Unsigned) return (T)Enum.ToObject(type, new IntegerConverter<ulong>(policy).Read(ref reader, typeof(ulong), options));
            return (T)Enum.ToObject(type, new IntegerConverter<long>(policy).Read(ref reader, typeof(long), options));
        }
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            => WriteInteger(writer, Unsigned ? new BigInteger(Convert.ToUInt64(value, CultureInfo.InvariantCulture))
                : new BigInteger(Convert.ToInt64(value, CultureInfo.InvariantCulture)), policy, false);
        public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => Enum.Parse<T>(reader.GetString()!);
        public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            => writer.WritePropertyName(value.ToString());
    }
}
