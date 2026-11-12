using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using InertiaNet.Authentication;
using InertiaNet.Extensions;
using InertiaNet.Support;
using InertiaNet.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace InertiaNet.Tests.Integration;

public class AuthenticationChallengeIntegrationTests
{
    private const string Login = "http://localhost/account/login";
    private const string Denied = "http://localhost/account/denied";

    [Theory]
    [InlineData(true)]  // Inertia v3 client shape: the cookie handler answers 401 + Location
    [InlineData(false)] // Without X-Requested-With: the cookie handler answers 302
    public async Task InertiaVisit_WithoutSession_ReturnsLocationVisitToLogin(bool xhr)
    {
        await using var app = await Fixture.Create();

        using var response = await app.Send(Inertia(HttpMethod.Get, "/dashboard?tab=2", xhr));

        response.AssertInertiaRedirect().To($"{Login}?ReturnUrl=%2Fdashboard%3Ftab%3D2");
        response.Headers.Location.Should().BeNull();
        response.Headers.Contains(HeaderNames.Inertia).Should().BeFalse();
        response.Headers.Vary.Should().Contain(HeaderNames.Inertia);
    }

    [Fact]
    public async Task InertiaVisit_WithExpiredSession_ReturnsLocationVisitToLogin()
    {
        await using var app = await Fixture.Create();

        var valid = await app.SignIn();
        using (var request = Inertia(HttpMethod.Get, "/dashboard"))
        {
            request.Headers.Add("Cookie", valid);
            using var ok = await app.Send(request);
            var page = await ok.AssertInertiaAsync();
            page.HasComponent("Dashboard");
        }

        var expired = await app.SignIn(expired: true);
        using (var request = Inertia(HttpMethod.Get, "/dashboard"))
        {
            request.Headers.Add("Cookie", expired);
            using var response = await app.Send(request);
            response.AssertInertiaRedirect().To($"{Login}?ReturnUrl=%2Fdashboard");
        }
    }

    [Theory]
    [InlineData("http://localhost/todos/create?draft=1", "%2Ftodos%2Fcreate%3Fdraft%3D1")]
    [InlineData("/todos/create", "%2Ftodos%2Fcreate")]
    [InlineData("https://evil.example/todos", "%2F")]
    [InlineData(null, "%2F")]
    public async Task InertiaMutation_WithoutSession_ReturnsToThePreviousSameOriginPage(string? referer, string returnUrl)
    {
        await using var app = await Fixture.Create();

        using var request = Inertia(HttpMethod.Post, "/todos");
        if (referer is not null) request.Headers.TryAddWithoutValidation("Referer", referer);
        using var response = await app.Send(request);

        response.AssertInertiaRedirect().To($"{Login}?ReturnUrl={returnUrl}");
    }

    [Fact]
    public async Task InertiaChallenge_PreservesAnExplicitRedirectUri()
    {
        await using var app = await Fixture.Create();

        using var request = Inertia(HttpMethod.Post, "/explicit-challenge");
        request.Headers.Referrer = new Uri("http://localhost/elsewhere");
        using var response = await app.Send(request);

        response.AssertInertiaRedirect().To($"{Login}?ReturnUrl=%2Fcustom");
    }

    [Fact]
    public async Task InertiaChallenge_InsideTheInertiaMiddleware_IsNotRewrittenTo303()
    {
        await using var app = await Fixture.Create(authorizeInsideInertia: true);

        using var request = Inertia(HttpMethod.Post, "/todos");
        request.Headers.Referrer = new Uri("http://localhost/todos");
        using var response = await app.Send(request);

        response.AssertInertiaRedirect().To($"{Login}?ReturnUrl=%2Ftodos");
        response.Headers.Vary.Should().Contain(HeaderNames.Inertia);
    }

