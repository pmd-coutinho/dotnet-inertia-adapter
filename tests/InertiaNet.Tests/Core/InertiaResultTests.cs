using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Support;
using InertiaNet.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace InertiaNet.Tests.Core;

public class InertiaResultTests
{
    private static (InertiaResult result, DefaultHttpContext context) CreateInertiaResult(
        string component = "Users/Index",
        Dictionary<string, object?>? pageProps = null,
        Dictionary<string, object?>? sharedProps = null,
        Action<InertiaOptions>? configureOptions = null)
    {
        var options = new InertiaOptions();
        configureOptions?.Invoke(options);
        var optionsWrapper = Options.Create(options);

        var context = HttpContextHelper.CreateInertia(configureServices: services =>
        {
            services.AddOptions<InertiaOptions>();
            if (configureOptions is not null)
                services.Configure(configureOptions);
            services.AddScoped<IInertiaService, InertiaService>();
            services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        });

        var inertiaService = new InertiaService(optionsWrapper, new HttpContextAccessor { HttpContext = context });

        var result = new InertiaResult(
            component,
            sharedProps ?? new(),
            pageProps ?? new Dictionary<string, object?> { ["name"] = "Alice" },
            inertiaService,
            optionsWrapper);

        return (result, context);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnJsonWithInertiaHeader_WhenInertiaRequest()
    {
        var (result, context) = CreateInertiaResult(
            pageProps: new() { ["name"] = "Alice" });

        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(200);
        context.Response.Headers[HeaderNames.Inertia].ToString().Should().Be("true");
        context.Response.ContentType.Should().Be("application/json");

        var body = await context.ReadResponseBodyAsync();
        body.Should().NotBeNullOrEmpty();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.GetProperty("component").GetString().Should().Be("Users/Index");
        root.GetProperty("props").GetProperty("name").GetString().Should().Be("Alice");
        root.GetProperty("url").GetString().Should().Be("/");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldIncludeVersionInResponse()
    {
        var (result, context) = CreateInertiaResult(
            configureOptions: opts => opts.Version = () => "abc123");

        await result.ExecuteAsync(context);

        var body = await context.ReadResponseBodyAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("version").GetString().Should().Be("abc123");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSetVaryHeader()
    {
        var (result, context) = CreateInertiaResult();

        await result.ExecuteAsync(context);

        context.Response.Headers["Vary"].ToString().Should().Contain("X-Inertia");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseCustomExceptionHandler_WhenItReturnsInertiaResult()
    {
        var (result, context) = CreateInertiaResult(
            component: "Users/Index",
            pageProps: new()
            {
                ["boom"] = new Func<IServiceProvider, CancellationToken, Task<object?>>((_, _) =>
                    throw new InvalidOperationException("Boom"))
            },
            configureOptions: options =>
            {
                options.HandleExceptionsUsing = (exception, httpContext) =>
                    InertiaResults.Inertia("Errors/ServerError", new { message = exception.Message });
            });

        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        var body = await context.ReadResponseBodyAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("component").GetString().Should().Be("Errors/ServerError");
        doc.RootElement.GetProperty("props").GetProperty("message").GetString().Should().Be("Boom");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseCustomExceptionHandler_WhenItReturnsStandardResult()
    {
        var (result, context) = CreateInertiaResult(
            component: "Users/Index",
            pageProps: new()
            {
                ["boom"] = new Func<IServiceProvider, CancellationToken, Task<object?>>((_, _) =>
                    throw new InvalidOperationException("Boom"))
            },
            configureOptions: options =>
            {
                options.HandleExceptionsUsing = (_, _) => new StaticResult(StatusCodes.Status503ServiceUnavailable, "Boom");
            });

        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.ContentType.Should().Be("text/plain");
        (await context.ReadResponseBodyAsync()).Should().Be("Boom");
    }

    [Fact]
    public async Task ExecuteResultAsync_ShouldUseCustomExceptionHandler_WhenItReturnsMvcResult()
    {
        var (result, context) = CreateInertiaResult(
            component: "Users/Index",
            pageProps: new()
            {
                ["boom"] = new Func<IServiceProvider, CancellationToken, Task<object?>>((_, _) =>
                    throw new InvalidOperationException("Boom"))
            },
            configureOptions: options =>
            {
                options.HandleExceptionsUsing = (_, _) => new StaticActionResult(StatusCodes.Status418ImATeapot, "Handled by MVC");
            });

        var actionContext = new ActionContext(context, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        await result.ExecuteResultAsync(actionContext);

        context.Response.StatusCode.Should().Be(StatusCodes.Status418ImATeapot);
        context.Response.ContentType.Should().Be("text/plain");
        (await context.ReadResponseBodyAsync()).Should().Be("Handled by MVC");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldApplyExplicitStatus_WhenInertiaRequest()
    {
        var (result, context) = CreateInertiaResult(component: "Errors/Status");

        await result.WithStatus(StatusCodes.Status404NotFound).ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        context.Response.Headers[HeaderNames.Inertia].ToString().Should().Be("true");
        context.Response.ContentType.Should().Be("application/json");
        using var doc = JsonDocument.Parse(await context.ReadResponseBodyAsync());
        doc.RootElement.GetProperty("component").GetString().Should().Be("Errors/Status");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldKeepStatusAlreadyOnResponse_WhenInertiaRequest()
    {
        var (result, context) = CreateInertiaResult(component: "Errors/Status");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        context.Response.Headers[HeaderNames.Inertia].ToString().Should().Be("true");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPreferExplicitStatusOverStatusAlreadyOnResponse()
    {
        var (result, context) = CreateInertiaResult(component: "Errors/Status");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await result.WithStatus(StatusCodes.Status503ServiceUnavailable).ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Theory]
    [InlineData(StatusCodes.Status204NoContent)]
    [InlineData(StatusCodes.Status302Found)]
    [InlineData(StatusCodes.Status304NotModified)]
    public async Task ExecuteAsync_ShouldFallBackTo200_WhenStatusOnResponseCannotCarryAPage(int statusCode)
    {
        var (result, context) = CreateInertiaResult();
        context.Response.StatusCode = statusCode;

        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        (await context.ReadResponseBodyAsync()).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(99)]
    [InlineData(StatusCodes.Status100Continue)]
    [InlineData(StatusCodes.Status204NoContent)]
    [InlineData(StatusCodes.Status205ResetContent)]
    [InlineData(StatusCodes.Status301MovedPermanently)]
    [InlineData(StatusCodes.Status304NotModified)]
    [InlineData(600)]
    public void WithStatus_ShouldRejectStatusesThatCannotCarryAPage(int statusCode)
    {
        var (result, _) = CreateInertiaResult();

        var act = () => result.WithStatus(statusCode);

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("statusCode");
    }

    [Theory]
    [InlineData(StatusCodes.Status200OK)]
    [InlineData(StatusCodes.Status201Created)]
    [InlineData(StatusCodes.Status403Forbidden)]
    [InlineData(StatusCodes.Status409Conflict)]
    [InlineData(419)]
    [InlineData(StatusCodes.Status503ServiceUnavailable)]
    public void WithStatus_ShouldAcceptStatusesThatCanCarryAPage(int statusCode)
    {
        var (result, _) = CreateInertiaResult();

        result.WithStatus(statusCode).Should().BeSameAs(result);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotLeakExplicitStatusIntoCustomExceptionHandlerResult()
    {
        var (result, context) = CreateInertiaResult(
            pageProps: new() { ["report"] = new UnserializableValue() },
            configureOptions: options =>
            {
                options.HandleExceptionsUsing = (_, _) => InertiaResults.Inertia("Errors/ServerError");
            });

        await result.WithStatus(StatusCodes.Status404NotFound).ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        using var doc = JsonDocument.Parse(await context.ReadResponseBodyAsync());
        doc.RootElement.GetProperty("component").GetString().Should().Be("Errors/ServerError");
    }

    [System.Text.Json.Serialization.JsonConverter(typeof(UnserializableValueConverter))]
    private sealed class UnserializableValue;

    private sealed class UnserializableValueConverter : System.Text.Json.Serialization.JsonConverter<UnserializableValue>
    {
        public override UnserializableValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, UnserializableValue value, JsonSerializerOptions options)
            => throw new InvalidOperationException("Cannot serialize.");
    }

    private sealed class StaticResult(int statusCode, string body) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.ContentType = "text/plain";
            await httpContext.Response.WriteAsync(body);
        }
    }

    private sealed class StaticActionResult(int statusCode, string body) : IActionResult
    {
        public async Task ExecuteResultAsync(ActionContext context)
        {
            context.HttpContext.Response.StatusCode = statusCode;
            context.HttpContext.Response.ContentType = "text/plain";
            await context.HttpContext.Response.WriteAsync(body);
        }
    }
}
