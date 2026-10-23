using System.Net;
using System.Text;
using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaNet.Tests.Testing;

public class InertiaTestClientTests
{
    [Fact]
    public async Task ReloadAndDeferredHelpers_SendRealPartialRequests_WithoutMutatingClientDefaults()
    {
        var optional = 0;
        var stats = 0;
        var other = 0;
        var requests = new List<Dictionary<string, string>>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllersWithViews();
        builder.Services.AddInertia(options => options.Version = () => "v1");
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            requests.Add(context.Request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString()));
            await next(context);
        });
        app.UseInertia();
        app.MapGet("/reports", (IInertiaService inertia) => inertia.Render("Reports", new Dictionary<string, object?>
        {
            ["title"] = "Reports",
            ["optional"] = inertia.Optional((_, _) => Task.FromResult<object?>(++optional)),
            ["stats"] = inertia.Defer((_, _) => Task.FromResult<object?>(++stats), "statistics"),
            ["other"] = inertia.Defer((_, _) => Task.FromResult<object?>(++other), "secondary"),
            ["items"] = inertia.Merge(new[] { new { id = 1 } }).MatchOn("id"),
            ["countries"] = inertia.Once((_, _) => Task.FromResult<object?>(new[] { "PT" })).As("country-cache"),
        }));
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Inertia-Version", "v1");
        var initial = await client.GetInertiaAsync("/reports?tab=all");
        initial.HasDeferredProp("stats", "statistics").HasDeferredProp("other", "secondary")
            .HasMergeProp("items").HasMatchingProp("items.id").HasOnceProp("country-cache")
            .DoesNotHaveProp("optional").DoesNotHaveProp("stats");
        (optional, stats, other).Should().Be((0, 0, 0));

        var loaded = await client.LoadDeferredPropsAsync(initial, ["statistics"]);
        loaded.Keys.Should().Equal("statistics");
        loaded["statistics"].HasComponent("Reports").HasProp("stats", 1).DoesNotHaveProp("other").DoesNotHaveProp("title");
        (optional, stats, other).Should().Be((0, 1, 0));
        requests[^1]["X-Inertia-Partial-Component"].Should().Be("Reports");
        requests[^1]["X-Inertia-Partial-Data"].Should().Be("stats");
        requests[^1]["X-Inertia-Version"].Should().Be("v1");

        var reload = await client.ReloadInertiaAsync(initial, new() { Only = ["optional"] });
        reload.HasProp("optional", 1).DoesNotHaveProp("title");
        var excluded = await client.ReloadInertiaAsync(initial, new() { Only = ["title", "optional"], Except = ["optional"] });
        excluded.HasProp("title").DoesNotHaveProp("optional");
        optional.Should().Be(1);
        var reset = await client.ReloadInertiaAsync(initial, new() { Only = ["items"], Reset = ["items"] });
        reset.HasPropCount("items", 1);
        reset.Page.MergeProps.Should().BeNull();
        var onceReload = await client.ReloadInertiaAsync(initial, new() { ExceptOnceProps = ["country-cache"] });
        onceReload.DoesNotHaveProp("countries").HasOnceProp("country-cache");
        client.DefaultRequestHeaders.Should().ContainSingle().Which.Key.Should().Be("X-Inertia-Version");

        var count = requests.Count;
        var unknown = () => client.LoadDeferredPropsAsync(initial, ["statistics", "missing"]);
        await unknown.Should().ThrowAsync<AssertionException>();
        requests.Should().HaveCount(count);
        var all = await client.LoadDeferredPropsAsync(initial);
        all.Keys.Should().BeEquivalentTo("statistics", "secondary");
        (stats, other).Should().Be((2, 1));
    }

    [Theory]
    [InlineData(null, "{\"component\":\"Page\",\"props\":{},\"url\":\"/\"}")]
    [InlineData("text/notjson", "{\"component\":\"Page\",\"props\":{},\"url\":\"/\"}")]
    [InlineData("application/json", "not-json")]
    [InlineData("application/json", "{\"component\":\"Page\",\"props\":null,\"url\":\"/\"}")]
    public async Task PageAssertions_RejectInvalidResponseContracts(string? contentType, string body)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };
        response.Headers.Add("X-Inertia", "true");
        response.Content.Headers.ContentType = contentType is null ? null : new(contentType);
        var assert = () => response.AssertInertiaAsync();
        await assert.Should().ThrowAsync<AssertionException>();
    }

    [Fact]
    public void MetadataAssertions_UseFullPathsAndExactMetadataValues()
    {
        var page = AssertableInertiaPage.FromJson("""
            {"component":"Page","url":"/","props":{"empty":null},
             "mergeProps":["feed.data"],"prependProps":["recent"],"deepMergeProps":["settings"],"matchPropsOn":["feed.data.id"],
             "rescuedProps":["statistics"],"deferredProps":{"stats":["statistics"]},
             "onceProps":{"country-cache":{"prop":"countries","expiresAt":null}},
             "scrollProps":{"feed.data":{"pageName":"p","currentPage":2,"previousPage":1,"nextPage":3,"reset":false}}}
            """);
        page.HasMergeProp("feed.data").HasPrependProp("recent").HasDeepMergeProp("settings").HasMatchingProp("feed.data.id")
            .HasRescuedProp("statistics").HasDeferredProp("statistics", "stats").HasNullProp("empty")
            .HasOnceProp("country-cache", new { prop = "countries", expiresAt = (long?)null })
            .HasScrollProp("feed.data", new { pageName = "p", currentPage = 2, previousPage = 1, nextPage = 3, reset = false });
        var mismatched = () => page.HasOnceProp("country-cache", new { prop = "countries", expiresAt = 10 });
        mismatched.Should().Throw<AssertionException>();
        var absent = () => page.HasMatchingProp("feed.id");
        absent.Should().Throw<AssertionException>();
        var negativeIndex = () => page.HasProp("feed[-1]");
        negativeIndex.Should().Throw<AssertionException>();
    }
}
