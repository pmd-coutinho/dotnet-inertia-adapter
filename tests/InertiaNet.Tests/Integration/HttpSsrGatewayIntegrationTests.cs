using System.Net;
using System.Text;
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

public class HttpSsrGatewayIntegrationTests
{
    [Fact]
    public async Task InitialRender_ShouldUseViteHotFileEndpoint_WhenPresent()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("inertianet-vite-hot-");

        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory.FullName, "custom.hot"),
                "http://127.0.0.1:5173");

            var httpClientFactory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"head\":[],\"body\":\"<div>SSR</div>\"}", Encoding.UTF8, "application/json"),
            });

            var app = await CreateAppAsync(
                httpClientFactory,
                configureInertia: options => options.Ssr.ThrowOnError = false,
                configureVite: options =>
                {
                    options.PublicDirectory = tempDirectory.FullName;
                    options.HotFile = "custom.hot";
                });

            try
            {
                using var response = await app.Client.GetAsync("/");

                response.IsSuccessStatusCode.Should().BeTrue();
                response.Headers.GetValues(SsrGatewayViewResultExecutor.SsrMarkerHeader)
                    .Should().ContainSingle().Which.Should().Be("present");
                httpClientFactory.LastRequestUri.Should().Be(new Uri("http://127.0.0.1:5173/__inertia_ssr"));
            }
            finally
            {
                await app.DisposeAsync();
            }
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InitialRender_ShouldPropagateSsrException_WhenThrowOnErrorIsEnabled()
    {
        var httpClientFactory = new RecordingHttpClientFactory(_ => throw new HttpRequestException("SSR server unavailable"));
        var app = await CreateAppAsync(
            httpClientFactory,
            configureInertia: options => options.Ssr.ThrowOnError = true);

        try
        {
            var act = () => app.Client.GetAsync("/");

            var exception = await act.Should().ThrowAsync<SsrException>();
            exception.Which.ErrorType.Should().Be(SsrErrorType.Connection);
            exception.Which.Component.Should().Be("Home");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    private static async Task<TestApp> CreateAppAsync(
        RecordingHttpClientFactory httpClientFactory,
        Action<InertiaOptions>? configureInertia = null,
        Action<ViteOptions>? configureVite = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddControllersWithViews();
        builder.Services.Replace(ServiceDescriptor.Singleton<IActionResultExecutor<ViewResult>, SsrGatewayViewResultExecutor>());
        builder.Services.AddViteHelper(options => configureVite?.Invoke(options));
        builder.Services.AddInertiaWithSsr(options =>
        {
            options.RootView = "App";
            configureInertia?.Invoke(options);
        });
        builder.Services.Replace(ServiceDescriptor.Singleton<IHttpClientFactory>(httpClientFactory));
        builder.Services.Replace(ServiceDescriptor.Transient<ISsrGateway, HttpSsrGateway>());

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

    private sealed class RecordingHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : IHttpClientFactory
    {
        public Uri? LastRequestUri { get; private set; }

        public HttpClient CreateClient(string name)
            => new(new RecordingHandler(this, responseFactory), disposeHandler: false);

        private sealed class RecordingHandler(
            RecordingHttpClientFactory factory,
            Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                factory.LastRequestUri = request.RequestUri;
                return Task.FromResult(responseFactory(request));
            }
        }
    }

    private sealed class SsrGatewayViewResultExecutor : IActionResultExecutor<ViewResult>
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
