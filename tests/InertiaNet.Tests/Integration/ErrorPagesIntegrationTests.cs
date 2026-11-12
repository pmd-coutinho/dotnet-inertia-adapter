using System.Net;
using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Support;
using InertiaNet.Testing;
using System.Security.Claims;
using InertiaNet.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace InertiaNet.Tests.Integration;

[Route("error-pages/mvc")]
public sealed class ErrorPagesProbeController : Controller
{
    [HttpGet("missing")]
    public IActionResult Missing() => NotFound();

    [HttpPost("throws")]
    public IActionResult Throws() => throw new InvalidOperationException("Boom");
}

public class ErrorPagesIntegrationTests
{
    // ── Status code pages ────────────────────────────────────────────────────

    [Fact]
    public async Task UnmatchedRoutes_ShouldRenderTheErrorPage_ForInitialVisits()
    {
        await using var app = await CreateAppAsync();

        using var response = await app.Client.GetAsync("/does-not-exist?page=2");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.GetValues(StatusPreservingViewResultExecutor.PageComponentHeader)
            .Should().ContainSingle().Which.Should().Be("ErrorPage");
        response.Headers.GetValues(StatusPreservingViewResultExecutor.PageUrlHeader)
            .Should().ContainSingle().Which.Should().Be("/does-not-exist?page=2");
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task UnmatchedRoutes_ShouldRenderTheErrorPage_ForInertiaVisits(string method)
    {
        await using var app = await CreateAppAsync();

        using var request = Inertia(new HttpMethod(method), "/does-not-exist?page=2");
        using var response = await app.Client.SendAsync(request);
        var page = await response.AssertInertiaAsync(HttpStatusCode.NotFound);

        page.HasComponent("ErrorPage").HasUrl("/does-not-exist?page=2").HasProp("status", 404);
    }

    [Fact]
    public async Task EmptyErrorResponses_ShouldRenderTheErrorPageWithTheirStatus()
    {
        await using var app = await CreateAppAsync();

        using var request = Inertia(HttpMethod.Get, "/forbidden");
        using var response = await app.Client.SendAsync(request);
        var page = await response.AssertInertiaAsync(HttpStatusCode.Forbidden);

        page.HasComponent("ErrorPage").HasUrl("/forbidden").HasProp("status", 403);
    }

    [Theory]
    [InlineData("/error/302")]
    [InlineData("/error/200")]
    [InlineData("/error/999")]
    public async Task ErrorRoute_ShouldNotRenderStatusesOutsideTheErrorRange(string path)
    {
        await using var app = await CreateAppAsync();

        using var request = Inertia(HttpMethod.Get, path);
        using var response = await app.Client.SendAsync(request);
        var page = await response.AssertInertiaAsync(HttpStatusCode.NotFound);

        page.HasComponent("ErrorPage").HasUrl(path).HasProp("status", 404);
    }

    [Fact]
    public async Task InertiaProtocolResponses_ShouldNotBeReplaced()
    {
        await using var app = await CreateAppAsync();

        using var request = Inertia(HttpMethod.Get, "/dashboard");
        request.Headers.Remove(HeaderNames.Version);
        request.Headers.Add(HeaderNames.Version, "stale-version");
        using var response = await app.Client.SendAsync(request);

        response.AssertVersionRedirect().To("/dashboard");
        response.Headers.Contains(HeaderNames.Inertia).Should().BeFalse();
    }

    [Fact]
    public async Task OrdinaryPages_ShouldBeUnchanged()
    {
        await using var app = await CreateAppAsync();

        using var request = Inertia(HttpMethod.Get, "/dashboard");
        using var response = await app.Client.SendAsync(request);

        (await response.AssertInertiaAsync()).HasComponent("Dashboard");
    }

    [Fact]
    public async Task WrongMethods_ShouldRenderA405ErrorPage()
    {
        await using var app = await CreateAppAsync();

        using var response = await app.Client.SendAsync(Inertia(HttpMethod.Post, "/dashboard"));

        (await response.AssertInertiaAsync(HttpStatusCode.MethodNotAllowed))
            .HasComponent("ErrorPage").HasUrl("/dashboard").HasProp("status", 405);
    }

    [Fact]
    public async Task ControllerErrors_ShouldRenderTheErrorPage()
    {
        await using var app = await CreateAppAsync();

        using var missing = await app.Client.SendAsync(Inertia(HttpMethod.Get, "/error-pages/mvc/missing"));
        (await missing.AssertInertiaAsync(HttpStatusCode.NotFound))
            .HasComponent("ErrorPage").HasUrl("/error-pages/mvc/missing").HasProp("status", 404);

        using var failed = await app.Client.SendAsync(Inertia(HttpMethod.Post, "/error-pages/mvc/throws"));
        (await failed.AssertInertiaAsync(HttpStatusCode.InternalServerError))
            .HasComponent("ErrorPage").HasUrl("/error-pages/mvc/throws").HasProp("status", 500);

        using var html = await app.Client.GetAsync("/error-pages/mvc/missing");
        html.StatusCode.Should().Be(HttpStatusCode.NotFound);
        html.Headers.GetValues(StatusPreservingViewResultExecutor.PageComponentHeader)
            .Should().ContainSingle().Which.Should().Be("ErrorPage");
    }

    [Theory]
    [InlineData("/does-not-exist?page=2", HttpStatusCode.NotFound)]
    [InlineData("/forbidden", HttpStatusCode.Forbidden)]
    [InlineData("/error-pages/mvc/missing", HttpStatusCode.NotFound)]
    [InlineData("/throws?id=7", HttpStatusCode.InternalServerError)]
    public async Task FrameworkViewExecutor_ShouldRenderTheErrorPageWithItsStatus(string path, HttpStatusCode expected)
    {
        await using var app = await CreateAppAsync(frameworkViewExecutor: true);

        using var response = await app.Client.GetAsync(path);

        response.StatusCode.Should().Be(expected);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync()).Should().EndWith($"ErrorPage|{path}");
    }

