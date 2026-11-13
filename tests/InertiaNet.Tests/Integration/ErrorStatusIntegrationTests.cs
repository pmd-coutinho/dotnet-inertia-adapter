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
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
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

/// <summary>
/// Resolves every view to <see cref="StubRootView"/>, so the framework's own <c>ViewResultExecutor</c>
/// and <c>ViewExecutor</c> run without compiled Razor views.
/// </summary>
internal sealed class StubRootViewEngine : IViewEngine
{
    public ViewEngineResult FindView(ActionContext context, string viewName, bool isMainPage)
        => ViewEngineResult.Found(viewName, new StubRootView(viewName));

    public ViewEngineResult GetView(string? executingFilePath, string viewPath, bool isMainPage)
        => ViewEngineResult.Found(viewPath, new StubRootView(viewPath));
}

internal sealed class StubRootView(string path) : IView
{
    public string Path { get; } = path;

    public Task RenderAsync(ViewContext context)
    {
        var page = context.ViewData.Model as InertiaPage;
        return context.Writer.WriteAsync($"{page?.Component}|{page?.Url}");
    }
}

/// <summary>
/// Handled by the test app's <c>HandleExceptionsUsing</c>; a null status renders without <c>WithStatus</c>,
/// and a response status is assigned to <c>context.Response.StatusCode</c> by the handler first.
/// </summary>
public sealed class ErrorStatusProbeException(int? status = StatusCodes.Status500InternalServerError, int? responseStatus = null) : Exception
{
    public int? Status { get; } = status;

    public int? ResponseStatus { get; } = responseStatus;
}

public sealed record ErrorStatusProps(int Status);

public sealed class ErrorStatusThrowingValue
{
    public string Value => throw new ErrorStatusProbeException();
}

[Route("error-status/mvc")]
public sealed class ErrorStatusProbeController : Controller
{
    [HttpGet("missing")]
    public IActionResult Missing()
        => this.Inertia("Errors/Status", new { status = StatusCodes.Status404NotFound })
            .WithStatus(StatusCodes.Status404NotFound);

    [HttpGet("render-failure")]
    public IActionResult RenderFailure([FromServices] IInertiaService inertia)
        => this.Inertia("Dashboard", new Dictionary<string, object?>
        {
            ["report"] = inertia.Always((_, _) => throw new ErrorStatusProbeException(status: null)),
        });
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

    // ── InertiaResults status overloads ──────────────────────────────────────

    public static TheoryData<string> StatusOverloads => ["object", "dictionary", "factory", "async-factory", "typed"];

    [Theory]
    [MemberData(nameof(StatusOverloads))]
    public async Task InertiaResultsStatusOverloads_ShouldApplyToInitialHtmlResponses(string overload)
    {
        await using var app = await CreateAppAsync();

        using var response = await app.Client.GetAsync($"/results/status/{overload}");

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        response.Headers.GetValues(StatusPreservingViewResultExecutor.PageComponentHeader)
            .Should().ContainSingle().Which.Should().Be("Errors/Status");
    }

    [Theory]
    [MemberData(nameof(StatusOverloads))]
    public async Task InertiaResultsStatusOverloads_ShouldApplyToInertiaJsonResponses(string overload)
    {
        await using var app = await CreateAppAsync();

        using var request = CreateInertiaRequest(HttpMethod.Get, $"/results/status/{overload}");
        using var response = await app.Client.SendAsync(request);
        var page = await response.AssertInertiaAsync(HttpStatusCode.Gone);

        page.HasComponent("Errors/Status").HasProp("status", StatusCodes.Status410Gone);
    }

