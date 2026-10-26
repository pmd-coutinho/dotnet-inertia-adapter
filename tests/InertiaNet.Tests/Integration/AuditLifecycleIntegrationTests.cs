using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Support;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InertiaNet.Tests.Integration;

public class AuditLifecycleIntegrationTests
{
    [Fact]
    public async Task ExcludedRequests_LeaveCookieTempDataForTheInitialHtmlPage()
    {
        await using var app = await Fixture.Create();
        using var seed = await app.Send(HttpMethod.Post, "/seed");
        using var excluded = await app.Send(HttpMethod.Get, "/api/ping", inertia: true, version: "old");
        excluded.StatusCode.Should().Be(HttpStatusCode.OK);
        excluded.Headers.Vary.Should().BeEmpty();
        excluded.Headers.Contains("Set-Cookie").Should().BeFalse();

        // No X-Inertia header: the predicate must include initial HTML loads.
        using var initial = await app.Send(HttpMethod.Get, "/ui/page");
        using var page = JsonDocument.Parse(await initial.Content.ReadAsStringAsync());
        page.RootElement.GetProperty("props").GetProperty("errors").GetProperty("email").GetString().Should().Be("Required");
        page.RootElement.GetProperty("flash").GetProperty("saved").GetBoolean().Should().BeTrue();
        page.RootElement.GetProperty("clearHistory").GetBoolean().Should().BeTrue();
        page.RootElement.GetProperty("preserveFragment").GetBoolean().Should().BeTrue();
        page.RootElement.GetProperty("props").GetProperty("xsrfToken").GetString().Should().NotBeNullOrEmpty();
        using var next = await app.Send(HttpMethod.Get, "/ui/page", inertia: true);
        using var nextPage = JsonDocument.Parse(await next.Content.ReadAsStringAsync());
        nextPage.RootElement.GetProperty("props").GetProperty("errors").EnumerateObject().Should().BeEmpty();
        nextPage.RootElement.TryGetProperty("flash", out _).Should().BeFalse();
        nextPage.RootElement.TryGetProperty("clearHistory", out _).Should().BeFalse();
        nextPage.RootElement.TryGetProperty("preserveFragment", out _).Should().BeFalse();
    }

    [Fact]
    public async Task PrefetchAndVersionConflict_PreserveRealTempDataUntilNavigation()
    {
        await using var app = await Fixture.Create();
        using var seed = await app.Send(HttpMethod.Post, "/seed");
        using var prefetch = await app.Send(HttpMethod.Get, "/ui/page", inertia: true, purpose: "prefetch");
        using var prefetched = JsonDocument.Parse(await prefetch.Content.ReadAsStringAsync());
        prefetched.RootElement.TryGetProperty("flash", out _).Should().BeFalse();
        using var conflict = await app.Send(HttpMethod.Get, "/ui/page", inertia: true, version: "old");
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        conflict.Headers.Contains("Set-Cookie").Should().BeFalse();
        using var navigation = await app.Send(HttpMethod.Get, "/ui/page", inertia: true);
        using var page = JsonDocument.Parse(await navigation.Content.ReadAsStringAsync());
        page.RootElement.GetProperty("flash").GetProperty("saved").GetBoolean().Should().BeTrue();
        page.RootElement.GetProperty("props").GetProperty("errors").GetProperty("email").GetString().Should().Be("Required");
    }

    [Fact]
    public async Task Middleware_DoesNotKeepApplicationTempDataAfterTheApplicationReadsIt()
    {
        await using var app = await Fixture.Create();
        using var seed = await app.Send(HttpMethod.Post, "/seed");
        using var first = await app.Send(HttpMethod.Get, "/ui/read");
        (await first.Content.ReadAsStringAsync()).Should().Be("one visit");
        using var second = await app.Send(HttpMethod.Get, "/ui/read");
        (await second.Content.ReadAsStringAsync()).Should().Be("missing");
    }

    [Theory]
    [InlineData("/ui/validation", false, false)]
    [InlineData("/ui/validation", true, true)]
    [InlineData("/api/validation", true, false)]
    public async Task MvcErrors_AreForwardedOnlyForIncludedInertiaRequests(string path, bool inertia, bool expectErrors)
    {
        await using var app = await Fixture.Create();
        using var redirect = await app.Send(HttpMethod.Post, path, inertia: inertia);
        redirect.StatusCode.Should().Be(path.StartsWith("/ui") && inertia ? HttpStatusCode.SeeOther : HttpStatusCode.Found);
        using var response = await app.Send(HttpMethod.Get, "/ui/page", inertia: true);
        using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        page.RootElement.GetProperty("props").GetProperty("errors").TryGetProperty("email", out _).Should().Be(expectErrors);
    }

    [Fact]
    public async Task CsrfTokens_AreGeneratedOnlyForIncludedProps_AndUseTheHandlersCurrentIdentity()
    {
        await using var app = await Fixture.Create();
        using var redirect = await app.Send(HttpMethod.Post, "/ui/redirect", inertia: true);
        if (redirect.Headers.TryGetValues("Set-Cookie", out var cookies))
            cookies.Should().NotContain(cookie => cookie.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal));
        redirect.Headers.CacheControl.Should().BeNull();

