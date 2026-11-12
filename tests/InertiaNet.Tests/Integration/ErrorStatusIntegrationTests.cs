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

/// <summary>
/// Mirrors the framework view executor's status handling: it applies <see cref="ViewResult.StatusCode"/>
/// when present and otherwise leaves the response status untouched.
/// </summary>
internal sealed class StatusPreservingViewResultExecutor : IActionResultExecutor<ViewResult>
{
    public const string PageComponentHeader = "X-Test-Page-Component";
    public const string PageUrlHeader = "X-Test-Page-Url";

    public async Task ExecuteAsync(ActionContext context, ViewResult result)
    {
        var page = result.ViewData.Model as InertiaPage;
        var response = context.HttpContext.Response;

        if (result.StatusCode is { } statusCode)
            response.StatusCode = statusCode;

        response.ContentType = "text/html";
        response.Headers[PageComponentHeader] = page?.Component ?? string.Empty;
        response.Headers[PageUrlHeader] = page?.Url ?? string.Empty;

        await response.WriteAsync($"Rendered {page?.Component}");
    }
}

public sealed class ErrorStatusProbeException : Exception;

[Route("error-status/mvc")]
public sealed class ErrorStatusProbeController : Controller
{
    [HttpGet("missing")]
    public IActionResult Missing()
        => this.Inertia("Errors/Status", new { status = StatusCodes.Status404NotFound })
            .WithStatus(StatusCodes.Status404NotFound);
}

public class ErrorStatusIntegrationTests
{
    // ── WithStatus ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/error-status/mvc/missing", HttpStatusCode.NotFound)]
    [InlineData("/minimal/forbidden", HttpStatusCode.Forbidden)]
    public async Task WithStatus_ShouldApplyToInitialHtmlResponses(string path, HttpStatusCode expected)
    {
        await using var app = await CreateAppAsync();

        using var response = await app.Client.GetAsync(path);

        response.StatusCode.Should().Be(expected);
        response.Headers.GetValues(StatusPreservingViewResultExecutor.PageComponentHeader)
            .Should().ContainSingle().Which.Should().Be("Errors/Status");
        response.Headers.GetValues(StatusPreservingViewResultExecutor.PageUrlHeader)
            .Should().ContainSingle().Which.Should().Be(path);
        response.Headers.Contains(HeaderNames.Inertia).Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().Be("Rendered Errors/Status");
    }

    [Theory]
    [InlineData("/error-status/mvc/missing", HttpStatusCode.NotFound)]
    [InlineData("/minimal/forbidden", HttpStatusCode.Forbidden)]
    public async Task WithStatus_ShouldApplyToInertiaJsonResponses(string path, HttpStatusCode expected)
    {
        await using var app = await CreateAppAsync();

        using var request = CreateInertiaRequest(HttpMethod.Get, path);
        using var response = await app.Client.SendAsync(request);
        var page = await response.AssertInertiaAsync(expected);

        page.HasComponent("Errors/Status").HasUrl(path).HasProp("status", (int)expected);
        response.Headers.Vary.Should().Contain(HeaderNames.Inertia);
    }

    [Fact]
    public async Task OrdinaryPages_ShouldStillReturn200()
    {
        await using var app = await CreateAppAsync();

        using var htmlResponse = await app.Client.GetAsync("/dashboard");
        htmlResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var request = CreateInertiaRequest(HttpMethod.Get, "/dashboard");
        using var jsonResponse = await app.Client.SendAsync(request);
        (await jsonResponse.AssertInertiaAsync()).HasComponent("Dashboard");
    }

    [Fact]
    public async Task VersionConflicts_ShouldStillReturn409()
    {
        await using var app = await CreateAppAsync();

        using var request = CreateInertiaRequest(HttpMethod.Get, "/minimal/forbidden");
        request.Headers.Remove(HeaderNames.Version);
        request.Headers.Add(HeaderNames.Version, "stale-version");
        using var response = await app.Client.SendAsync(request);

        response.AssertVersionRedirect().To("/minimal/forbidden");
    }