    [Theory]
    [InlineData(StatusCodes.Status204NoContent)]
    [InlineData(StatusCodes.Status302Found)]
    [InlineData(StatusCodes.Status100Continue)]
    public void InertiaResultsStatusOverloads_ShouldRejectStatusesThatCannotCarryAPage_WhenCreated(int statusCode)
    {
        var component = new InertiaComponent<ErrorStatusProps>("Errors/Status");
        var creations = new Func<IResult>[]
        {
            () => InertiaResults.Inertia("Errors/Status", new { status = statusCode }, statusCode),
            () => InertiaResults.Inertia("Errors/Status", new Dictionary<string, object?>(), statusCode),
            () => InertiaResults.Inertia("Errors/Status", _ => null, statusCode),
            () => InertiaResults.Inertia("Errors/Status", (_, _) => Task.FromResult<object?>(null), statusCode),
            () => InertiaResults.Inertia(component, new ErrorStatusProps(statusCode), statusCode),
        };

        foreach (var create in creations)
            create.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("statusCode");
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

    [Theory]
    [InlineData("/minimal/render-failure/default")]
    [InlineData("/error-status/mvc/render-failure")]
    [InlineData("/minimal/render-failure/on-404")]
    public async Task HandleExceptionsUsing_ShouldDefaultTo500_WithoutAnExplicitStatus(string path)
    {
        await using var app = await CreateAppAsync();

        using var htmlResponse = await app.Client.GetAsync(path);
        htmlResponse.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        htmlResponse.Headers.GetValues(StatusPreservingViewResultExecutor.PageComponentHeader)
            .Should().ContainSingle().Which.Should().Be("Errors/Render");
        htmlResponse.Headers.GetValues(StatusPreservingViewResultExecutor.PageUrlHeader)
            .Should().ContainSingle().Which.Should().Be(path);

        using var request = CreateInertiaRequest(HttpMethod.Get, path);
        using var jsonResponse = await app.Client.SendAsync(request);
        (await jsonResponse.AssertInertiaAsync(HttpStatusCode.InternalServerError)).HasComponent("Errors/Render").HasUrl(path);
    }

    [Theory]
    [InlineData("/minimal/render-failure/handler-sets-404", HttpStatusCode.NotFound)]
    [InlineData("/minimal/render-failure/on-404/handler-sets-404", HttpStatusCode.InternalServerError)]
    [InlineData("/minimal/render-failure/handler-sets-404/explicit", HttpStatusCode.ServiceUnavailable)]
    public async Task HandleExceptionsUsing_ShouldKeepAStatusTheHandlerAssigns(string path, HttpStatusCode expected)
    {
        await using var app = await CreateAppAsync();

        using var htmlResponse = await app.Client.GetAsync(path);
        htmlResponse.StatusCode.Should().Be(expected);
        htmlResponse.Headers.GetValues(StatusPreservingViewResultExecutor.PageComponentHeader)
            .Should().ContainSingle().Which.Should().Be("Errors/Render");

        using var request = CreateInertiaRequest(HttpMethod.Get, path);
        using var jsonResponse = await app.Client.SendAsync(request);
        (await jsonResponse.AssertInertiaAsync(expected)).HasComponent("Errors/Render").HasUrl(path);
    }

    [Fact]
    public async Task HandleExceptionsUsing_ShouldPreferAnExplicitStatus()
    {
        await using var app = await CreateAppAsync();

        using var htmlResponse = await app.Client.GetAsync("/minimal/render-failure/explicit");
        htmlResponse.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        using var request = CreateInertiaRequest(HttpMethod.Get, "/minimal/render-failure/explicit");
        using var jsonResponse = await app.Client.SendAsync(request);
        (await jsonResponse.AssertInertiaAsync(HttpStatusCode.ServiceUnavailable)).HasComponent("Errors/Render");
    }

    [Fact]
    public async Task HandleExceptionsUsing_ShouldNotKeepThePrefetchCacheHeader_WhenSerializationFails()
    {
        await using var app = await CreateAppAsync();

        using var request = CreatePrefetchRequest("/minimal/serialization-failure");
        using var response = await app.Client.SendAsync(request);

        (await response.AssertInertiaAsync(HttpStatusCode.InternalServerError)).HasComponent("Errors/Render");
        (response.Headers.CacheControl?.MaxAge).Should().BeNull();
    }

    // ── Errors raised before the Inertia middleware ──────────────────────────

    [Fact]
    public async Task PreInertiaErrors_ShouldRenderTheOriginalStatusAndUrl()
    {
        await using var app = await CreateAppAsync();

        using var request = CreateInertiaRequest(HttpMethod.Get, "/admin/users?tab=2");
        using var response = await app.Client.SendAsync(request);
        var page = await response.AssertInertiaAsync(HttpStatusCode.Forbidden);

        page.HasComponent("Errors/Status").HasUrl("/admin/users?tab=2").HasProp("status", 403);
    }

    [Theory]
    [InlineData("/admin/users?tab=2")]
    [InlineData("/pre-inertia/throws?id=3")]
    public async Task VersionConflicts_DuringErrorReExecution_ShouldPointAtTheOriginalUrl(string path)
    {
        await using var app = await CreateAppAsync();

        using var request = CreateInertiaRequest(HttpMethod.Get, path);
        request.Headers.Remove(HeaderNames.Version);
        request.Headers.Add(HeaderNames.Version, "stale-version");
        using var response = await app.Client.SendAsync(request);

        response.AssertVersionRedirect().To(path);
        response.Headers.Contains(HeaderNames.Inertia).Should().BeFalse();
    }

    // ── Error route ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/error/302")]
    [InlineData("/error/200")]
    [InlineData("/error/999")]
    public async Task ErrorRoute_ShouldNotRenderStatusesOutsideTheErrorRange(string path)
    {
        await using var app = await CreateAppAsync();

        using var request = CreateInertiaRequest(HttpMethod.Get, path);
        using var response = await app.Client.SendAsync(request);
        var page = await response.AssertInertiaAsync(HttpStatusCode.NotFound);

        page.HasComponent("Errors/Status").HasUrl(path).HasProp("status", 404);
    }

    // ── Prefetch ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/does-not-exist", HttpStatusCode.NotFound)]
    [InlineData("/minimal/forbidden", HttpStatusCode.Forbidden)]
    [InlineData("/minimal/throws", HttpStatusCode.InternalServerError)]
    public async Task PrefetchedErrorPages_ShouldNotBeCacheable(string path, HttpStatusCode expected)
    {
        await using var app = await CreateAppAsync();

        using var request = CreatePrefetchRequest(path);
        using var response = await app.Client.SendAsync(request);

        await response.AssertInertiaAsync(expected);
        (response.Headers.CacheControl?.MaxAge).Should().BeNull();
    }

    [Fact]
    public async Task PrefetchedPages_ShouldStillBeCacheable()
    {
        await using var app = await CreateAppAsync();

        using var request = CreatePrefetchRequest("/dashboard");
        using var response = await app.Client.SendAsync(request);

        await response.AssertInertiaAsync();
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.Private.Should().BeTrue();
        response.Headers.CacheControl!.MaxAge.Should().Be(TimeSpan.FromSeconds(10));
    }

    // ── Framework view executor ──────────────────────────────────────────────

    [Theory]
    [InlineData("/error-status/mvc/missing", HttpStatusCode.NotFound, "/error-status/mvc/missing")]
    [InlineData("/minimal/forbidden", HttpStatusCode.Forbidden, "/minimal/forbidden")]
    [InlineData("/dashboard", HttpStatusCode.OK, "/dashboard")]
    [InlineData("/does-not-exist?page=2", HttpStatusCode.NotFound, "/does-not-exist?page=2")]
    [InlineData("/minimal/throws?id=7", HttpStatusCode.InternalServerError, "/minimal/throws?id=7")]
    public async Task FrameworkViewExecutor_ShouldKeepTheResponseStatus(string path, HttpStatusCode expected, string url)
    {
        await using var app = await CreateAppAsync(explicitErrorStatus: false, frameworkViewExecutor: true);

        using var response = await app.Client.GetAsync(path);

        response.StatusCode.Should().Be(expected);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync()).Should().EndWith($"|{url}");
    }

