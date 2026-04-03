using System.Net;
using System.Text.Json;
using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Middleware;
using InertiaNet.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InertiaNet.Tests.Integration;

file sealed class RequestAwareInertiaMiddleware(RequestDelegate next) : InertiaMiddleware(next)
{
    public const string RootViewHeader = "X-Test-Root-View";
    public const string VersionHeader = "X-Test-Version-Override";

    protected override string? GetVersion(HttpContext context)
        => context.Request.Headers.TryGetValue(VersionHeader, out var version)
            ? version.ToString()
            : null;

    protected override string? GetRootView(HttpContext context)
        => context.Request.Headers.TryGetValue(RootViewHeader, out var rootView)
            ? rootView.ToString()
            : null;
}

file sealed class FakeViewResultExecutor : IActionResultExecutor<ViewResult>
{
    public const string ViewNameHeader = "X-Test-View-Name";
    public const string PageVersionHeader = "X-Test-Page-Version";

    public async Task ExecuteAsync(ActionContext context, ViewResult result)
    {
        var response = context.HttpContext.Response;
        var page = result.ViewData.Model as InertiaPage;

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/plain";
        response.Headers[ViewNameHeader] = result.ViewName ?? string.Empty;
        response.Headers[PageVersionHeader] = page?.Version ?? string.Empty;

        await response.WriteAsync($"Rendered {result.ViewName}");
    }
}

public class RequestScopedRenderSettingsIntegrationTests
{
    [Fact]
    public async Task InertiaRequests_ShouldUseRequestScopedVersion_ForPayloadAndVersionChecks()
    {
        var app = await CreateAppAsync();

        try
        {
            using var overrideRequest = CreateInertiaRequest(clientVersion: "tenant-version");
            overrideRequest.Headers.Add(RequestAwareInertiaMiddleware.VersionHeader, "tenant-version");

            using var overrideResponse = await app.Client.SendAsync(overrideRequest);

            overrideResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            using (var json = JsonDocument.Parse(await overrideResponse.Content.ReadAsStringAsync()))
            {
                json.RootElement.GetProperty("version").GetString().Should().Be("tenant-version");
            }

            using var defaultRequest = CreateInertiaRequest(clientVersion: "configured-version");
            using var defaultResponse = await app.Client.SendAsync(defaultRequest);

            defaultResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            using (var json = JsonDocument.Parse(await defaultResponse.Content.ReadAsStringAsync()))
            {
                json.RootElement.GetProperty("version").GetString().Should().Be("configured-version");
            }

            using var mismatchRequest = CreateInertiaRequest(clientVersion: "configured-version");
            mismatchRequest.Headers.Add(RequestAwareInertiaMiddleware.VersionHeader, "tenant-version");

            using var mismatchResponse = await app.Client.SendAsync(mismatchRequest);

            mismatchResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
            mismatchResponse.Headers.GetValues(HeaderNames.Location).Should().ContainSingle().Which.Should().Be("/");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task InitialRequests_ShouldUseRequestScopedRootView_WithoutLeakingAcrossRequests()
    {
        var app = await CreateAppAsync();

        try
        {
            using var tenantARequest = new HttpRequestMessage(HttpMethod.Get, "/");
            tenantARequest.Headers.Add(RequestAwareInertiaMiddleware.RootViewHeader, "TenantA");

            using var tenantAResponse = await app.Client.SendAsync(tenantARequest);

            tenantAResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            tenantAResponse.Headers.GetValues(FakeViewResultExecutor.ViewNameHeader)
                .Should().ContainSingle().Which.Should().Be("TenantA");

            using var defaultResponse = await app.Client.GetAsync("/");

            defaultResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            defaultResponse.Headers.GetValues(FakeViewResultExecutor.ViewNameHeader)
                .Should().ContainSingle().Which.Should().Be("ConfiguredApp");

            using var tenantBRequest = new HttpRequestMessage(HttpMethod.Get, "/");
            tenantBRequest.Headers.Add(RequestAwareInertiaMiddleware.RootViewHeader, "TenantB");

            using var tenantBResponse = await app.Client.SendAsync(tenantBRequest);

            tenantBResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            tenantBResponse.Headers.GetValues(FakeViewResultExecutor.ViewNameHeader)
                .Should().ContainSingle().Which.Should().Be("TenantB");
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
        builder.Services.Replace(ServiceDescriptor.Singleton<IActionResultExecutor<ViewResult>, FakeViewResultExecutor>());
        builder.Services.AddInertia<RequestAwareInertiaMiddleware>(options =>
        {
            options.RootView = "ConfiguredApp";
            options.Version = () => "configured-version";
        });

        var app = builder.Build();

        app.UseInertia<RequestAwareInertiaMiddleware>();
        app.MapGet("/", (IInertiaService inertia) => inertia.Render("Dashboard"));

        await app.StartAsync();

        return new TestApp(app, app.GetTestClient());
    }

    private static HttpRequestMessage CreateInertiaRequest(string clientVersion)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(HeaderNames.Inertia, "true");
        request.Headers.Add(HeaderNames.Version, clientVersion);
        return request;
    }

    private sealed record TestApp(WebApplication App, HttpClient Client) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => App.DisposeAsync();
    }
}