        using var partialRequest = new HttpRequestMessage(HttpMethod.Get, "/ui/page");
        partialRequest.Headers.Add(HeaderNames.Inertia, "true");
        partialRequest.Headers.Add(HeaderNames.Version, "v1");
        partialRequest.Headers.Add(HeaderNames.PartialComponent, "Audit/Page");
        partialRequest.Headers.Add(HeaderNames.PartialOnly, "title");
        using var partial = await app.Send(partialRequest);
        partial.Headers.Contains("Set-Cookie").Should().BeFalse();
        partial.Headers.CacheControl.Should().BeNull();
        using var partialPage = JsonDocument.Parse(await partial.Content.ReadAsStringAsync());
        partialPage.RootElement.GetProperty("props").TryGetProperty("xsrfToken", out _).Should().BeFalse();

        // The handler changes the user after the Inertia middleware has run.
        using var login = await app.Send(HttpMethod.Post, "/ui/login", inertia: true);
        using var loggedInPage = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = loggedInPage.RootElement.GetProperty("props").GetProperty("xsrfToken").GetString();
        token.Should().NotBeNullOrEmpty();
        login.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var check = await app.Send(HttpMethod.Post, "/ui/check", inertia: true, token: token, user: "alice");
        check.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ExcludedPrecognitionRequests_ReachTheMvcActionAndMinimalApiHandler()
    {
        await using var app = await Fixture.Create();
        foreach (var path in new[] { "/api/validation", "/api/precognition" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Add(HeaderNames.Precognition, "true");
            using var response = await app.Send(request);
            response.StatusCode.Should().Be(path.EndsWith("validation") ? HttpStatusCode.Found : HttpStatusCode.OK);
            response.Headers.Contains(HeaderNames.PrecognitionSuccess).Should().BeFalse();
        }
    }

    private sealed class PageExecutor : IActionResultExecutor<ViewResult>
    {
        public Task ExecuteAsync(ActionContext context, ViewResult result)
            => context.HttpContext.Response.WriteAsJsonAsync((InertiaPage)result.ViewData.Model!);
    }

    private sealed class Fixture(WebApplication app) : IAsyncDisposable
    {
        private readonly HttpClient _client = app.GetTestClient();
        private readonly CookieContainer _cookies = new();

        public static async Task<Fixture> Create()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddControllersWithViews().AddApplicationPart(typeof(AuditValidationController).Assembly);
            builder.Services.Replace(ServiceDescriptor.Singleton<IActionResultExecutor<ViewResult>>(new PageExecutor()));
            builder.Services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");
            builder.Services.AddInertia(options =>
            {
                options.Version = () => "v1";
                options.RequestPredicate = context => context.Request.Path.StartsWithSegments("/ui");
            });
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                if (context.Request.Headers["X-Test-User"].ToString() is { Length: > 0 } user)
                    context.User = User(user);
                await next(context);
            });
            app.UseInertia();
            app.MapControllers();
            app.MapPost("/seed", (HttpContext context, ITempDataDictionaryFactory factory) =>
            {
                var data = factory.GetTempData(context);
                data[SessionKeys.ValidationErrors] = "{\"email\":[\"Required\"]}";
                data[SessionKeys.FlashData] = "{\"saved\":true}";
                data[SessionKeys.ClearHistory] = true;
                data[SessionKeys.PreserveFragment] = true;
                data["notice"] = "one visit";
                data.Save();
                return Results.NoContent();
            });
            app.MapGet("/api/ping", () => "pong");
            app.MapPost("/api/precognition", () => Results.Ok()).WithInertiaPrecognition();
            app.MapGet("/ui/page", (IInertiaService inertia) => inertia.Render("Audit/Page", new { title = "Ready" }));
            app.MapGet("/ui/read", (HttpContext context, ITempDataDictionaryFactory factory) =>
            {
                var data = factory.GetTempData(context);
                var value = (string?)data["notice"];
                data.Save();
                return Results.Text(value ?? "missing");
            });
            app.MapPost("/ui/redirect", () => Results.Redirect("/ui/page"));
            app.MapPost("/ui/login", (HttpContext context, IInertiaService inertia) =>
            {
                context.User = User("alice");
                return inertia.Render("Audit/Page");
            });
            app.MapPost("/ui/check", async (HttpContext context, IAntiforgery antiforgery) =>
            {
                await antiforgery.ValidateRequestAsync(context);
                return Results.Ok(new { valid = true });
            });
            await app.StartAsync();
            return new(app);
        }

        private static ClaimsPrincipal User(string name) => new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, name), new Claim(ClaimTypes.Name, name)], "test"));

        public async Task<HttpResponseMessage> Send(HttpMethod method, string path, bool inertia = false,
            string? version = "v1", string? purpose = null, string? token = null, string? user = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (inertia) request.Headers.Add(HeaderNames.Inertia, "true");
            if (version is not null) request.Headers.Add(HeaderNames.Version, version);
            if (purpose is not null) request.Headers.Add(HeaderNames.Purpose, purpose);
            if (token is not null) request.Headers.Add("X-XSRF-TOKEN", token);
            if (user is not null) request.Headers.Add("X-Test-User", user);
            return await Send(request);
        }

        public async Task<HttpResponseMessage> Send(HttpRequestMessage request)
        {
            var cookie = _cookies.GetCookieHeader(_client.BaseAddress!);
            if (cookie.Length > 0) request.Headers.Add("Cookie", cookie);
            var response = await _client.SendAsync(request);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
                foreach (var value in values) _cookies.SetCookies(_client.BaseAddress!, value);
            return response;
        }

        public async ValueTask DisposeAsync() { _client.Dispose(); await app.DisposeAsync(); }
    }
}

public class AuditValidationController : Controller
{
    [HttpPost("/ui/validation")]
    [HttpPost("/api/validation")]
    public IActionResult Submit()
    {
        ModelState.AddModelError("Email", "Required");
        return Redirect("/ui/page");
    }
}
