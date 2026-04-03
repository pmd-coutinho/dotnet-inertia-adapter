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

internal sealed class ThrowingSsrGateway : ISsrGateway
{
    public Task<SsrResponse?> DispatchAsync(InertiaPage page, CancellationToken ct = default)
        => throw new SsrException(SsrErrorType.Connection, page.Component, new HttpRequestException("SSR unavailable"));
}

internal sealed class ErrorViewResultExecutor : IActionResultExecutor<ViewResult>
{
    public const string ComponentHeader = "X-Test-Component";

    public async Task ExecuteAsync(ActionContext context, ViewResult result)
    {
        var page = result.ViewData.Model as InertiaPage;

        context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
        context.HttpContext.Response.ContentType = "text/plain";
        context.HttpContext.Response.Headers[ComponentHeader] = page?.Component ?? string.Empty;

        await context.HttpContext.Response.WriteAsync(page?.Component ?? string.Empty);
    }
}

public class SsrExceptionHandlingIntegrationTests
{
    [Fact]
    public async Task InitialRender_ShouldAllowCustomErrorPages_WhenSsrThrows()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddControllersWithViews();
        builder.Services.Replace(ServiceDescriptor.Singleton<IActionResultExecutor<ViewResult>, ErrorViewResultExecutor>());
        builder.Services.AddInertiaWithSsr(options =>
        {
            options.RootView = "App";
            options.Ssr.ThrowOnError = true;
            options.HandleExceptionsUsing = (exception, context) =>
            {
                var inertia = context.RequestServices.GetRequiredService<IInertiaService>();
                inertia.WithoutSsr();
                return inertia.Render("Errors/Ssr", new { message = exception.Message });
            };
        });
        builder.Services.Replace(ServiceDescriptor.Singleton<ISsrGateway, ThrowingSsrGateway>());

        var app = builder.Build();
        app.UseInertia();
        app.MapGet("/", () => InertiaResults.Inertia("Dashboard", new { ready = true }));

        await app.StartAsync();

        try
        {
            using var response = await app.GetTestClient().GetAsync("/");

            response.IsSuccessStatusCode.Should().BeTrue();
            response.Headers.GetValues(ErrorViewResultExecutor.ComponentHeader)
                .Should().ContainSingle().Which.Should().Be("Errors/Ssr");
            (await response.Content.ReadAsStringAsync()).Should().Be("Errors/Ssr");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }
}
