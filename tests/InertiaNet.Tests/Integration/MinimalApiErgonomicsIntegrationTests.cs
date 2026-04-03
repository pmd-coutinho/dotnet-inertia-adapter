using System.Net;
using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Support;
using InertiaNet.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InertiaNet.Tests.Integration;

internal sealed class MinimalApiViewResultExecutor : IActionResultExecutor<ViewResult>
{
    public const string ViewNameHeader = "X-Test-View-Name";
    public const string ComponentHeader = "X-Test-Component";

    public async Task ExecuteAsync(ActionContext context, ViewResult result)
    {
        var page = result.ViewData.Model as InertiaPage;

        context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
        context.HttpContext.Response.ContentType = "text/plain";
        context.HttpContext.Response.Headers[ViewNameHeader] = result.ViewName ?? string.Empty;
        context.HttpContext.Response.Headers[ComponentHeader] = page?.Component ?? string.Empty;

        await context.HttpContext.Response.WriteAsync(page?.Component ?? string.Empty);
    }
}

public class MinimalApiErgonomicsIntegrationTests
{
    [Fact]
    public async Task InertiaResults_ShouldRenderJsonResponses_ForMinimalApis()
    {
        var app = await CreateAppAsync();

        try
        {
            using var request = CreateInertiaRequest(HttpMethod.Get, "/results");
            using var response = await app.Client.SendAsync(request);
            var page = await response.AssertInertiaAsync();

            page.HasComponent("Results/Index")
                .HasProp("ready", true)
                .HasUrl("/results");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task MapInertia_ShouldSupportDynamicPropsFactories()
    {
        var app = await CreateAppAsync();

        try
        {
            using var request = CreateInertiaRequest(HttpMethod.Get, "/about");
            using var response = await app.Client.SendAsync(request);
            var page = await response.AssertInertiaAsync();

            page.HasComponent("About")
                .HasProp("path", "/about")
                .HasProp("method", "GET");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task MapInertiaFallback_ShouldRenderFallbackComponent()
    {
        var app = await CreateAppAsync();

        try
        {
            using var request = CreateInertiaRequest(HttpMethod.Get, "/client/orders/42");
            using var response = await app.Client.SendAsync(request);
            var page = await response.AssertInertiaAsync();

            page.HasComponent("Spa/Fallback")
                .HasProp("path", "/client/orders/42");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task InertiaResults_Location_ShouldUseInertiaLocationProtocol()
    {
        var app = await CreateAppAsync();

        try
        {
            using var request = CreateInertiaRequest(HttpMethod.Post, "/leave");
            using var response = await app.Client.SendAsync(request);

            response.AssertInertiaRedirect().To("https://example.com/logout");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task InitialRequests_ShouldStillRenderRootView_ForInertiaResults()
    {
        var app = await CreateAppAsync();

        try
        {
            using var response = await app.Client.GetAsync("/results");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.GetValues(MinimalApiViewResultExecutor.ViewNameHeader)
                .Should().ContainSingle().Which.Should().Be("App");
            response.Headers.GetValues(MinimalApiViewResultExecutor.ComponentHeader)
                .Should().ContainSingle().Which.Should().Be("Results/Index");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task InitialRequests_ShouldThrowHelpfulError_WhenRazorViewServicesAreMissing()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddInertia(options => options.RootView = "App");

        var app = builder.Build();
        app.UseInertia();
        app.MapGet("/", () => InertiaResults.Inertia("Results/Index", new { ready = true }));

        await app.StartAsync();

        try
        {
            var act = () => app.GetTestClient().GetAsync("/");

            var exception = await act.Should().ThrowAsync<InvalidOperationException>();
            exception.Which.Message.Should().Contain("Initial Inertia HTML rendering requires Razor view services");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    private static async Task<TestApp> CreateAppAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddControllersWithViews();
        builder.Services.Replace(ServiceDescriptor.Singleton<IActionResultExecutor<ViewResult>, MinimalApiViewResultExecutor>());
        builder.Services.AddInertia(options => options.RootView = "App");

        var app = builder.Build();

        app.UseInertia();
        app.MapGet("/results", () => InertiaResults.Inertia("Results/Index", new { ready = true }));
        app.MapPost("/leave", () => InertiaResults.Location("https://example.com/logout"));
        app.MapInertia("/about", "About", context => new
        {
            path = context.Request.Path.Value,
            method = context.Request.Method,
        });
        app.MapInertiaFallback("Spa/Fallback", context => new { path = context.Request.Path.Value });

        await app.StartAsync();

        return new TestApp(app, app.GetTestClient());
    }

    private static HttpRequestMessage CreateInertiaRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add(HeaderNames.Inertia, "true");
        return request;
    }

    private sealed record TestApp(WebApplication App, HttpClient Client) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => App.DisposeAsync();
    }
}
