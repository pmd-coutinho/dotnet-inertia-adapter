using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Ssr;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InertiaNet.Tests.Integration;

public class SsrFallbackIntegrationTests
{
    [Fact]
    public async Task InitialRender_ShouldFallbackToCsr_WhenSsrReturnsNull()
    {
        var app = await CreateAppAsync(new NullSsrGateway());

        try
        {
            using var response = await app.Client.GetAsync("/");

            response.IsSuccessStatusCode.Should().BeTrue();
            response.Headers.GetValues(FallbackViewResultExecutor.SsrMarkerHeader)
                .Should().ContainSingle().Which.Should().Be("missing");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task InitialRender_ShouldExposeSsrPayload_WhenGatewayReturnsMarkup()
    {
        var app = await CreateAppAsync(new StaticSsrGateway(new SsrResponse("<title>SSR</title>", "<div>SSR</div>")));

        try
        {
            using var response = await app.Client.GetAsync("/");

            response.IsSuccessStatusCode.Should().BeTrue();
            response.Headers.GetValues(FallbackViewResultExecutor.SsrMarkerHeader)
                .Should().ContainSingle().Which.Should().Be("present");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    private static async Task<TestApp> CreateAppAsync(ISsrGateway ssrGateway)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddControllersWithViews();
        builder.Services.Replace(ServiceDescriptor.Singleton<IActionResultExecutor<ViewResult>, FallbackViewResultExecutor>());
        builder.Services.AddInertiaWithSsr(options => options.RootView = "App");
        builder.Services.Replace(ServiceDescriptor.Singleton(ssrGateway));

        var app = builder.Build();

        app.UseInertia();
        app.MapGet("/", (IInertiaService inertia) => inertia.Render("Home"));

        await app.StartAsync();

        return new TestApp(app, app.GetTestClient());
    }

    private sealed record TestApp(WebApplication App, HttpClient Client) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => App.DisposeAsync();
    }

    private sealed class NullSsrGateway : ISsrGateway
    {
        public Task<SsrResponse?> DispatchAsync(InertiaPage page, CancellationToken ct = default)
            => Task.FromResult<SsrResponse?>(null);
    }

    private sealed class StaticSsrGateway(SsrResponse response) : ISsrGateway
    {
        public Task<SsrResponse?> DispatchAsync(InertiaPage page, CancellationToken ct = default)
            => Task.FromResult<SsrResponse?>(response);
    }

    private sealed class FallbackViewResultExecutor : IActionResultExecutor<ViewResult>
    {
        public const string SsrMarkerHeader = "X-Test-Ssr-Present";

        public async Task ExecuteAsync(ActionContext context, ViewResult result)
        {
            var hasSsr = context.HttpContext.Items[InertiaContextKeys.SsrResponseKey] is SsrResponse;

            context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
            context.HttpContext.Response.ContentType = "text/plain";
            context.HttpContext.Response.Headers[SsrMarkerHeader] = hasSsr ? "present" : "missing";

            await context.HttpContext.Response.WriteAsync(result.ViewName ?? string.Empty);
        }
    }
}
