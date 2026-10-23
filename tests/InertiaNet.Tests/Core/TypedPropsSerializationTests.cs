using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Props;
using InertiaNet.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaNet.Tests.Core;

public class TypedPropsSerializationTests
{
    [Theory]
    [InlineData("generic")]
    [InlineData("component")]
    [InlineData("minimal")]
    public async Task TypedRenderingSurfaces_UseTheSameSerializationContract(string surface)
    {
        var ctx = HttpContextHelper.CreateInertia(configureServices: services => services.AddInertia());
        var service = ctx.RequestServices.GetRequiredService<IInertiaService>();
        var component = new InertiaComponent<ProfileProps>("Profile");
        IResult result = surface switch
        {
            "generic" => service.Render<ProfileProps>("Profile", new ProfileProps()),
            "component" => service.Render(component, new ProfileProps()),
            _ => InertiaResults.Inertia(component, new ProfileProps()),
        };
        await result.ExecuteAsync(ctx);
        using var doc = JsonDocument.Parse(await ctx.ReadResponseBodyAsync());
        doc.RootElement.GetProperty("component").GetString().Should().Be("Profile");
        var props = doc.RootElement.GetProperty("props");
        props.GetProperty("display_name").GetString().Should().Be("Ana");
        props.TryGetProperty("secret", out _).Should().BeFalse();
    }

    [Fact]
    public async Task TypedProps_HonorJsonNamesIgnoreConditionsAndExtensionData_WithoutEvaluatingDeferredProps()
    {
        var callbacks = 0;
        var props = new ProfileProps
        {
            Deferred = new DeferProp((_, _) => { callbacks++; return Task.FromResult<object?>("expensive"); }),
            Extra = new() { ["extra"] = JsonSerializer.SerializeToElement(42) },
        };
        using var document = await Render(props);
        var json = document.RootElement.GetProperty("props");
        json.GetProperty("display_name").GetString().Should().Be("Ana");
        json.GetProperty("extra").GetInt32().Should().Be(42);
        json.GetProperty("explicitNull").ValueKind.Should().Be(JsonValueKind.Null);
        foreach (var absent in new[] { "secret", "displayName", "empty", "zero", "deferred", "extraData" })
            json.TryGetProperty(absent, out _).Should().BeFalse(absent);
        callbacks.Should().Be(0);
        document.RootElement.GetProperty("deferredProps").GetProperty("default")[0].GetString().Should().Be("deferred");
    }

    [Fact]
    public async Task TypedProps_UseConfiguredNamingAndResolver()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type == typeof(ResolverProps))
                info.Properties.Single(p => p.Name == "secret").ShouldSerialize = (_, _) => false;
        });
        using var doc = await Render(new ResolverProps(), new()
        {
            JsonSerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, TypeInfoResolver = resolver },
        });
        var props = doc.RootElement.GetProperty("props");
        props.GetProperty("display_name").GetString().Should().Be("Ana");
        props.TryGetProperty("secret", out _).Should().BeFalse();
    }

    [Fact]
    public async Task RootConverter_OwnsItsObjectShape()
    {
        var options = new InertiaOptions { JsonSerializerOptions = new() };
        options.JsonSerializerOptions.Converters.Add(new RootConverter());
        using var doc = await Render(new ResolverProps(), options);
        doc.RootElement.GetProperty("props").GetProperty("custom").GetString().Should().Be("Ana");
    }

    [Fact]
    public async Task FieldsAndReadOnlyProperties_FollowOptions()
    {
        using var doc = await Render(new FieldProps(), new()
        {
            JsonSerializerOptions = new() { IncludeFields = true, IgnoreReadOnlyProperties = true },
        });
        var props = doc.RootElement.GetProperty("props");
        props.GetProperty("Field").GetInt32().Should().Be(42);
        props.TryGetProperty("Computed", out _).Should().BeFalse();
        props.GetProperty("Items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task PropertyConvertersAndNumberHandling_ApplyAtTheTopLevelAndInNestedDtos()
    {
        using var doc = await Render(new ConverterProps { Nested = new ConverterValue() });
        var props = doc.RootElement.GetProperty("props");
        props.GetProperty("value").GetString().Should().Be("converted");
        props.GetProperty("count").GetString().Should().Be("42");
        props.GetProperty("nested").GetProperty("value").GetString().Should().Be("converted");
    }

    private sealed class ConverterProps : ConverterValue { public ConverterValue? Nested { get; set; } }
    private class ConverterValue
    {
        [JsonConverter(typeof(ConstantStringConverter))] public string Value { get; set; } = "original";
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public int Count { get; set; } = 42;
    }
    private sealed class ConstantStringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue("converted");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypedContract_MatchesSystemTextJsonForReadOnlyMembersAndStructDefaults(bool ignoreReadOnly)
    {
        var value = new SerializerOracleProps();
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IncludeFields = true,
            IgnoreReadOnlyProperties = ignoreReadOnly, IgnoreReadOnlyFields = ignoreReadOnly,
        };
        var expected = JsonSerializer.Serialize(value, jsonOptions);
        using var doc = await Render(value, new() { JsonSerializerOptions = jsonOptions });
        doc.RootElement.GetProperty("props").GetRawText().Should().Be(expected);
    }

    private class SerializerOracleProps
    {
        public int ReadOnly => 1;
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string KeptReadOnly => "kept";
        public readonly int ReadOnlyField = 2;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public NonZeroConstructorStruct Zero { get; set; } = default;
    }
    private struct NonZeroConstructorStruct
    {
        public int Value { get; set; }
        public NonZeroConstructorStruct() { Value = 42; }
    }

    private static async Task<JsonDocument> Render(object props, InertiaOptions? options = null)
    {
        var ctx = HttpContextHelper.CreateInertia();
        var service = new InertiaService(Options.Create(options ?? new()), new HttpContextAccessor { HttpContext = ctx });
        await service.Render("Profile", props).ExecuteAsync(ctx);
        return JsonDocument.Parse(await ctx.ReadResponseBodyAsync());
    }

    private sealed class ProfileProps
    {
        [JsonIgnore] public string Secret => throw new InvalidOperationException("Ignored getter must never run");
        [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "Ana";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Empty { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Zero { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? ExplicitNull { get; set; }
        public DeferProp? Deferred { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; } = [];
    }
    private sealed class ResolverProps { public string DisplayName { get; set; } = "Ana"; public string Secret { get; set; } = "hidden"; }
    private sealed class FieldProps { public int Field = 42; public string Computed => "computed"; public List<int> Items { get; } = [1, 2]; }
    private sealed class RootConverter : JsonConverter<ResolverProps>
    {
        public override ResolverProps Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, ResolverProps value, JsonSerializerOptions options)
        { writer.WriteStartObject(); writer.WriteString("custom", value.DisplayName); writer.WriteEndObject(); }
    }
}
