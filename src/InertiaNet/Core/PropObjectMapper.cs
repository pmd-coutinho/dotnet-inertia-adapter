using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace InertiaNet.Core;

/// <summary>Reads the serializer's object contract without evaluating prop wrappers.</summary>
internal static class PropObjectMapper
{
    public static Dictionary<string, object?> ToDictionary(object? value, JsonSerializerOptions options)
    {
        if (value is null) return [];
        if (value is PropObjectDictionary mapped) return PropObjectDictionary.Copy(mapped);
        options.MakeReadOnly(populateMissingResolver: true);
        var contract = options.GetTypeInfo(value.GetType());
        if (contract.Kind == JsonTypeInfoKind.Dictionary && TryStringDictionary(value, out var entries)) return entries;

        if (contract.Kind != JsonTypeInfoKind.Object)
        {
            // A custom converter owns the entire representation. Its result must be
            // an object; ordinary object contracts below retain their lazy values.
            var json = JsonSerializer.SerializeToElement(value, contract);
            if (json.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Inertia page props must serialize to a JSON object.", nameof(value));
            var converted = new PropObjectDictionary();
            foreach (var property in json.EnumerateObject()) converted.Set(property.Name, property.Value.Clone(), true);
            return converted;
        }

        var result = new PropObjectDictionary();
        foreach (var property in contract.Properties)
        {
            if (property.Get is null) continue;
            var propertyValue = property.Get(value);
            if (property.ShouldSerialize is not null && !property.ShouldSerialize(value, propertyValue)) continue;

            var ignore = property.AttributeProvider?.GetCustomAttributes(typeof(JsonIgnoreAttribute), true)
                .OfType<JsonIgnoreAttribute>().FirstOrDefault()?.Condition ?? options.DefaultIgnoreCondition;
            if (ignore == JsonIgnoreCondition.Always
                || (ignore == JsonIgnoreCondition.WhenWritingNull && propertyValue is null)
                || (ignore == JsonIgnoreCondition.WhenWritingDefault && IsDefault(propertyValue, property.PropertyType)))
                continue;

            if (options.IgnoreReadOnlyProperties && property.Set is null && property.ShouldSerialize is null
                && property.AttributeProvider is PropertyInfo
                && !(property.PropertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(property.PropertyType)))
                continue;
            if (options.IgnoreReadOnlyFields && property.ShouldSerialize is null
                && property.AttributeProvider is FieldInfo { IsInitOnly: true }) continue;

            var numberHandling = property.NumberHandling ?? contract.NumberHandling;
            if (!property.IsExtensionData && (property.CustomConverter is not null || numberHandling is not null))
            {
                var propertyOptions = new JsonSerializerOptions(options);
                if (property.CustomConverter is not null) propertyOptions.Converters.Insert(0, property.CustomConverter);
                if (numberHandling is { } handling) propertyOptions.NumberHandling = handling;
                propertyValue = JsonSerializer.SerializeToElement(propertyValue, property.PropertyType, propertyOptions);
            }

            if (property.IsExtensionData && propertyValue is IDictionary extensionData)
            {
                foreach (DictionaryEntry entry in extensionData)
                    result.Set((string)entry.Key, entry.Value, true);
            }
            else
            {
                result.Set(property.Name, propertyValue, true);
            }
        }
        return result;
    }

    internal static bool TryStringDictionary(object value, out Dictionary<string, object?> result)
    {
        result = [];
        if (value is IDictionary dictionary && dictionary.Keys.Cast<object>().All(key => key is string))
        {
            foreach (DictionaryEntry entry in dictionary) result[(string)entry.Key] = entry.Value;
            return true;
        }
        if (value is not IEnumerable sequence || !value.GetType().GetInterfaces().Any(type => type.IsGenericType
            && type.GetGenericTypeDefinition() is var definition && (definition == typeof(IReadOnlyDictionary<,>) || definition == typeof(IDictionary<,>))
            && type.GetGenericArguments()[0] == typeof(string))) return false;
        foreach (var entry in sequence)
        {
            var type = entry!.GetType();
            result[(string)type.GetProperty("Key")!.GetValue(entry)!] = type.GetProperty("Value")!.GetValue(entry);
        }
        return true;
    }

    private static bool IsDefault(object? value, Type type)
        => value is null || (type.IsValueType && Nullable.GetUnderlyingType(type) is null
            && Equals(value, System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type)));
}