    /// <param name="explicitErrorStatus">
    /// When false the error endpoint renders without <c>WithStatus</c> and relies on the status the
    /// exception handler or status code pages middleware left on the response.
    /// </param>
    /// <param name="frameworkViewExecutor">
    /// When true the framework's view executor renders <see cref="StubRootView"/> instead of
    /// <see cref="StatusPreservingViewResultExecutor"/> handling the view result.
    /// </param>
    private static async Task<TestApp> CreateAppAsync(bool explicitErrorStatus = true, bool frameworkViewExecutor = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ErrorStatusProbeController).Assembly);
        if (frameworkViewExecutor)
        {
            builder.Services.Configure<MvcViewOptions>(options =>
            {
                options.ViewEngines.Clear();
                options.ViewEngines.Add(new StubRootViewEngine());
            });
        }
        else
        {
            builder.Services.Replace(ServiceDescriptor.Singleton<IActionResultExecutor<ViewResult>, StatusPreservingViewResultExecutor>());
        }
        builder.Services.AddInertia(options =>
        {
            options.RootView = "App";
            options.Version = () => "server-version";
            options.HandleExceptionsUsing = (exception, context) =>
            {
                if (exception is not ErrorStatusProbeException probe) return null;
                if (probe.ResponseStatus is { } responseStatus) context.Response.StatusCode = responseStatus;
                var page = context.RequestServices.GetRequiredService<IInertiaService>().Render("Errors/Render");
                return probe.Status is { } status ? page.WithStatus(status) : page;
            };
        });

        var app = builder.Build();

        // The documented error-page pattern: both middlewares re-execute the pipeline on /error/{status}.
        app.UseExceptionHandler("/error/500");
        app.UseStatusCodePagesWithReExecute("/error/{0}");

        // Errors produced before the Inertia middleware, such as authorization or rate limiting.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/admin"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (context.Request.Path.StartsWithSegments("/pre-inertia/throws"))
                throw new InvalidOperationException("Boom");

            await next();
        });

        app.UseInertia();

        app.MapControllers();
        app.Map("/error/{status:int:range(400,599)}", (int status, IInertiaService inertia) =>
        {
            var page = inertia.Render("Errors/Status", new { status });
            return explicitErrorStatus ? page.WithStatus(status) : page;
        });
        app.MapGet("/dashboard", (IInertiaService inertia) => inertia.Render("Dashboard"));
        const int gone = StatusCodes.Status410Gone;
        app.MapGet("/results/status/object", () => InertiaResults.Inertia("Errors/Status", new { status = gone }, gone));
        app.MapGet("/results/status/dictionary", () => InertiaResults.Inertia("Errors/Status",
            new Dictionary<string, object?> { ["status"] = gone }, gone));
        app.MapGet("/results/status/factory", () => InertiaResults.Inertia("Errors/Status",
            _ => new { status = gone }, gone));
        app.MapGet("/results/status/async-factory", () => InertiaResults.Inertia("Errors/Status",
            (_, _) => Task.FromResult<object?>(new { status = gone }), gone));
        app.MapGet("/results/status/typed", () => InertiaResults.Inertia(
            new InertiaComponent<ErrorStatusProps>("Errors/Status"), new ErrorStatusProps(gone), gone));
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
        app.MapGet("/minimal/render-failure/default", (IInertiaService inertia) => inertia.Render("Dashboard", new Dictionary<string, object?>
        {
            ["report"] = inertia.Always((_, _) => throw new ErrorStatusProbeException(status: null)),
        }));
        app.MapGet("/minimal/render-failure/explicit", (IInertiaService inertia) => inertia.Render("Dashboard", new Dictionary<string, object?>
        {
            ["report"] = inertia.Always((_, _) => throw new ErrorStatusProbeException(StatusCodes.Status503ServiceUnavailable)),
        }));
        // A render that fails while the response already carries 404 still reports the failure as a 500.
        app.MapGet("/minimal/render-failure/on-404", (HttpContext context, IInertiaService inertia) =>
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return inertia.Render("Errors/Status", new Dictionary<string, object?>
            {
                ["report"] = inertia.Always((_, _) => throw new ErrorStatusProbeException(status: null)),
            });
        });
        // The handler assigns a status itself, without WithStatus.
        app.MapGet("/minimal/render-failure/handler-sets-404", (IInertiaService inertia) => inertia.Render("Dashboard", new Dictionary<string, object?>
        {
            ["report"] = inertia.Always((_, _) => throw new ErrorStatusProbeException(status: null, StatusCodes.Status404NotFound)),
        }));
        // The handler assigns the status the response already had, which cannot be told apart from leaving it.
        app.MapGet("/minimal/render-failure/on-404/handler-sets-404", (HttpContext context, IInertiaService inertia) =>
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return inertia.Render("Errors/Status", new Dictionary<string, object?>
            {
                ["report"] = inertia.Always((_, _) => throw new ErrorStatusProbeException(status: null, StatusCodes.Status404NotFound)),
            });
        });
        // An explicit status wins over the one the handler assigns.
        app.MapGet("/minimal/render-failure/handler-sets-404/explicit", (IInertiaService inertia) => inertia.Render("Dashboard", new Dictionary<string, object?>
        {
            ["report"] = inertia.Always((_, _) => throw new ErrorStatusProbeException(StatusCodes.Status503ServiceUnavailable, StatusCodes.Status404NotFound)),
        }));
        app.MapGet("/minimal/serialization-failure", (IInertiaService inertia) => inertia.Render("Dashboard", new Dictionary<string, object?>
        {
            ["report"] = new ErrorStatusThrowingValue(),
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

    private static HttpRequestMessage CreatePrefetchRequest(string uri)
    {
        var request = CreateInertiaRequest(HttpMethod.Get, uri);
        request.Headers.Add(HeaderNames.Purpose, "prefetch");
        return request;
    }

    private sealed record TestApp(WebApplication App, HttpClient Client) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => App.DisposeAsync();
    }
}
