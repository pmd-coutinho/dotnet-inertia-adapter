using InertiaNet.Core;
using System.Text.Json;
using System.Globalization;
using System.Numerics;

namespace InertiaNet.Testing;

/// <summary>
/// Fluent assertion wrapper for <see cref="InertiaPage"/> responses.
/// Enables readable assertions in integration tests.
/// </summary>
public sealed class AssertableInertiaPage
{
    private readonly InertiaPage _page;
    private static readonly JsonSerializerOptions AssertionJsonOptions = InertiaJsonOptions.GetOptions(null);

    public AssertableInertiaPage(InertiaPage page) => _page = page;

    /// <summary>Returns a new assertion wrapper from an Inertia JSON response.</summary>
    public static AssertableInertiaPage FromJson(string json)
    {
        var page = JsonSerializer.Deserialize<InertiaPage>(json, InertiaJsonOptions.Default)
            ?? throw new InvalidOperationException("Failed to deserialize Inertia page JSON.");
        return new AssertableInertiaPage(page);
    }

    /// <summary>Asserts the component name matches the expected value.</summary>
    public AssertableInertiaPage HasComponent(string expected)
    {
        if (_page.Component != expected)
            throw new AssertionException($"Expected component '{expected}', got '{_page.Component}'.");
        return this;
    }

    /// <summary>Asserts a prop exists (with optional value check).</summary>
    public AssertableInertiaPage HasProp(string key, object? expectedValue = null)
    {
        if (!TryGetValueAtPath(_page.Props, key, out var actualValue))
            throw new AssertionException($"Prop '{key}' does not exist.");

        if (expectedValue is not null && !JsonEquals(actualValue, ToJsonElement(expectedValue)))
            throw new AssertionException($"Prop '{key}' expected value '{expectedValue}', got '{actualValue}'.");

        return this;
    }

    /// <summary>Asserts an exact integer value across numeric, decimal-string and opted-in marker transports.</summary>
    public AssertableInertiaPage HasBigIntegerProp(string key, BigInteger expected)
    {
        AssertBigInteger(_page.Props, key, expected);
        return this;
    }

    /// <summary>Asserts an exact integer in flash data without converting it to a floating-point number.</summary>
    public AssertableInertiaPage HasBigIntegerFlash(string key, BigInteger expected)
    {
        if (_page.Flash is null) throw new AssertionException($"Flash data '{key}' not found.");
        AssertBigInteger(_page.Flash, key, expected);
        return this;
    }

    /// <summary>Asserts that the page opts into client BigInt revival.</summary>
    public AssertableInertiaPage HasPreserveBigIntegers()
    {
        if (_page.PreserveBigIntegers != true) throw new AssertionException("Expected preserveBigIntegers to be true.");
        return this;
    }

