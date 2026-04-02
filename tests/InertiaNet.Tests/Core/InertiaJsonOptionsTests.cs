using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InertiaNet.Tests.Core;

public class InertiaJsonOptionsTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldUseCamelCaseForPageProperties_ByDefault()
    {
        var (result, context) = CreateInertiaResult(
            pageProps: new() { ["name"] = "Alice" });

        await result.ExecuteAsync(context);

        var body = await context.ReadResponseBodyAsync();
        using var doc = JsonDocument.Parse(body);
        // Default camelCase: InertiaPage.Component → "component"
        doc.RootElement.GetProperty("component").GetString().Should().Be("Test/Page");
        doc.RootElement.GetProperty("props").GetProperty("name").GetString().Should().Be("Alice");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseCustomJsonOptions_WhenConfigured()
    {
        var (result, context) = CreateInertiaResult(
            pageProps: new() { ["name"] = "Alice" },
            configureOptions: opts =>
            {
                opts.JsonSerializerOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = null, // PascalCase (no conversion)
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                };
            });

        await result.ExecuteAsync(context);

        var body = await context.ReadResponseBodyAsync();
        using var doc = JsonDocument.Parse(body);
        // Custom options: InertiaPage.Component → "Component" (PascalCase)
        doc.RootElement.GetProperty("Component").GetString().Should().Be("Test/Page");
        doc.RootElement.GetProperty("Props").GetProperty("name").GetString().Should().Be("Alice");
    }

    private static (InertiaResult result, DefaultHttpContext context) CreateInertiaResult(
        Dictionary<string, object?>? pageProps = null,
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
            "Test/Page",
            new(),
            pageProps ?? new Dictionary<string, object?>(),
            inertiaService,
            optionsWrapper);

        return (result, context);
    }
}
