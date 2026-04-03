using System.Net;
using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Support;
using InertiaNet.Testing;
using InertiaNet.Tests.Helpers;
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

internal sealed class ProtocolViewResultExecutor : IActionResultExecutor<ViewResult>
{
    public const string ViewNameHeader = "X-Test-View-Name";
    public const string PageComponentHeader = "X-Test-Page-Component";

    public async Task ExecuteAsync(ActionContext context, ViewResult result)
    {
        var page = result.ViewData.Model as InertiaPage;

        context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
        context.HttpContext.Response.ContentType = "text/plain";
        context.HttpContext.Response.Headers[ViewNameHeader] = result.ViewName ?? string.Empty;
        context.HttpContext.Response.Headers[PageComponentHeader] = page?.Component ?? string.Empty;

        await context.HttpContext.Response.WriteAsync($"Rendered {page?.Component}");
    }
}

public class ProtocolIntegrationTests
{
    [Fact]
    public async Task InitialRequests_ShouldRenderRootView_WithPageModel()
    {
        var app = await CreateAppAsync();

        try
        {
            using var response = await app.Client.GetAsync("/");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.GetValues(ProtocolViewResultExecutor.ViewNameHeader)
                .Should().ContainSingle().Which.Should().Be("App");
            response.Headers.GetValues(ProtocolViewResultExecutor.PageComponentHeader)
                .Should().ContainSingle().Which.Should().Be("Dashboard");
            response.Headers.Vary.Should().Contain(HeaderNames.Inertia);
            response.Headers.Contains(HeaderNames.Inertia).Should().BeFalse();
            (await response.Content.ReadAsStringAsync()).Should().Be("Rendered Dashboard");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task InertiaRequests_ShouldReturnJsonWithProtocolMetadata()
    {
        var app = await CreateAppAsync();

        try
        {
            using var response = CreateInertiaRequest(HttpMethod.Get, "/feed", request =>
            {
                request.Headers.Add(HeaderNames.ExceptOnceProps, "countries");
            });

            using var httpResponse = await app.Client.SendAsync(response);
            var page = await httpResponse.AssertInertiaAsync();

            page.HasComponent("Feed")
                .HasUrl("/feed")
                .HasProp("items[0]", 1)
                .DoesNotHaveProp("countries");

            httpResponse.Headers.Vary.Should().Contain(HeaderNames.Inertia);
            page.Page.MergeProps.Should().Contain("items");
            page.Page.DeferredProps.Should().ContainKey("analytics");
            page.Page.DeferredProps!["analytics"].Should().Contain("stats");
            page.Page.OnceProps.Should().ContainKey("countries");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExternalRedirects_ShouldUseInertiaLocationProtocol()
    {
        var app = await CreateAppAsync();

        try
        {
            using var request = CreateInertiaRequest(HttpMethod.Post, "/redirect/external");
            using var response = await app.Client.SendAsync(request);

            response.AssertInertiaRedirect().To("https://example.com");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task FragmentRedirects_ShouldUseFragmentRedirectProtocol()
    {
        var app = await CreateAppAsync();

        try
        {
            using var request = CreateInertiaRequest(HttpMethod.Post, "/redirect/fragment");
            using var response = await app.Client.SendAsync(request);

            response.AssertFragmentRedirect().To("/reports#summary");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task PrefetchRequests_ShouldSetCacheHeaders_AndNotConsumeFlash()
    {
        var app = await CreateAppAsync();

        try
        {
            using var submitRequest = CreateInertiaRequest(HttpMethod.Post, "/submit");
            using var submitResponse = await app.Client.SendAsync(submitRequest);

            submitResponse.StatusCode.Should().Be(HttpStatusCode.SeeOther);
            submitResponse.Headers.Location.Should().NotBeNull();
            submitResponse.Headers.Location!.OriginalString.Should().Be("/dashboard");

            using var prefetchRequest = CreateInertiaRequest(HttpMethod.Get, "/dashboard", request =>
            {
                request.Headers.Add(HeaderNames.Purpose, "prefetch");
                request.Headers.Add(HeaderNames.Prefetch, "42");
            });

            using var prefetchResponse = await app.Client.SendAsync(prefetchRequest);
            var prefetchPage = await prefetchResponse.AssertInertiaAsync();

            prefetchResponse.Headers.CacheControl.Should().NotBeNull();
            prefetchResponse.Headers.CacheControl!.Private.Should().BeTrue();
            prefetchResponse.Headers.CacheControl!.MaxAge.Should().Be(TimeSpan.FromSeconds(42));
            prefetchPage.DoesNotHaveFlash("message");

            using var realVisitRequest = CreateInertiaRequest(HttpMethod.Get, "/dashboard");
            using var realVisitResponse = await app.Client.SendAsync(realVisitRequest);
            var realVisitPage = await realVisitResponse.AssertInertiaAsync();

            realVisitPage.HasFlash("message", "Saved");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task ValidationErrors_ShouldBeForwardedAcrossRedirects()
    {
        var app = await CreateAppAsync();

        try
        {
            using var submitRequest = CreateInertiaRequest(HttpMethod.Post, "/users");
            using var submitResponse = await app.Client.SendAsync(submitRequest);

            submitResponse.StatusCode.Should().Be(HttpStatusCode.SeeOther);
            submitResponse.Headers.Location.Should().NotBeNull();
            submitResponse.Headers.Location!.OriginalString.Should().Be("/users/create");

            using var formRequest = CreateInertiaRequest(HttpMethod.Get, "/users/create");
            using var formResponse = await app.Client.SendAsync(formRequest);
            var page = await formResponse.AssertInertiaAsync();

            page.HasProp("errors.createUser.email", "Required");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task HistoryFlags_ShouldBeRestoredAcrossRedirects()
    {
        var app = await CreateAppAsync();

        try
        {
            using var submitRequest = CreateInertiaRequest(HttpMethod.Post, "/filters");
            using var submitResponse = await app.Client.SendAsync(submitRequest);

            submitResponse.AssertFragmentRedirect().To("/reports#summary");

            using var reportRequest = CreateInertiaRequest(HttpMethod.Get, "/reports");
            using var reportResponse = await app.Client.SendAsync(reportRequest);
            var page = await reportResponse.AssertInertiaAsync();

            page.HasClearHistory().HasPreserveFragment();
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

        var tempDataFactory = new FakeTempDataFactory();

        builder.Services.AddControllersWithViews();
        builder.Services.Replace(ServiceDescriptor.Singleton<IActionResultExecutor<ViewResult>, ProtocolViewResultExecutor>());
        builder.Services.Replace(ServiceDescriptor.Singleton<ITempDataDictionaryFactory>(tempDataFactory));
        builder.Services.AddInertia(options =>
        {
            options.RootView = "App";
            options.PrefetchCacheMaxAge = 10;
            options.Version = () => "server-version";
        });

        var app = builder.Build();

        app.UseInertia();

        app.MapGet("/", (IInertiaService inertia) => inertia.Render("Dashboard", new { greeting = "Hello" }));
        app.MapGet("/dashboard", (IInertiaService inertia) => inertia.Render("Dashboard", new { ready = true }));
        app.MapGet("/reports", (IInertiaService inertia) => inertia.Render("Reports", new { filters = new[] { "all" } }));
        app.MapGet("/users/create", (IInertiaService inertia) => inertia.Render("Users/Create"));
        app.MapGet("/feed", (IInertiaService inertia) => inertia.Render("Feed", new Dictionary<string, object?>
        {
            ["items"] = inertia.Merge(new[] { 1, 2 }),
            ["stats"] = inertia.Defer((_, _) => Task.FromResult<object?>(new { total = 2 }), group: "analytics"),
            ["countries"] = inertia.Once((_, _) => Task.FromResult<object?>(new[] { "BR", "US" })),
        }));

        app.MapPost("/redirect/external", (IInertiaService inertia) => inertia.Location("https://example.com"));
        app.MapPost("/redirect/fragment", () => Results.Redirect("/reports#summary"));
        app.MapPost("/submit", (IInertiaService inertia) =>
        {
            inertia.Flash("message", "Saved");
            return Results.Redirect("/dashboard");
        });
        app.MapPost("/filters", (IInertiaService inertia) =>
        {
            inertia.ClearHistory();
            inertia.PreserveFragment();
            return Results.Redirect("/reports#summary");
        });
        app.MapPost("/users", (HttpContext context) =>
        {
            context.SetInertiaValidationErrors(new Dictionary<string, string[]>
            {
                ["email"] = ["Required"],
            });
            context.SetInertiaErrorBag("createUser");
            return Results.Redirect("/users/create");
        }).WithInertiaValidation();

        await app.StartAsync();

        return new TestApp(app, app.GetTestClient(), tempDataFactory);
    }

    private static HttpRequestMessage CreateInertiaRequest(
        HttpMethod method,
        string uri,
        Action<HttpRequestMessage>? configure = null)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add(HeaderNames.Inertia, "true");
        configure?.Invoke(request);
        return request;
    }

    private sealed record TestApp(WebApplication App, HttpClient Client, FakeTempDataFactory TempDataFactory) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => App.DisposeAsync();
    }
}
