using System.Net;
using System.Text.Json;
using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Support;
using InertiaNet.Tests.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InertiaNet.Tests.Integration;

public class ProtocolLifecycleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("old")]
    public async Task VersionConflict_PreservesAllTransientStateForTheReload(string? version)
    {
        var data = new FakeTempDataFactory();
        data.TempData[SessionKeys.FlashData] = "{\"details\":{\"ids\":[1,2]}}";
        data.TempData[SessionKeys.ValidationErrors] = "{\"email\":[\"Required\"]}";
        data.TempData[SessionKeys.ErrorBag] = "create";
        data.TempData[SessionKeys.ClearHistory] = true;
        data.TempData[SessionKeys.PreserveFragment] = true;
        await using var app = await App(data);
        using var client = app.GetTestClient();
        using var request = Request(HttpMethod.Get, "/page", version);
        using var conflict = await client.SendAsync(request);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        conflict.Headers.GetValues(HeaderNames.Version).Should().Equal("new");
        conflict.Headers.Vary.Should().Contain(HeaderNames.Inertia);
        data.TempData.Count.Should().Be(5);

        using var reload = await client.SendAsync(Request(HttpMethod.Get, "/page", "new"));
        using var doc = JsonDocument.Parse(await reload.Content.ReadAsStringAsync());
        var page = doc.RootElement;
        page.GetProperty("flash").GetProperty("details").GetProperty("ids")[1].GetInt32().Should().Be(2);
        page.GetProperty("props").GetProperty("errors").GetProperty("create").GetProperty("email").GetString().Should().Be("Required");
        page.GetProperty("clearHistory").GetBoolean().Should().BeTrue();
        page.GetProperty("preserveFragment").GetBoolean().Should().BeTrue();
        data.TempData.Should().BeEmpty();
    }

    [Fact]
    public async Task SuccessfulPartial_AlwaysIncludesEmptyErrors()
    {
        await using var app = await App();
        using var client = app.GetTestClient();
        using var request = Request(HttpMethod.Get, "/page", "new");
        request.Headers.Add(HeaderNames.PartialComponent, "Page");
        request.Headers.Add(HeaderNames.PartialOnly, "title");
        using var response = await client.SendAsync(request);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("props").GetProperty("errors").EnumerateObject().Should().BeEmpty();
    }

    [Theory]
    [InlineData(307)]
    [InlineData(308)]
    [InlineData(301)]
    public async Task DeliberateRedirectStatus_IsPreserved(int status)
    {
        await using var app = await App();
        using var client = app.GetTestClient();
        using var response = await client.SendAsync(Request(HttpMethod.Put, $"/redirect/{status}", null));
        ((int)response.StatusCode).Should().Be(status);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task FoundRedirect_FromMutation_UsesSeeOther(string method)
    {
        await using var app = await App();
        using var client = app.GetTestClient();
        using var response = await client.SendAsync(Request(new HttpMethod(method), "/redirect/302", null));
        response.StatusCode.Should().Be(HttpStatusCode.SeeOther);
    }

    [Fact]
    public async Task HeadRedirect_RetainsItsSafeMethodStatus()
    {
        await using var app = await App();
        using var client = app.GetTestClient();
        using var response = await client.SendAsync(Request(HttpMethod.Head, "/redirect/302", null));
        response.StatusCode.Should().Be(HttpStatusCode.Found);
    }

    [Fact]
    public async Task Vary_PreservesExistingTokensAndDeduplicatesInertia()
    {
        await using var app = await App();
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/vary");
        response.Headers.Vary.Should().BeEquivalentTo("Accept-Encoding", "x-inertia");
    }

    [Fact]
    public async Task EmptyMutation_RedirectsBackAndPersistsFlash()
    {
        var data = new FakeTempDataFactory();
        await using var app = await App(data);
        using var client = app.GetTestClient();
        using var request = Request(HttpMethod.Post, "/empty", null);
        request.Headers.Referrer = new Uri("http://localhost/form");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.SeeOther);
        response.Headers.Location!.OriginalString.Should().Be("/form");
        data.TempData.Should().ContainKey(SessionKeys.FlashData);
    }

    [Fact]
    public async Task StreamedResponse_IsNotBufferedOrRewritten()
    {
        await using var app = await App();
        using var client = app.GetTestClient();
        using var response = await client.SendAsync(Request(HttpMethod.Get, "/stream", "new"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("0");
    }

    private static async Task<WebApplication> App(FakeTempDataFactory? data = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllersWithViews();
        builder.Services.Replace(ServiceDescriptor.Singleton<ITempDataDictionaryFactory>(data ?? new FakeTempDataFactory()));
        builder.Services.AddInertia(options => options.Version = () => "new");
        var app = builder.Build();
        app.UseInertia();
        app.MapGet("/page", (IInertiaService inertia) => inertia.Render("Page", new { title = "Hello" }));
        app.MapMethods("/redirect/{status:int}", ["HEAD", "POST", "PUT", "PATCH", "DELETE"], (int status, HttpContext ctx) =>
        { ctx.Response.StatusCode = status; ctx.Response.Headers.Location = "/page"; return Task.CompletedTask; });
        app.MapPost("/empty", (IInertiaService inertia) => { inertia.Flash("saved", true); });
        app.MapGet("/vary", (HttpContext ctx) => { ctx.Response.Headers.Vary = "Accept-Encoding, x-inertia"; return "plain"; });
        app.MapGet("/stream", async (HttpContext ctx) =>
        { await ctx.Response.Body.FlushAsync(); await ctx.Response.WriteAsync("0"); });
        await app.StartAsync();
        return app;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? version)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(HeaderNames.Inertia, "true");
        if (version is not null) request.Headers.Add(HeaderNames.Version, version);
        return request;
    }
}