    [Fact]
    public async Task BrowserVisit_WithoutSession_KeepsTheLoginRedirect()
    {
        await using var app = await Fixture.Create();

        using var response = await app.Send(Browser(HttpMethod.Get, "/dashboard"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be($"{Login}?ReturnUrl=%2Fdashboard");
        response.Headers.Contains(HeaderNames.Location).Should().BeFalse();
    }

    [Theory]
    [InlineData("/api/orders", null, false)]
    [InlineData("/api/orders", "*/*", false)]
    [InlineData("/api/orders", "application/json", false)]
    [InlineData("/reports", "application/json, text/plain, */*", false)]
    [InlineData("/reports", "*/*", true)] // the cookie handler's XHR shape: 401 + Location
    public async Task ApiRequest_WithoutSession_Returns401(string path, string? accept, bool xhr)
    {
        await using var app = await Fixture.Create();

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (accept is not null) request.Headers.TryAddWithoutValidation("Accept", accept);
        if (xhr) request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        using var response = await app.Send(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull();
        response.Headers.Contains(HeaderNames.Location).Should().BeFalse();
    }

    [Fact]
    public async Task BrowserVisit_ToAnExcludedPath_KeepsTheLoginRedirect()
    {
        await using var app = await Fixture.Create();

        using var response = await app.Send(Browser(HttpMethod.Get, "/api/orders"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be($"{Login}?ReturnUrl=%2Fapi%2Forders");
    }

    [Fact]
    public async Task WithoutChallengeHandling_AuthenticationKeepsItsDefaultResponses()
    {
        await using var app = await Fixture.Create(challengeHandling: false);

        using var inertia = await app.Send(Inertia(HttpMethod.Get, "/dashboard"));
        inertia.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        inertia.Headers.Location!.ToString().Should().Be($"{Login}?ReturnUrl=%2Fdashboard");

        using var api = await app.Send(new HttpRequestMessage(HttpMethod.Get, "/api/orders"));
        api.StatusCode.Should().Be(HttpStatusCode.Redirect);

        using var json = new HttpRequestMessage(HttpMethod.Get, "/reports");
        json.Headers.TryAddWithoutValidation("Accept", "application/json");
        using var jsonResponse = await app.Send(json);
        jsonResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task NullApiRequestPredicate_KeepsNonInertiaRedirects()
    {
        await using var app = await Fixture.Create(configure: options => options.ApiRequestPredicate = null);

        using var api = await app.Send(new HttpRequestMessage(HttpMethod.Get, "/api/orders"));
        api.StatusCode.Should().Be(HttpStatusCode.Redirect);

        using var inertia = await app.Send(Inertia(HttpMethod.Get, "/dashboard"));
        inertia.AssertInertiaRedirect().To($"{Login}?ReturnUrl=%2Fdashboard");
    }

    [Fact]
    public async Task CustomApiRequestPredicate_ReplacesTheDefault()
    {
        await using var app = await Fixture.Create(configure: options => options.ApiRequestPredicate =
            context => context.Request.Path.StartsWithSegments("/reports")
                || InertiaChallengeOptions.DefaultApiRequestPredicate(context));

        using var reports = await app.Send(Browser(HttpMethod.Get, "/reports"));
        reports.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var api = await app.Send(new HttpRequestMessage(HttpMethod.Get, "/api/orders"));
        api.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InertiaVisit_Forbidden_Returns403WithoutRedirect(bool xhr)
    {
        await using var app = await Fixture.Create();
        var cookie = await app.SignIn();

        using var request = Inertia(HttpMethod.Get, "/admin", xhr);
        request.Headers.Add("Cookie", cookie);
        using var response = await app.Send(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Headers.Contains(HeaderNames.Location).Should().BeFalse();
        response.Headers.Vary.Should().Contain(HeaderNames.Inertia);
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task BrowserVisit_Forbidden_KeepsTheAccessDeniedRedirect()
    {
        await using var app = await Fixture.Create();
        var cookie = await app.SignIn();

        using var request = Browser(HttpMethod.Get, "/admin");
        request.Headers.Add("Cookie", cookie);
        using var response = await app.Send(request);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be($"{Denied}?ReturnUrl=%2Fadmin");
    }

    [Fact]
    public async Task ApiRequest_Forbidden_Returns403()
    {
        await using var app = await Fixture.Create();
        var cookie = await app.SignIn();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin");
        request.Headers.Add("Cookie", cookie);
        using var response = await app.Send(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task Registration_IsIdempotent_AndIndependentOfAddAuthenticationOrder()
    {
        var services = new ServiceCollection();
        services.AddInertiaChallengeHandling();
        services.AddInertiaChallengeHandling(options => options.ApiRequestPredicate = null);
        services.AddAuthentication().AddCookie();

        services.Count(descriptor => descriptor.ServiceType == typeof(IAuthenticationService)).Should().Be(1);

        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthenticationService>().Should().BeOfType<InertiaAuthenticationService>();
        provider.GetRequiredService<IOptions<InertiaChallengeOptions>>().Value.ApiRequestPredicate.Should().BeNull();
    }

    [Fact]
    public async Task ForwardedChallenges_AreTranslatedOnce()
    {
        await using var app = await Fixture.Create(challengeScheme: "forwarding");

        using var request = Inertia(HttpMethod.Post, "/todos");
        request.Headers.Referrer = new Uri("http://localhost/todos/create");
        using var response = await app.Send(request);

        response.AssertInertiaRedirect().To($"{Login}?ReturnUrl=%2Ftodos%2Fcreate");
    }

    [Fact]
    public async Task OpenIdConnectChallenge_ForInertiaVisit_ReturnsLocationVisitToTheIdentityProvider()
    {
        await using var app = await Fixture.Create(challengeScheme: OpenIdConnectDefaults.AuthenticationScheme);

        using var response = await app.Send(Inertia(HttpMethod.Get, "/dashboard"));

        var location = response.AssertInertiaRedirect().Location;
        location.Should().StartWith("https://idp.example/connect/authorize?")
            .And.Contain("client_id=storefront-admin")
            .And.Contain("redirect_uri=http%3A%2F%2Flocalhost%2Fsignin-oidc")
            .And.Contain("state=");
        response.Headers.Location.Should().BeNull();
        response.Headers.GetValues("Set-Cookie").Should().Contain(cookie => cookie.StartsWith(".AspNetCore.Correlation."));
    }

    [Fact]
    public async Task OpenIdConnectChallenge_ForBrowserAndApiRequests()
    {
        await using var app = await Fixture.Create(challengeScheme: OpenIdConnectDefaults.AuthenticationScheme);

        using var browser = await app.Send(Browser(HttpMethod.Get, "/dashboard"));
        browser.StatusCode.Should().Be(HttpStatusCode.Redirect);
        browser.Headers.Location!.ToString().Should().StartWith("https://idp.example/connect/authorize?");

        using var api = await app.Send(new HttpRequestMessage(HttpMethod.Get, "/api/orders"));
        api.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        api.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task RemoteChallenge_ThatWritesABody_IsLeftUnchanged()
    {
        await using var app = await Fixture.Create(challengeScheme: FormPostHandler.SchemeName);

        using var response = await app.Send(Inertia(HttpMethod.Get, "/dashboard"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains(HeaderNames.Location).Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().Contain("<form");
    }

    private static HttpRequestMessage Inertia(HttpMethod method, string path, bool xhr = true)
    {
        // Mirrors the headers sent by @inertiajs/core 3.x (Request.getHeaders).
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(HeaderNames.Inertia, "true");
        request.Headers.TryAddWithoutValidation("Accept", "text/html, application/xhtml+xml");
        if (xhr) request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        return request;
    }

    private static HttpRequestMessage Browser(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        return request;
    }

    private sealed class FormPostHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "form-post";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

        protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.ContentType = "text/html";
            await Response.WriteAsync("<form method=\"post\" action=\"https://idp.example/authorize\"></form>");
        }
    }

    private sealed class Fixture(WebApplication app) : IAsyncDisposable
    {
        private readonly HttpClient _client = app.GetTestClient();

        public static async Task<Fixture> Create(
            bool challengeHandling = true,
            bool authorizeInsideInertia = false,
            string challengeScheme = CookieAuthenticationDefaults.AuthenticationScheme,
            Action<InertiaChallengeOptions>? configure = null)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddInertia(options =>
                options.RequestPredicate = context => !context.Request.Path.StartsWithSegments("/api"));

            var authentication = builder.Services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = challengeScheme;
                })
                .AddCookie(options =>
                {
                    options.LoginPath = "/account/login";
                    options.AccessDeniedPath = "/account/denied";
                })
                .AddPolicyScheme("forwarding", "Forwarding", options =>
                    options.ForwardDefaultSelector = _ => CookieAuthenticationDefaults.AuthenticationScheme)
                .AddScheme<AuthenticationSchemeOptions, FormPostHandler>(FormPostHandler.SchemeName, null)
                .AddOpenIdConnect(options =>
                {
                    options.ClientId = "storefront-admin";
                    options.Configuration = new OpenIdConnectConfiguration
                    {
                        Issuer = "https://idp.example",
                        AuthorizationEndpoint = "https://idp.example/connect/authorize",
                    };
                });
            if (challengeHandling) authentication.AddInertiaChallengeHandling(configure);

            builder.Services.AddAuthorization(options =>
                options.AddPolicy("Admin", policy => policy.RequireRole("admin")));

            var app = builder.Build();
            app.UseRouting();
            app.UseAuthentication();
            if (authorizeInsideInertia)
            {
                app.UseInertia();
                app.UseAuthorization();
            }
            else
            {
                app.UseAuthorization();
                app.UseInertia();
            }

            app.MapGet("/dashboard", () => InertiaResults.Inertia("Dashboard")).RequireAuthorization();
            app.MapGet("/reports", () => InertiaResults.Inertia("Reports")).RequireAuthorization();
            app.MapPost("/todos", () => Results.Redirect("/todos")).RequireAuthorization();
            app.MapGet("/admin", () => InertiaResults.Inertia("Admin")).RequireAuthorization("Admin");
            app.MapGet("/api/orders", () => Results.Text("[]")).RequireAuthorization();
            app.MapGet("/api/admin", () => Results.Text("[]")).RequireAuthorization("Admin");
            app.MapPost("/explicit-challenge", () => Results.Challenge(new AuthenticationProperties { RedirectUri = "/custom" }));
            app.MapPost("/account/sign-in", async (HttpContext context, bool expired) =>
            {
                var principal = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "alice"), new Claim(ClaimTypes.Name, "alice")],
                    CookieAuthenticationDefaults.AuthenticationScheme));
                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
                    new AuthenticationProperties
                    {
                        IssuedUtc = DateTimeOffset.UtcNow.AddHours(-2),
                        ExpiresUtc = expired ? DateTimeOffset.UtcNow.AddMinutes(-1) : DateTimeOffset.UtcNow.AddHours(1),
                    });
            });

            await app.StartAsync();
            return new(app);
        }

        /// <summary>Signs in and returns the request Cookie header value for the session.</summary>
        public async Task<string> SignIn(bool expired = false)
        {
            using var response = await _client.PostAsync($"/account/sign-in?expired={expired}", null);
            response.EnsureSuccessStatusCode();
            var setCookie = response.Headers.GetValues("Set-Cookie")
                .Single(value => value.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal));
            return setCookie[..setCookie.IndexOf(';')];
        }

        public Task<HttpResponseMessage> Send(HttpRequestMessage request) => _client.SendAsync(request);

        public async ValueTask DisposeAsync() { _client.Dispose(); await app.DisposeAsync(); }
    }
}