    // ── Exceptions ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Exceptions_ShouldRenderA500ErrorPage_ForInitialVisits()
    {
        await using var app = await CreateAppAsync();

        using var response = await app.Client.GetAsync("/throws?id=7");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Headers.GetValues(StatusPreservingViewResultExecutor.PageComponentHeader)
            .Should().ContainSingle().Which.Should().Be("ErrorPage");
        response.Headers.GetValues(StatusPreservingViewResultExecutor.PageUrlHeader)
            .Should().ContainSingle().Which.Should().Be("/throws?id=7");
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Exceptions_ShouldRenderA500ErrorPage_ForInertiaVisits(string method)
    {
        await using var app = await CreateAppAsync();

        using var request = Inertia(new HttpMethod(method), "/throws?id=7");
        using var response = await app.Client.SendAsync(request);
        var page = await response.AssertInertiaAsync(HttpStatusCode.InternalServerError);

        page.HasComponent("ErrorPage").HasUrl("/throws?id=7").HasProp("status", 500);
    }

    [Fact]
    public async Task Exceptions_ShouldPropagate_WhenExceptionHandlingIsOff()
    {
        await using var app = await CreateAppAsync(options => options.HandleExceptions = false);

        var act = () => app.Client.GetAsync("/throws");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Boom");
    }

    [Fact]
    public async Task Exceptions_ShouldNotBeHandled_InDevelopmentByDefault()
    {
        await using var app = await CreateAppAsync(environment: Environments.Development);

        using var response = await app.Client.GetAsync("/throws");

        // The developer exception page, not the Inertia error page.
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Headers.Contains(StatusPreservingViewResultExecutor.PageComponentHeader).Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().Contain("Boom");
    }

    [Fact]
    public async Task StatusCodePages_ShouldStillRender_InDevelopment()
    {
        await using var app = await CreateAppAsync(environment: Environments.Development);

        using var request = Inertia(HttpMethod.Get, "/does-not-exist");
        using var response = await app.Client.SendAsync(request);

        (await response.AssertInertiaAsync(HttpStatusCode.NotFound)).HasComponent("ErrorPage");
    }

    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    public async Task ErrorPages_ShouldWork_WithAnExplicitUseRouting(string useRouting)
    {
        await using var app = await CreateAppAsync(useRouting: useRouting);

        using var missing = await app.Client.SendAsync(Inertia(HttpMethod.Get, "/does-not-exist"));
        (await missing.AssertInertiaAsync(HttpStatusCode.NotFound)).HasComponent("ErrorPage").HasUrl("/does-not-exist");

        using var failed = await app.Client.SendAsync(Inertia(HttpMethod.Post, "/throws"));
        (await failed.AssertInertiaAsync(HttpStatusCode.InternalServerError)).HasComponent("ErrorPage").HasUrl("/throws");
    }

    [Fact]
    public async Task ChallengeLocationVisits_ShouldNotBeReplaced_WhileForbiddenVisitsGetTheErrorPage()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Services.AddControllersWithViews();
        builder.Services.Replace(ServiceDescriptor.Singleton<IActionResultExecutor<ViewResult>, StatusPreservingViewResultExecutor>());
        builder.Services.AddInertia(options => options.RootView = "App");
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/account/login";
                options.AccessDeniedPath = "/account/denied";
            })
            .AddInertiaChallengeHandling();
        builder.Services.AddAuthorization(options => options.AddPolicy("Admin", policy => policy.RequireRole("admin")));
        await using var app = builder.Build();
        app.UseInertiaErrorPages();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseInertia();
        app.MapGet("/protected", () => InertiaResults.Inertia("Dashboard")).RequireAuthorization();
        app.MapGet("/admin", () => InertiaResults.Inertia("Admin")).RequireAuthorization("Admin");
        app.MapPost("/sign-in", (HttpContext context) => context.SignInAsync(new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], CookieAuthenticationDefaults.AuthenticationScheme))));
        await app.StartAsync();
        var client = app.GetTestClient();

        // Unauthenticated: the challenge becomes a 409 location visit, which status code pages must leave alone.
        using var challenge = await client.SendAsync(Inertia(HttpMethod.Get, "/protected"));
        challenge.StatusCode.Should().Be(HttpStatusCode.Conflict);
        challenge.Headers.GetValues(HeaderNames.Location).Should().ContainSingle()
            .Which.Should().StartWith("http://localhost/account/login");
        challenge.Headers.Contains(HeaderNames.Inertia).Should().BeFalse();
        (await challenge.Content.ReadAsStringAsync()).Should().BeEmpty();

        // Authenticated but not authorized: the forbid is a bare 403, which the helper renders as the error page.
        using var signIn = await client.PostAsync("/sign-in", null);
        using var forbidden = Inertia(HttpMethod.Get, "/admin");
        forbidden.Headers.Add("Cookie", signIn.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
        using var forbiddenResponse = await client.SendAsync(forbidden);
        (await forbiddenResponse.AssertInertiaAsync(HttpStatusCode.Forbidden))
            .HasComponent("ErrorPage").HasUrl("/admin").HasProp("status", 403);
    }

    [Fact]
    public async Task ErrorPages_ShouldAllowAnonymousUsers_UnderAFallbackPolicy()
    {
        await using var app = await CreateFallbackPolicyAppAsync();

        using var inertia = await app.Client.SendAsync(Inertia(HttpMethod.Get, "/public/missing"));
        (await inertia.AssertInertiaAsync(HttpStatusCode.NotFound))
            .HasComponent("ErrorPage").HasUrl("/public/missing").HasProp("status", 404);
        inertia.Headers.Contains(HeaderNames.Location).Should().BeFalse();

        using var html = await app.Client.GetAsync("/public/missing");
        html.StatusCode.Should().Be(HttpStatusCode.NotFound);
        html.Headers.Location.Should().BeNull();
        html.Headers.GetValues(StatusPreservingViewResultExecutor.PageComponentHeader)
            .Should().ContainSingle().Which.Should().Be("ErrorPage");

        using var badRequest = await app.Client.PostAsync("/public/bad-request", null);
        badRequest.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        badRequest.Headers.Location.Should().BeNull();
        badRequest.Headers.GetValues(StatusPreservingViewResultExecutor.PageUrlHeader)
            .Should().ContainSingle().Which.Should().Be("/public/bad-request");
    }

    [Fact]
    public async Task ErrorPages_ShouldBeAuthorized_WhenAllowAnonymousIsOff()
    {
        await using var app = await CreateFallbackPolicyAppAsync(options => options.AllowAnonymous = false);

        using var inertia = await app.Client.SendAsync(Inertia(HttpMethod.Get, "/public/missing"));
        inertia.StatusCode.Should().Be(HttpStatusCode.Conflict);
        inertia.Headers.GetValues(HeaderNames.Location).Should().ContainSingle()
            .Which.Should().EndWith("/login?ReturnUrl=%2Ferror%2F404");

        using var html = await app.Client.GetAsync("/public/missing");
        html.StatusCode.Should().Be(HttpStatusCode.Redirect);
        html.Headers.Location!.ToString().Should().EndWith("/login?ReturnUrl=%2Ferror%2F404");
    }

    private static async Task<TestApp> CreateFallbackPolicyAppAsync(Action<InertiaErrorPageOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Services.AddControllersWithViews();
        builder.Services.Replace(ServiceDescriptor.Singleton<IActionResultExecutor<ViewResult>, StatusPreservingViewResultExecutor>());
        builder.Services.AddInertia(options => options.RootView = "App");
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options => options.LoginPath = "/login")
            .AddInertiaChallengeHandling();
        builder.Services.AddAuthorization(options => options.FallbackPolicy =
            new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        var app = builder.Build();
        app.UseInertiaErrorPages(configure);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseInertia();
        app.MapGet("/public/missing", () => Results.NotFound()).AllowAnonymous();
        app.MapPost("/public/bad-request", () => Results.BadRequest()).AllowAnonymous();
        await app.StartAsync();
        return new TestApp(app, app.GetTestClient());
    }

    // ── Excluded requests ────────────────────────────────────────────────────

    [Theory]
    [InlineData("/does-not-exist", HttpStatusCode.NotFound)]
    [InlineData("/forbidden", HttpStatusCode.Forbidden)]
    public async Task JsonApiRequests_ShouldKeepPlainErrorResponses(string path, HttpStatusCode expected)
    {
        await using var app = await CreateAppAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Accept", "application/json");
        using var response = await app.Client.SendAsync(request);

        response.StatusCode.Should().Be(expected);
        response.Headers.Contains(StatusPreservingViewResultExecutor.PageComponentHeader).Should().BeFalse();
        response.Headers.Contains(HeaderNames.Inertia).Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task JsonApiRequests_ShouldNotHaveTheirExceptionsRendered()
    {
        await using var app = await CreateAppAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/throws");
        request.Headers.Add("Accept", "application/json");
        var act = () => app.Client.SendAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Boom");
    }

    [Fact]
    public async Task PrecognitionRequests_ShouldKeepPlainErrorResponses()
    {
        await using var app = await CreateAppAsync();

        using var request = Inertia(HttpMethod.Post, "/does-not-exist");
        request.Headers.Add(HeaderNames.Precognition, "true");
        using var response = await app.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.Contains(HeaderNames.Inertia).Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PathsExcludedByTheInertiaRequestPredicate_ShouldKeepPlainErrorResponses(bool inertiaVisit)
    {
        await using var app = await CreateAppAsync(inertiaPredicate: context => !context.Request.Path.StartsWithSegments("/api"));

        using var request = inertiaVisit ? Inertia(HttpMethod.Get, "/api/missing") : new HttpRequestMessage(HttpMethod.Get, "/api/missing");
        if (!inertiaVisit) request.Headers.Add("Accept", "text/html");
        using var response = await app.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.Contains(StatusPreservingViewResultExecutor.PageComponentHeader).Should().BeFalse();
        response.Headers.Contains(HeaderNames.Inertia).Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task PathsIncludedByTheInertiaRequestPredicate_ShouldStillRender()
    {
        await using var app = await CreateAppAsync(inertiaPredicate: context => !context.Request.Path.StartsWithSegments("/api"));

        using var request = Inertia(HttpMethod.Get, "/does-not-exist");
        using var response = await app.Client.SendAsync(request);

        (await response.AssertInertiaAsync(HttpStatusCode.NotFound)).HasComponent("ErrorPage").HasUrl("/does-not-exist");
    }

    [Fact]
    public async Task ACustomRequestPredicate_ShouldReplaceTheDefault()
    {
        await using var app = await CreateAppAsync(options =>
        {
            options.RequestPredicate = context => !context.Request.Query.ContainsKey("plain");
        });

        using var plain = await app.Client.SendAsync(Inertia(HttpMethod.Get, "/does-not-exist?plain=1"));
        plain.StatusCode.Should().Be(HttpStatusCode.NotFound);
        plain.Headers.Contains(HeaderNames.Inertia).Should().BeFalse();

        // The default would exclude a JSON request; this predicate renders it.
        using var json = new HttpRequestMessage(HttpMethod.Get, "/does-not-exist");
        json.Headers.Add("Accept", "application/json");
        using var rendered = await app.Client.SendAsync(json);
        rendered.StatusCode.Should().Be(HttpStatusCode.NotFound);
        rendered.Headers.GetValues(StatusPreservingViewResultExecutor.PageComponentHeader)
            .Should().ContainSingle().Which.Should().Be("ErrorPage");
    }

    [Fact]
    public async Task TheDefaultRequestPredicate_ShouldUseTheConfiguredChallengeApiPredicate()
    {
        await using var app = await CreateAppAsync(configureServices: services =>
            services.Configure<InertiaChallengeOptions>(options =>
                options.ApiRequestPredicate = context => context.Request.Path.StartsWithSegments("/hooks")));

        // The configured predicate classifies /hooks as an API path, even for a browser navigation.
        using var hook = new HttpRequestMessage(HttpMethod.Get, "/hooks/missing");
        hook.Headers.Add("Accept", "text/html");
        using var plain = await app.Client.SendAsync(hook);
        plain.StatusCode.Should().Be(HttpStatusCode.NotFound);
        plain.Headers.Contains(StatusPreservingViewResultExecutor.PageComponentHeader).Should().BeFalse();
        (await plain.Content.ReadAsStringAsync()).Should().BeEmpty();

        // It does not classify JSON requests as API requests, so they get the error page.
        using var json = new HttpRequestMessage(HttpMethod.Get, "/does-not-exist");
        json.Headers.Add("Accept", "application/json");
        using var rendered = await app.Client.SendAsync(json);
        rendered.StatusCode.Should().Be(HttpStatusCode.NotFound);
        rendered.Headers.GetValues(StatusPreservingViewResultExecutor.PageComponentHeader)
            .Should().ContainSingle().Which.Should().Be("ErrorPage");
    }

    [Fact]
    public async Task TheDefaultRequestPredicate_ShouldFallBackToTheDefaultApiPredicate_WhenTheChallengeOneIsNull()
    {
        await using var app = await CreateAppAsync(configureServices: services =>
            services.Configure<InertiaChallengeOptions>(options => options.ApiRequestPredicate = null));

        using var json = new HttpRequestMessage(HttpMethod.Get, "/does-not-exist");
        json.Headers.Add("Accept", "application/json");
        using var response = await app.Client.SendAsync(json);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.Contains(StatusPreservingViewResultExecutor.PageComponentHeader).Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    // ── Configuration ────────────────────────────────────────────────────────

    [Fact]
    public async Task ComponentAndPath_ShouldBeConfigurable()
    {
        await using var app = await CreateAppAsync(options =>
        {
            options.Component = "Errors/Status";
            options.Path = "/oops/";
        });

        using var missing = await app.Client.SendAsync(Inertia(HttpMethod.Get, "/does-not-exist"));
        (await missing.AssertInertiaAsync(HttpStatusCode.NotFound))
            .HasComponent("Errors/Status").HasUrl("/does-not-exist").HasProp("status", 404);

        using var failed = await app.Client.SendAsync(Inertia(HttpMethod.Post, "/throws"));
        (await failed.AssertInertiaAsync(HttpStatusCode.InternalServerError))
            .HasComponent("Errors/Status").HasProp("status", 500);

        using var direct = await app.Client.SendAsync(Inertia(HttpMethod.Get, "/oops/410"));
        (await direct.AssertInertiaAsync(HttpStatusCode.Gone)).HasComponent("Errors/Status").HasProp("status", 410);

        using var old = await app.Client.SendAsync(Inertia(HttpMethod.Get, "/error/404"));
        (await old.AssertInertiaAsync(HttpStatusCode.NotFound)).HasUrl("/error/404").HasComponent("Errors/Status");
    }

    [Fact]
    public async Task ConfigureEndpoint_ShouldReceiveTheErrorEndpoint()
    {
        var marker = new object();
        await using var app = await CreateAppAsync(options => options.ConfigureEndpoint = endpoint => endpoint.WithMetadata(marker));

        var endpoints = app.App.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints;

        var error = endpoints.OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Should().ContainSingle(e => e.RoutePattern.RawText == "/error/{status:int:range(400,599)}").Subject;
        error.Metadata.Should().Contain(marker);
        error.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Should().NotBeNull();
        error.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IExcludeFromDescriptionMetadata>()!.ExcludeFromDescription.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "/error")]
    [InlineData(" ", "/error")]
    [InlineData("ErrorPage", "")]
    [InlineData("ErrorPage", "error")]
    [InlineData("ErrorPage", "/error/{code}")]
    [InlineData("ErrorPage", "/error?x=1")]
    [InlineData("ErrorPage", "/")]
    [InlineData("ErrorPage", "//")]
    public void InvalidOptions_ShouldBeRejected(string component, string path)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();

        var act = () => app.UseInertiaErrorPages(options =>
        {
            options.Component = component;
            options.Path = path;
        });

        act.Should().Throw<ArgumentException>();
    }

    /// <param name="frameworkViewExecutor">
    /// When true the framework's view executor renders <see cref="StubRootView"/> instead of
    /// <see cref="StatusPreservingViewResultExecutor"/> handling the view result.
    /// </param>
    [Fact]
    public void ASecondCall_ShouldBeRejected()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.UseInertiaErrorPages();

        var act = () => app.UseInertiaErrorPages();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task AllowAnonymousOff_ShouldLeaveTheErrorEndpointWithoutAllowAnonymousMetadata()
    {
        await using var app = await CreateAppAsync(options => options.AllowAnonymous = false);

        var endpoints = app.App.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints;

        var error = endpoints.OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Should().ContainSingle(e => e.RoutePattern.RawText == "/error/{status:int:range(400,599)}").Subject;
        error.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Should().BeNull();
    }

    private static async Task<TestApp> CreateAppAsync(
        Action<InertiaErrorPageOptions>? configure = null,
        string environment = "Production",
        Func<HttpContext, bool>? inertiaPredicate = null,
        string? useRouting = null,
        Action<IServiceCollection>? configureServices = null,
        bool frameworkViewExecutor = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();

        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ErrorPagesProbeController).Assembly);
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
        configureServices?.Invoke(builder.Services);
        builder.Services.AddInertia(options =>
        {
            options.RootView = "App";
            options.Version = () => "server-version";
            options.RequestPredicate = inertiaPredicate;
        });

        var app = builder.Build();

        if (useRouting == "before") app.UseRouting();
        app.UseInertiaErrorPages(configure);
        if (useRouting == "after") app.UseRouting();
        app.UseInertia();

        app.MapGet("/dashboard", (IInertiaService inertia) => inertia.Render("Dashboard"));
        app.MapGet("/forbidden", () => Results.StatusCode(StatusCodes.Status403Forbidden));
        app.Map("/throws", IResult () => throw new InvalidOperationException("Boom"));
        app.MapControllers();

        await app.StartAsync();

        return new TestApp(app, app.GetTestClient());
    }

    private static HttpRequestMessage Inertia(HttpMethod method, string uri)
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
