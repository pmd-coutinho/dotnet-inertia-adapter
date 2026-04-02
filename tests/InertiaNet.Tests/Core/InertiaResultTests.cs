using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Support;
using InertiaNet.Tests.Helpers;
using Microsoft.AspNetCore.Http;
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
}
