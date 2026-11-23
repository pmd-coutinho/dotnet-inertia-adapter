using System.Net;
using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Middleware;
using InertiaNet.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wolverine;
using Wolverine.Http;
using Xunit;

namespace InertiaNet.Wolverine.Tests;

public static class TestEndpoints
{
    [WolverineGet("/")]
    public static IResult Home(IInertiaService inertia) => inertia.Render("Home", new { ready = true });

    [WolverineGet("/deferred")]
    public static IResult Deferred(IInertiaService inertia) => inertia.Render("Deferred", new Dictionary<string, object?>
    {
        ["first"] = 1,
        ["second"] = 2,
        ["late"] = inertia.Defer((_, _) => Task.FromResult<object?>("loaded"), "history"),
    });
}

public class SharingMiddleware(RequestDelegate next) : InertiaMiddleware(next)
{
    protected override Task Share(HttpContext context, IInertiaService inertia)
    {
        inertia.Share("shared", "from-middleware");
        return Task.CompletedTask;
    }
}

internal sealed class RootViewExecutor : IActionResultExecutor<ViewResult>
{
    public const string ViewNameHeader = "X-Test-View-Name";

    public async Task ExecuteAsync(ActionContext context, ViewResult result)
    {
        var page = result.ViewData.Model as InertiaPage;
        context.HttpContext.Response.ContentType = "text/html";
        context.HttpContext.Response.Headers[ViewNameHeader] = result.ViewName ?? string.Empty;
        await context.HttpContext.Response.WriteAsync($"<div data-page=\"{page?.Component}\"></div>");
    }
}

public sealed class WolverineHttpIntegrationTests : IAsyncLifetime
{
    private const string Version = "v1";
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Host.UseWolverine(opts => opts.Discovery.IncludeAssembly(typeof(TestEndpoints).Assembly));
        builder.Services.AddWolverineHttp();
        builder.Services.AddControllersWithViews();
        builder.Services.Replace(ServiceDescriptor.Singleton<IActionResultExecutor<ViewResult>, RootViewExecutor>());
        builder.Services.AddInertia<SharingMiddleware>(options =>
        {
            options.RootView = "App";
            options.Version = () => Version;
        });

        _app = builder.Build();
        _app.UseInertia<SharingMiddleware>();
        _app.MapWolverineEndpoints(opts => opts.UseInertia());
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task InitialVisit_ShouldRenderRootView()
    {
        using var response = await _client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues(RootViewExecutor.ViewNameHeader).Should().ContainSingle().Which.Should().Be("App");
        (await response.Content.ReadAsStringAsync()).Should().Contain("Home");
    }

    [Fact]
    public async Task InertiaVisit_ShouldReturnPageJson_WithMiddlewareSharedProps()
    {
        using var response = await _client.SendAsync(InertiaRequest("/"));
        var page = await response.AssertInertiaAsync();

        page.HasComponent("Home")
            .HasProp("ready", true)
            .HasProp("shared", "from-middleware")
            .HasUrl("/");
    }

    [Fact]
    public async Task OutdatedAssetVersion_ShouldReturnVersionConflict()
    {
        using var response = await _client.SendAsync(InertiaRequest("/", version: "outdated"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.AssertVersionRedirect();
    }

    [Fact]
    public async Task PartialReload_ShouldReturnOnlyRequestedProps()
    {
        using var request = InertiaRequest("/deferred");
        request.Headers.Add("X-Inertia-Partial-Component", "Deferred");
        request.Headers.Add("X-Inertia-Partial-Data", "second");

        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("\"second\":2");
        body.Should().NotContain("\"first\":");
    }

    [Fact]
    public async Task DeferredProp_ShouldBeOmittedFirst_ThenLoadedByPartialReload()
    {
        using var firstResponse = await _client.SendAsync(InertiaRequest("/deferred"));
        var first = await firstResponse.Content.ReadAsStringAsync();
        first.Should().NotContain("loaded");
        first.Should().Contain("\"history\"");

        using var request = InertiaRequest("/deferred");
        request.Headers.Add("X-Inertia-Partial-Component", "Deferred");
        request.Headers.Add("X-Inertia-Partial-Data", "late");
        using var partialResponse = await _client.SendAsync(request);
        var partial = await partialResponse.Content.ReadAsStringAsync();

        partial.Should().Contain("loaded");
        partial.Should().NotContain("\"first\":");
    }

    private static HttpRequestMessage InertiaRequest(string url, string version = Version)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Inertia", "true");
        request.Headers.Add("X-Inertia-Version", version);
        return request;
    }
}
