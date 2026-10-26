using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Middleware;
using InertiaNet.Support;
using InertiaNet.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InertiaNet.Tests.Middleware;

/// <summary>
/// Test subclass that allows controlling the version returned by the middleware.
/// </summary>
file sealed class TestInertiaMiddleware : InertiaMiddleware
{
    private readonly string? _version;

    public TestInertiaMiddleware(RequestDelegate next, string? version = null) : base(next)
    {
        _version = version;
    }

    protected override string? GetVersion(HttpContext context) => _version;
}

public class InertiaMiddlewareTests
{
    private static DefaultHttpContext CreateContextWithInertiaServices(
        string method = "GET",
        string path = "/",
        Dictionary<string, string>? headers = null,
        FakeTempDataFactory? tempDataFactory = null,
        Action<InertiaOptions>? configure = null)
    {
        return HttpContextHelper.Create(method, path, headers, services =>
        {
            services.AddOptions<InertiaOptions>();
            if (configure is not null) services.Configure(configure);
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddScoped<IInertiaService, InertiaService>();
            if (tempDataFactory is not null)
                services.AddSingleton<ITempDataDictionaryFactory>(tempDataFactory);
        });
    }

    [Theory]
    [InlineData(200)]
    [InlineData(204)]
    public async Task EmptyResponse_CanBePreservedWithoutChangingRedirectNormalization(int status)
    {
        var context = CreateContextWithInertiaServices("POST", "/submit", new() { ["X-Inertia"] = "true" },
            configure: options => options.RedirectEmptyResponses = false);
        await new InertiaMiddleware(ctx => { ctx.Response.StatusCode = status; return Task.CompletedTask; }).InvokeAsync(context);
        context.Response.StatusCode.Should().Be(status);
        context.Response.Headers.Should().NotContainKey("Location");
    }

    [Fact]
    public async Task ExcludedRequest_SkipsVersionHooksHeadersAndTransientState()
    {
        var data = new FakeTempDataFactory();
        data.TempData[SessionKeys.ValidationErrors] = "{\"email\":[\"Required\"]}";
        var context = CreateContextWithInertiaServices(headers: new() { ["X-Inertia"] = "true", ["X-Inertia-Version"] = "old" },
            tempDataFactory: data, configure: options =>
            {
                options.RequestPredicate = _ => false;
                options.Version = () => throw new InvalidOperationException("Version hook must not run");
            });
        var called = false;
        await new InertiaMiddleware(ctx => { called = true; ctx.Response.StatusCode = 204; return Task.CompletedTask; }).InvokeAsync(context);
        called.Should().BeTrue();
        context.Response.StatusCode.Should().Be(204);
        context.Response.Headers.Should().NotContainKey("Vary");
        context.Items.Should().BeEmpty();
        context.RequestServices.GetRequiredService<IInertiaService>().GetSharedProps().Should().BeEmpty();
        data.TempData.Peek(SessionKeys.ValidationErrors).Should().NotBeNull();
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturn409_WhenVersionMismatch()
    {
        var context = CreateContextWithInertiaServices(headers: new()
        {
            ["X-Inertia"] = "true",
            ["X-Inertia-Version"] = "old-version"
        });

        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new TestInertiaMiddleware(next, version: "new-version");

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(409);
        context.Response.Headers[HeaderNames.Location].ToString().Should().Be("/");
    }

    [Fact]
    public async Task InvokeAsync_ShouldRewriteRedirectTo303_WhenNonGetInertiaRequest()
    {
        var context = CreateContextWithInertiaServices("POST", "/submit", new()
        {
            ["X-Inertia"] = "true"
        }, configure: options => options.RedirectEmptyResponses = false);

        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers.Location = "/dashboard";
            return Task.CompletedTask;
        };
        var middleware = new TestInertiaMiddleware(next);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(303);
    }

    [Fact]
    public async Task InvokeAsync_ShouldRedirectEmptyResponseBack_WhenInertiaRequest()
    {
        var context = CreateContextWithInertiaServices(headers: new()
        {
            ["X-Inertia"] = "true"
        });

        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = 204;
            return Task.CompletedTask;
        };
        var middleware = new TestInertiaMiddleware(next);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(302);
        context.Response.Headers.Location.ToString().Should().Be("/");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturn409WithRedirectHeader_WhenRedirectHasFragment()
    {
        var context = CreateContextWithInertiaServices(headers: new()
        {
            ["X-Inertia"] = "true"
        });

        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers.Location = "/page#section";
            return Task.CompletedTask;
        };
        var middleware = new TestInertiaMiddleware(next);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(409);
        context.Response.Headers[HeaderNames.Redirect].ToString().Should().Be("/page#section");
    }

    [Fact]
    public async Task InvokeAsync_ShouldNotConsumeTempData_WhenPrefetchRequest()
    {
        var tempDataFactory = new FakeTempDataFactory();
        tempDataFactory.TempData[SessionKeys.ValidationErrors] = """{"email":["Required"]}""";

        var context = CreateContextWithInertiaServices(headers: new()
        {
            ["X-Inertia"] = "true",
            ["Purpose"] = "prefetch"
        }, tempDataFactory: tempDataFactory);

        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new TestInertiaMiddleware(next);

        await middleware.InvokeAsync(context);

        // TempData should still contain the validation errors (not consumed)
        tempDataFactory.TempData[SessionKeys.ValidationErrors].Should().NotBeNull();
    }

    [Fact]
    public async Task InvokeAsync_ShouldPassThrough_WhenNotInertiaRequest()
    {
        var context = CreateContextWithInertiaServices();

        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        };
        var middleware = new TestInertiaMiddleware(next);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_ShouldPassThroughMultipartBody_WhenInertiaRequest()
    {
        var context = CreateContextWithInertiaServices("POST", "/upload", new()
        {
            ["X-Inertia"] = "true",
            ["Content-Type"] = "multipart/form-data; boundary=----WebKitFormBoundary"
        });

        var bodyContent = "------WebKitFormBoundary\r\nContent-Disposition: form-data; name=\"file\"; filename=\"test.txt\"\r\n\r\nfile content\r\n------WebKitFormBoundary--\r\n";
        var bodyBytes = System.Text.Encoding.UTF8.GetBytes(bodyContent);
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Request.ContentLength = bodyBytes.Length;

        byte[]? capturedBody = null;
        RequestDelegate next = async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            capturedBody = ms.ToArray();
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers.Location = "/success";
        };
        var middleware = new TestInertiaMiddleware(next);

        await middleware.InvokeAsync(context);

        capturedBody.Should().NotBeNull();
        capturedBody!.Length.Should().Be(bodyBytes.Length);
    }
}