    private void AssertBigInteger(IReadOnlyDictionary<string, object?> source, string key, BigInteger expected)
    {
        if (!TryGetValueAtPath(source, key, out var value)) throw new AssertionException($"Integer '{key}' not found.");
        if (value.ValueKind == JsonValueKind.Object && _page.PreserveBigIntegers == true
            && value.TryGetProperty("$bigint", out var marker)) value = marker;
        var digits = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
        if (!BigInteger.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var actual) || actual != expected)
            throw new AssertionException($"Integer '{key}' expected '{expected}', got '{value}'.");
    }

    /// <summary>Asserts a prop does NOT exist.</summary>
    public AssertableInertiaPage DoesNotHaveProp(string key)
    {
        if (TryGetValueAtPath(_page.Props, key, out _))
            throw new AssertionException($"Prop '{key}' should not exist.");

        return this;
    }

    /// <summary>Asserts a prop collection contains the expected number of items.</summary>
    public AssertableInertiaPage HasPropCount(string key, int expectedCount)
    {
        if (!TryGetValueAtPath(_page.Props, key, out var actualValue))
            throw new AssertionException($"Prop '{key}' does not exist.");

        var actualCount = actualValue.ValueKind switch
        {
            JsonValueKind.Array => actualValue.GetArrayLength(),
            JsonValueKind.Object => actualValue.EnumerateObject().Count(),
            _ => throw new AssertionException($"Prop '{key}' is not a collection."),
        };

        if (actualCount != expectedCount)
            throw new AssertionException($"Prop '{key}' expected count {expectedCount}, got {actualCount}.");

        return this;
    }

    /// <summary>Asserts a shared prop exists (from <c>Share()</c>).</summary>
    public AssertableInertiaPage HasSharedProp(string key)
    {
        if (_page.SharedProps is null || !_page.SharedProps.Contains(key))
            throw new AssertionException($"Shared prop '{key}' not found.");
        return this;
    }

    /// <summary>Asserts the URL matches the expected value.</summary>
    public AssertableInertiaPage HasUrl(string expected)
    {
        if (_page.Url != expected)
            throw new AssertionException($"Expected URL '{expected}', got '{_page.Url}'.");
        return this;
    }

    /// <summary>Asserts the version string matches.</summary>
    public AssertableInertiaPage HasVersion(string expected)
    {
        if (_page.Version != expected)
            throw new AssertionException($"Expected version '{expected}', got '{_page.Version}'.");
        return this;
    }

    /// <summary>Asserts no version is set.</summary>
    public AssertableInertiaPage HasNoVersion()
    {
        if (_page.Version is not null)
            throw new AssertionException($"Expected no version, got '{_page.Version}'.");
        return this;
    }

    /// <summary>Asserts the page has deferred props.</summary>
    public AssertableInertiaPage HasDeferredProps()
    {
        if (_page.DeferredProps is null || _page.DeferredProps.Count == 0)
            throw new AssertionException("Expected deferred props, but none were found.");
        return this;
    }

    /// <summary>Asserts the page has the specified deferred prop group.</summary>
    public AssertableInertiaPage HasDeferredPropGroup(string groupName)
    {
        if (_page.DeferredProps is null || !_page.DeferredProps.ContainsKey(groupName))
            throw new AssertionException($"Deferred prop group '{groupName}' not found.");
        return this;
    }

    /// <summary>Asserts that a prop belongs to the specified deferred group.</summary>
    public AssertableInertiaPage HasDeferredProp(string path, string group = "default")
    {
        HasDeferredPropGroup(group);
        return Contains(_page.DeferredProps![group], path, $"deferred group '{group}'");
    }

    /// <summary>Asserts that a prop will be appended when merging.</summary>
    public AssertableInertiaPage HasMergeProp(string path) => Contains(_page.MergeProps, path, "mergeProps");

    /// <summary>Asserts that a prop will be prepended when merging.</summary>
    public AssertableInertiaPage HasPrependProp(string path) => Contains(_page.PrependProps, path, "prependProps");

    /// <summary>Asserts that a prop will be deep merged.</summary>
    public AssertableInertiaPage HasDeepMergeProp(string path) => Contains(_page.DeepMergeProps, path, "deepMergeProps");

    /// <summary>Asserts that a full matching path is present.</summary>
    public AssertableInertiaPage HasMatchingProp(string path) => Contains(_page.MatchPropsOn, path, "matchPropsOn");

    /// <summary>Asserts that a deferred prop failed with rescue enabled.</summary>
    public AssertableInertiaPage HasRescuedProp(string path) => Contains(_page.RescuedProps, path, "rescuedProps");

    /// <summary>Asserts once metadata exists; a supplied non-null value must match its complete JSON shape.</summary>
    public AssertableInertiaPage HasOnceProp(string key, object? expectedMetadata = null)
        => Metadata(_page.OnceProps, "onceProps", key, expectedMetadata);

    /// <summary>Asserts scroll metadata exists; a supplied non-null value must match its complete JSON shape.</summary>
    public AssertableInertiaPage HasScrollProp(string path, object? expectedMetadata = null)
        => Metadata(_page.ScrollProps, "scrollProps", path, expectedMetadata);

    /// <summary>Asserts a prop exists with an explicit JSON null value.</summary>
    public AssertableInertiaPage HasNullProp(string path)
    {
        if (!TryGetValueAtPath(_page.Props, path, out var value) || value.ValueKind != JsonValueKind.Null)
            throw new AssertionException($"Expected prop '{path}' to be null.");
        return this;
    }

    private AssertableInertiaPage Contains(IReadOnlyList<string>? paths, string path, string kind)
    {
        if (paths?.Contains(path) != true) throw new AssertionException($"Expected '{path}' in {kind}.");
        return this;
    }

    private AssertableInertiaPage Metadata(IReadOnlyDictionary<string, object?>? metadata, string kind, string key, object? expected)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var actual))
            throw new AssertionException($"Expected '{key}' in {kind}.");
        if (expected is not null && !JsonEquals(ToJsonElement(actual), JsonSerializer.SerializeToElement(expected,
            new JsonSerializerOptions(InertiaJsonOptions.Default) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never })))
            throw new AssertionException($"Metadata for '{key}' in {kind} did not match the expected JSON.");
        return this;
    }

    /// <summary>Asserts the clearHistory flag is set.</summary>
    public AssertableInertiaPage HasClearHistory()
    {
        if (_page.ClearHistory != true)
            throw new AssertionException("Expected clearHistory to be true.");
        return this;
    }

    /// <summary>Asserts the encryptHistory flag is set.</summary>
    public AssertableInertiaPage HasEncryptHistory()
    {
        if (_page.EncryptHistory != true)
            throw new AssertionException("Expected encryptHistory to be true.");
        return this;
    }

    /// <summary>Asserts the preserveFragment flag is set.</summary>
    public AssertableInertiaPage HasPreserveFragment()
    {
        if (_page.PreserveFragment != true)
            throw new AssertionException("Expected preserveFragment to be true.");
        return this;
    }

    /// <summary>Asserts flash data exists with optional value check.</summary>
    public AssertableInertiaPage HasFlash(string key, object? expectedValue = null)
    {
        if (_page.Flash is null || !TryGetValueAtPath(_page.Flash, key, out var actualValue))
            throw new AssertionException($"Flash data '{key}' not found.");

        if (expectedValue is not null && !JsonEquals(actualValue, ToJsonElement(expectedValue)))
            throw new AssertionException($"Flash '{key}' expected value '{expectedValue}', got '{actualValue}'.");

        return this;
    }

    /// <summary>Asserts flash data does NOT exist.</summary>
    public AssertableInertiaPage DoesNotHaveFlash(string key)
    {
        if (_page.Flash is not null && TryGetValueAtPath(_page.Flash, key, out _))
            throw new AssertionException($"Flash data '{key}' should not exist.");

        return this;
    }

    /// <summary>Returns the underlying <see cref="InertiaPage"/>.</summary>
    public InertiaPage Page => _page;

    private static bool TryGetValueAtPath(IReadOnlyDictionary<string, object?> source, string path, out JsonElement value)
    {
        var current = ToJsonElement(source);

        foreach (var segment in ParsePath(path))
        {
            if (segment.PropertyName is not null)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment.PropertyName, out current))
                {
                    value = default;
                    return false;
                }
            }

            if (segment.ArrayIndex is not null)
            {
                if (current.ValueKind != JsonValueKind.Array || current.GetArrayLength() <= segment.ArrayIndex.Value)
                {
                    value = default;
                    return false;
                }

                current = current[segment.ArrayIndex.Value];
            }
        }

        value = current;
        return true;
    }

    private static JsonElement ToJsonElement(object? value)
        => value is JsonElement element
            ? element.Clone()
            : JsonSerializer.SerializeToElement(value, AssertionJsonOptions);

    private static bool JsonEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
            return false;

        return left.ValueKind switch
        {
            JsonValueKind.Object => JsonObjectEquals(left, right),
            JsonValueKind.Array => JsonArrayEquals(left, right),
            JsonValueKind.String => left.GetString() == right.GetString(),
            JsonValueKind.Number => left.GetRawText() == right.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => true,
            JsonValueKind.Null => true,
            JsonValueKind.Undefined => true,
            _ => left.GetRawText() == right.GetRawText(),
        };
    }

    private static bool JsonObjectEquals(JsonElement left, JsonElement right)
    {
        var leftProperties = left.EnumerateObject().ToArray();
        var rightProperties = right.EnumerateObject().ToDictionary(property => property.Name, property => property.Value);

        if (leftProperties.Length != rightProperties.Count)
            return false;

        foreach (var property in leftProperties)
        {
            if (!rightProperties.TryGetValue(property.Name, out var rightValue))
                return false;

            if (!JsonEquals(property.Value, rightValue))
                return false;
        }

        return true;
    }

    private static bool JsonArrayEquals(JsonElement left, JsonElement right)
    {
        if (left.GetArrayLength() != right.GetArrayLength())
            return false;

        for (var index = 0; index < left.GetArrayLength(); index++)
        {
            if (!JsonEquals(left[index], right[index]))
                return false;
        }

        return true;
    }

    private static IEnumerable<PathSegment> ParsePath(string path)
    {
        var index = 0;

        while (index < path.Length)
        {
            if (path[index] == '.')
            {
                index++;
                continue;
            }

            string? propertyName = null;
            int? arrayIndex = null;

            if (path[index] != '[')
            {
                var propertyStart = index;
                while (index < path.Length && path[index] is not '.' and not '[')
                    index++;

                propertyName = path[propertyStart..index];
            }

            while (index < path.Length && path[index] == '[')
            {
                index++;
                var indexStart = index;

                while (index < path.Length && path[index] != ']')
                    index++;

                if (index >= path.Length || !int.TryParse(path[indexStart..index], out var parsedIndex) || parsedIndex < 0)
                    throw new AssertionException($"Invalid path segment in '{path}'.");

                arrayIndex = parsedIndex;
                index++;

                yield return new PathSegment(propertyName, arrayIndex);
                propertyName = null;
                arrayIndex = null;
            }

            if (propertyName is not null)
                yield return new PathSegment(propertyName, null);
        }
    }

    private readonly record struct PathSegment(string? PropertyName, int? ArrayIndex);
}

/// <summary>Thrown when an assertion fails.</summary>
public sealed class AssertionException : Exception
{
    public AssertionException(string message) : base(message) { }
}