    [Fact]
    public async Task ExternalRedirects_ShouldNotBeReplacedByStatusCodePages()
    {
        await using var app = await CreateAppAsync();

        using var request = CreateInertiaRequest(HttpMethod.Post, "/redirect/external");
        using var response = await app.Client.SendAsync(request);

        response.AssertInertiaRedirect().To("https://example.com");
        response.Headers.Contains(HeaderNames.Inertia).Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task FragmentRedirects_ShouldNotBeReplacedByStatusCodePages()
    {
        await using var app = await CreateAppAsync();

        using var request = CreateInertiaRequest(HttpMethod.Post, "/redirect/fragment");
        using var response = await app.Client.SendAsync(request);

        response.AssertFragmentRedirect().To("/dashboard#summary");
        response.Headers.Contains(HeaderNames.Inertia).Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Redirects_ShouldKeepTheirStatus()
    {
        await using var app = await CreateAppAsync();

        using var request = CreateInertiaRequest(HttpMethod.Post, "/redirect/internal");
        using var response = await app.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.SeeOther);
        response.Headers.Location!.OriginalString.Should().Be("/dashboard");
    }

    [Fact]
    public async Task AssertInertiaAsync_ShouldRejectAnErrorStatus_WhenNoStatusIsExpected()
    {
        await using var app = await CreateAppAsync();

        using var request = CreateInertiaRequest(HttpMethod.Get, "/minimal/forbidden");
        using var response = await app.Client.SendAsync(request);

        var act = () => response.AssertInertiaAsync();
        (await act.Should().ThrowAsync<AssertionException>()).WithMessage("Expected status 200, got 403.");
    }

    // ── Status code pages (re-execution) ─────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StatusCodePagesReExecution_ShouldRenderTheOriginalStatusAndUrl_ForInitialVisits(bool explicitStatus)
    {
        await using var app = await CreateAppAsync(explicitStatus);

        using var response = await app.Client.GetAsync("/does-not-exist?page=2");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.GetValues(StatusPreservingViewResultExecutor.PageComponentHeader)
            .Should().ContainSingle().Which.Should().Be("Errors/Status");
        response.Headers.GetValues(StatusPreservingViewResultExecutor.PageUrlHeader)
            .Should().ContainSingle().Which.Should().Be("/does-not-exist?page=2");
    }

    [Theory]
    [InlineData("GET", true)]
    [InlineData("POST", true)]
    [InlineData("GET", false)]
    [InlineData("POST", false)]
    public async Task StatusCodePagesReExecution_ShouldRenderTheOriginalStatusAndUrl_ForInertiaVisits(string method, bool explicitStatus)
    {
        await using var app = await CreateAppAsync(explicitStatus);

        using var request = CreateInertiaRequest(new HttpMethod(method), "/does-not-exist?page=2");
        using var response = await app.Client.SendAsync(request);
        var page = await response.AssertInertiaAsync(HttpStatusCode.NotFound);

        page.HasComponent("Errors/Status").HasUrl("/does-not-exist?page=2").HasProp("status", 404);
    }

    // ── Exception handler ────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExceptionHandler_ShouldRender500_ForInitialVisits(bool explicitStatus)
    {
        await using var app = await CreateAppAsync(explicitStatus);

        using var response = await app.Client.GetAsync("/minimal/throws?id=7");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Headers.GetValues(StatusPreservingViewResultExecutor.PageComponentHeader)
            .Should().ContainSingle().Which.Should().Be("Errors/Status");
        response.Headers.GetValues(StatusPreservingViewResultExecutor.PageUrlHeader)
            .Should().ContainSingle().Which.Should().Be("/minimal/throws?id=7");
    }

    [Theory]
    [InlineData("GET", true)]
    [InlineData("POST", true)]
    [InlineData("GET", false)]
    [InlineData("POST", false)]
    public async Task ExceptionHandler_ShouldRender500_ForInertiaVisits(string method, bool explicitStatus)
    {
        await using var app = await CreateAppAsync(explicitStatus);

        using var request = CreateInertiaRequest(new HttpMethod(method), "/minimal/throws?id=7");
        using var response = await app.Client.SendAsync(request);
        var page = await response.AssertInertiaAsync(HttpStatusCode.InternalServerError);

        page.HasComponent("Errors/Status").HasUrl("/minimal/throws?id=7").HasProp("status", 500);
    }

    // ── HandleExceptionsUsing ────────────────────────────────────────────────

    [Fact]
    public async Task HandleExceptionsUsing_ShouldApplyTheHandlerResultStatus()
    {
        await using var app = await CreateAppAsync();

        using var htmlResponse = await app.Client.GetAsync("/minimal/render-failure");
        htmlResponse.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        htmlResponse.Headers.GetValues(StatusPreservingViewResultExecutor.PageComponentHeader)
            .Should().ContainSingle().Which.Should().Be("Errors/Render");

        using var request = CreateInertiaRequest(HttpMethod.Get, "/minimal/render-failure");
        using var jsonResponse = await app.Client.SendAsync(request);
        (await jsonResponse.AssertInertiaAsync(HttpStatusCode.InternalServerError)).HasComponent("Errors/Render");
    }

    /// <param name="explicitErrorStatus">
    /// When false the error endpoint renders without <c>WithStatus</c> and relies on the status the
    /// exception handler or status code pages middleware left on the response.
    /// </param>
    private static async Task<TestApp> CreateAppAsync(bool explicitErrorStatus = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ErrorStatusProbeController).Assembly);
        builder.Services.Replace(ServiceDescriptor.Singleton<IActionResultExecutor<ViewResult>, StatusPreservingViewResultExecutor>());
        builder.Services.AddInertia(options =>
        {
            options.RootView = "App";
            options.Version = () => "server-version";
            options.HandleExceptionsUsing = (exception, context) =>
                exception is not ErrorStatusProbeException
                    ? null
                    : context.RequestServices.GetRequiredService<IInertiaService>()
                        .Render("Errors/Render")
                        .WithStatus(StatusCodes.Status500InternalServerError);
        });

