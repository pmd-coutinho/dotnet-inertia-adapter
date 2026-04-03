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

internal sealed class RecordingSsrGateway : ISsrGateway
{
    public int DispatchCount { get; private set; }

    public Task<SsrResponse?> DispatchAsync(InertiaPage page, CancellationToken ct = default)
    {
        DispatchCount++;
        return Task.FromResult<SsrResponse?>(new SsrResponse(string.Empty, "<div>SSR</div>"));
    }

    public void Reset() => DispatchCount = 0;
}

internal sealed class SsrTestViewResultExecutor : IActionResultExecutor<ViewResult>
{
    public async Task ExecuteAsync(ActionContext context, ViewResult result)
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
        context.HttpContext.Response.ContentType = "text/plain";
        await context.HttpContext.Response.WriteAsync(result.ViewName ?? string.Empty);
    }
}

public class SsrExclusionIntegrationTests
{
    [Fact]
    public async Task ConfiguredExcludePaths_ShouldSkipSsrDispatch_ForMatchingRequests()
    {
        var app = await CreateAppAsync(options => options.Ssr.ExcludePaths = ["/admin/*"]);

        try
        {
            app.Gateway.Reset();
            using var excludedResponse = await app.Client.GetAsync("/admin");
            excludedResponse.IsSuccessStatusCode.Should().BeTrue();
            app.Gateway.DispatchCount.Should().Be(0);

            using var nestedExcludedResponse = await app.Client.GetAsync("/admin/users");
            nestedExcludedResponse.IsSuccessStatusCode.Should().BeTrue();
            app.Gateway.DispatchCount.Should().Be(0);

            using var includedResponse = await app.Client.GetAsync("/");
            includedResponse.IsSuccessStatusCode.Should().BeTrue();
            app.Gateway.DispatchCount.Should().Be(1);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task WithoutSsr_ShouldSkipSsrDispatch_ForCurrentRequest_WhenCalledWithoutPaths()
    {
        var app = await CreateAppAsync();

        try
        {
            app.Gateway.Reset();
            using var excludedResponse = await app.Client.GetAsync("/reports");
            excludedResponse.IsSuccessStatusCode.Should().BeTrue();
            app.Gateway.DispatchCount.Should().Be(0);

            using var includedResponse = await app.Client.GetAsync("/dashboard");
            includedResponse.IsSuccessStatusCode.Should().BeTrue();
            app.Gateway.DispatchCount.Should().Be(1);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    private static async Task<TestApp> CreateAppAsync(Action<InertiaOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddControllersWithViews();
        builder.Services.Replace(ServiceDescriptor.Singleton<IActionResultExecutor<ViewResult>, SsrTestViewResultExecutor>());
        builder.Services.AddSingleton<RecordingSsrGateway>();
        builder.Services.AddInertiaWithSsr(options =>
        {
            options.RootView = "App";
            configure?.Invoke(options);
        });
        builder.Services.Replace(ServiceDescriptor.Singleton<ISsrGateway>(sp => sp.GetRequiredService<RecordingSsrGateway>()));

        var app = builder.Build();

        app.UseInertia();
        app.MapGet("/", (IInertiaService inertia) => inertia.Render("Home"));
        app.MapGet("/dashboard", (IInertiaService inertia) => inertia.Render("Dashboard"));
        app.MapGet("/admin", (IInertiaService inertia) => inertia.Render("Admin/Index"));
        app.MapGet("/admin/users", (IInertiaService inertia) => inertia.Render("Admin/Users"));
        app.MapGet("/reports", (IInertiaService inertia) =>
        {
            inertia.WithoutSsr();
            return inertia.Render("Reports");
        });

        await app.StartAsync();

        return new TestApp(app, app.GetTestClient(), app.Services.GetRequiredService<RecordingSsrGateway>());
    }

    private sealed record TestApp(WebApplication App, HttpClient Client, RecordingSsrGateway Gateway) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => App.DisposeAsync();
    }
}
