using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaNet.Tests.Core;

public class SerializedPropNamesTests
{
    [Fact]
    public async Task LiteralDtoNames_StayLiteral_AndNestedNames_AreNotChangedByDictionaryPolicy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInertia(options => options.JsonSerializerOptions = new()
        {
            PropertyNamingPolicy = null, DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var inertia = scope.ServiceProvider.GetRequiredService<IInertiaService>();
        inertia.ShareProps(new LiteralShared());
        inertia.Share("auth.userName", "shared path");
        var context = Context(scope.ServiceProvider);
        await inertia.Render("Names", new LiteralProps()).ExecuteAsync(context);
        using var page = await Read(context);
        var props = page.RootElement.GetProperty("props");
        props.GetProperty("literal.dot").GetString().Should().Be("literal");
        props.GetProperty("shared.dot").GetString().Should().Be("shared literal");
        props.GetProperty("Nested").GetProperty("wireName").GetString().Should().Be("nested literal");
        props.GetProperty("auth").GetProperty("userName").GetString().Should().Be("shared path");
        props.GetProperty("Lookup").GetProperty("user_name").GetInt32().Should().Be(1);
        page.RootElement.GetProperty("sharedProps").EnumerateArray().Select(value => value.GetString()).Should().Contain("shared.dot").And.Contain("auth");
        inertia.GetSharedProps()["shared.dot"].Should().Be("shared literal");
    }

    [Fact]
    public async Task DictionaryKeys_AreNormalizedBeforePartialFiltering_AndExactlyOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInertia(options => options.JsonSerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DictionaryKeyPolicy = new PrefixPolicy() });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = Context(scope.ServiceProvider);
        context.Request.Headers["X-Inertia-Partial-Component"] = "Names";
        context.Request.Headers["X-Inertia-Partial-Data"] = "lookup.prefix_userName";
        var original = new Dictionary<string, object?> { ["userName"] = 1, ["excluded"] = 2 };
        await scope.ServiceProvider.GetRequiredService<IInertiaService>().Render("Names", new { Lookup = original }).ExecuteAsync(context);
        using var page = await Read(context);
        var lookup = page.RootElement.GetProperty("props").GetProperty("lookup");
        lookup.GetProperty("prefix_userName").GetInt32().Should().Be(1);
        lookup.EnumerateObject().Should().ContainSingle();
        original.Keys.Should().Equal("userName", "excluded");
    }

    [Fact]
    public async Task ApiDotPaths_KeepTheirMeaning_WhenTheyOverrideTypedNames()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInertia();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var inertia = scope.ServiceProvider.GetRequiredService<IInertiaService>();
        inertia.ShareProps(new LiteralShared());
        inertia.Share("shared.dot", "override path");
        var context = Context(scope.ServiceProvider);
        await inertia.Render("Names").ExecuteAsync(context);
        using var page = await Read(context);
        page.RootElement.GetProperty("props").GetProperty("shared").GetProperty("dot").GetString().Should().Be("override path");
        page.RootElement.GetProperty("props").TryGetProperty("shared.dot", out _).Should().BeFalse();
    }

    private static DefaultHttpContext Context(IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Headers["X-Inertia"] = "true";
        context.Response.Body = new MemoryStream();
        return context;
    }
    private static async Task<JsonDocument> Read(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }
    private sealed class LiteralProps
    {
        [JsonPropertyName("literal.dot")] public string Name => "literal";
        public LiteralNested Nested => new();
        public Dictionary<string, int> Lookup => new() { ["userName"] = 1 };
    }
    private sealed class LiteralNested { [JsonPropertyName("wireName")] public string Name => "nested literal"; }
    private sealed class LiteralShared { [JsonPropertyName("shared.dot")] public string Name => "shared literal"; }
    private sealed class PrefixPolicy : JsonNamingPolicy { public override string ConvertName(string name) => "prefix_" + name; }
}