        var app = builder.Build();

        // The documented error-page pattern: both middlewares re-execute the pipeline on /error/{status}.
        app.UseExceptionHandler("/error/500");
        app.UseStatusCodePagesWithReExecute("/error/{0}");
        app.UseInertia();

        app.MapControllers();
        app.Map("/error/{status:int}", (int status, IInertiaService inertia) =>
        {
            var page = inertia.Render("Errors/Status", new { status });
            return explicitErrorStatus ? page.WithStatus(status) : page;
        });
        app.MapGet("/dashboard", (IInertiaService inertia) => inertia.Render("Dashboard"));
        app.MapGet("/minimal/forbidden", (IInertiaService inertia) =>
            inertia.Render("Errors/Status", new { status = StatusCodes.Status403Forbidden })
                .WithStatus(StatusCodes.Status403Forbidden));
        app.Map("/minimal/throws", IResult () => throw new InvalidOperationException("Boom"));
        app.MapPost("/redirect/external", (IInertiaService inertia) => inertia.Location("https://example.com"));
        app.MapPost("/redirect/fragment", () => Results.Redirect("/dashboard#summary"));
        app.MapPost("/redirect/internal", () => Results.Redirect("/dashboard"));
        app.MapGet("/minimal/render-failure", (IInertiaService inertia) => inertia.Render("Dashboard", new Dictionary<string, object?>
        {
            ["report"] = inertia.Always((_, _) => throw new ErrorStatusProbeException()),
        }));

        await app.StartAsync();

        return new TestApp(app, app.GetTestClient());
    }

    private static HttpRequestMessage CreateInertiaRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add(HeaderNames.Inertia, "true");
        request.Headers.Add(HeaderNames.Version, "server-version");
        return request;
    }

    private sealed record TestApp(WebApplication App, HttpClient Client) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => App.DisposeAsync();
    }
}
