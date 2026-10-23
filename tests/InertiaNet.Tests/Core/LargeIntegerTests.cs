using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaNet.Tests.Core;

public class LargeIntegerTests
{
    [Theory]
    [InlineData(LargeIntegerSerialization.Number)]
    [InlineData(LargeIntegerSerialization.String)]
    [InlineData(LargeIntegerSerialization.BigInt)]
    public async Task Rendering_PreservesExactDigitsAcrossNestedTypedSharedLazyAndFlashValues(LargeIntegerSerialization policy)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInertia(options => options.LargeIntegerSerialization = policy);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var inertia = scope.ServiceProvider.GetRequiredService<IInertiaService>();
        inertia.Share("shared", ulong.MaxValue);
        inertia.Flash("receipt", long.MinValue);
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers["X-Inertia"] = "true";
        context.Response.Body = new MemoryStream();
        await inertia.Render("Numbers", new IntegerProps()).ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var page = await JsonDocument.ParseAsync(context.Response.Body);
        var assertion = AssertableInertiaPage.FromJson(page.RootElement.GetRawText())
            .HasBigIntegerProp("large", long.MaxValue).HasBigIntegerProp("huge", BigInteger.Parse("999999999999999999999999999999999999"))
            .HasBigIntegerProp("nested[0]", long.MinValue).HasBigIntegerFlash("receipt", long.MinValue);
        Action mismatch = () => assertion.HasBigIntegerProp("large", long.MaxValue - 1);
        mismatch.Should().Throw<AssertionException>();
        if (policy == LargeIntegerSerialization.BigInt) assertion.HasPreserveBigIntegers();
        var props = page.RootElement.GetProperty("props");
        Digits(props.GetProperty("large"), policy).Should().Be(long.MaxValue.ToString());
        Digits(props.GetProperty("shared"), policy).Should().Be(ulong.MaxValue.ToString());
        Digits(props.GetProperty("huge"), policy).Should().Be("999999999999999999999999999999999999");
        Digits(props.GetProperty("nested")[0], policy).Should().Be(long.MinValue.ToString());
        Digits(props.GetProperty("factory"), policy).Should().Be(long.MaxValue.ToString());
        Digits(props.GetProperty("lookup").GetProperty("key"), policy).Should().Be(long.MinValue.ToString());
        Digits(props.GetProperty("numericKeys").GetProperty(long.MaxValue.ToString()), policy).Should().Be(ulong.MaxValue.ToString());
        Digits(props.GetProperty("state"), policy).Should().Be(long.MaxValue.ToString());
        Digits(page.RootElement.GetProperty("flash").GetProperty("receipt"), policy).Should().Be(long.MinValue.ToString());
        props.GetProperty("decimal").GetDecimal().Should().Be(9007199254740992m);
        props.GetProperty("asString").GetString().Should().Be(long.MaxValue.ToString());
        props.GetProperty("custom").GetString().Should().Be("custom integer");
        props.GetProperty("small").ValueKind.Should().Be(policy == LargeIntegerSerialization.String ? JsonValueKind.String : JsonValueKind.Number);
        props.GetProperty("safe").ValueKind.Should().Be(policy == LargeIntegerSerialization.String ? JsonValueKind.String : JsonValueKind.Number);
        props.TryGetProperty("nullable", out _).Should().BeFalse();
        if (policy == LargeIntegerSerialization.BigInt) page.RootElement.GetProperty("preserveBigIntegers").GetBoolean().Should().BeTrue();
        else page.RootElement.TryGetProperty("preserveBigIntegers", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(LargeIntegerSerialization.String)]
    [InlineData(LargeIntegerSerialization.BigInt)]
    public void Options_RoundTripIntegersAndDictionaryKeys_AndRespectExplicitConverters(LargeIntegerSerialization policy)
    {
        var options = InertiaJsonOptions.GetOptions(new() { LargeIntegerSerialization = policy });
        var value = new Dictionary<BigInteger, ulong> { [BigInteger.Pow(10, 40)] = ulong.MaxValue };
        var json = JsonSerializer.Serialize(value, options);
        JsonSerializer.Deserialize<Dictionary<BigInteger, ulong>>(json, options).Should().BeEquivalentTo(value);
        JsonSerializer.Deserialize<long>(JsonSerializer.Serialize(long.MinValue, options), options).Should().Be(long.MinValue);
        JsonSerializer.Deserialize<BigInteger>(JsonSerializer.Serialize(BigInteger.Pow(10, 80), options), options).Should().Be(BigInteger.Pow(10, 80));
        var custom = new JsonSerializerOptions();
        custom.Converters.Add(new JsonStringEnumConverter());
        var explicitOptions = InertiaJsonOptions.GetOptions(new() { LargeIntegerSerialization = policy, JsonSerializerOptions = custom });
        JsonSerializer.Serialize(LargeState.Large, explicitOptions).Should().Be("\"Large\"");
        var page = new InertiaPage { Component = "Numbers", Url = "/", Props = [], OnceProps = new Dictionary<string, object?> { ["once"] = new { expiresAt = 1234L } } };
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(page, options));
        result.RootElement.GetProperty("onceProps").GetProperty("once").GetProperty("expiresAt").ValueKind.Should().Be(JsonValueKind.Number);
    }

    [Fact]
    public void TestAssertions_RequireMarkerOptIn_AndRejectNonIntegerValues()
    {
        var page = AssertableInertiaPage.FromJson("""{"component":"Numbers","url":"/","props":{"id":{"$bigint":"123"},"fraction":1.5}}""");
        new AssertableInertiaPage(new() { Component = "Numbers", Url = "/", Props = new() { ["huge"] = BigInteger.Pow(10, 40) } })
            .HasBigIntegerProp("huge", BigInteger.Pow(10, 40));
        Action unmarked = () => page.HasBigIntegerProp("id", 123);
        unmarked.Should().Throw<AssertionException>();
        Action fraction = () => page.HasBigIntegerProp("fraction", 1);
        fraction.Should().Throw<AssertionException>();
        Action flag = () => page.HasPreserveBigIntegers();
        flag.Should().Throw<AssertionException>();
    }

    private static string Digits(JsonElement value, LargeIntegerSerialization policy) => policy switch
    {
        LargeIntegerSerialization.String => value.GetString()!,
        LargeIntegerSerialization.BigInt => value.GetProperty("$bigint").GetString()!,
        _ => value.GetRawText(),
    };
    private sealed class IntegerProps
    {
        public long Large => long.MaxValue;
        public BigInteger Huge => BigInteger.Parse("999999999999999999999999999999999999");
        public long Small => 42L;
        public long Safe => 9007199254740991L;
        public long? Nullable => null;
        public long[] Nested => [long.MinValue];
        public Func<long> Factory => () => long.MaxValue;
        public Dictionary<string, long> Lookup => new() { ["key"] = long.MinValue };
        public Dictionary<long, ulong> NumericKeys => new() { [long.MaxValue] = ulong.MaxValue };
        public LargeState State => LargeState.Large;
        public decimal Decimal => 9007199254740992m;
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long AsString => long.MaxValue;
        [JsonConverter(typeof(CustomIntegerConverter))] public long Custom => long.MaxValue;
    }
    public enum LargeState : long { Large = long.MaxValue }
    public sealed class CustomIntegerConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteStringValue("custom integer");
    }
}
