using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Globalization;
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
    public async Task ExecuteAsync_ShouldPreserveProtocolEnvelope_WhenCustomJsonOptionsAreConfigured()
    {
        var (result, context) = CreateInertiaResult(
            pageProps: new() { ["userName"] = "Alice" },
            configureOptions: opts =>
            {
                opts.JsonSerializerOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = null,
                    DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
                };
            });

        await result.ExecuteAsync(context);

        var body = await context.ReadResponseBodyAsync();
        using var doc = JsonDocument.Parse(body);

        doc.RootElement.TryGetProperty("Component", out _).Should().BeFalse();
        doc.RootElement.GetProperty("component").GetString().Should().Be("Test/Page");
        doc.RootElement.GetProperty("props").GetProperty("userName").GetString().Should().Be("Alice");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseCustomConverters_ForPropValues()
    {
        var (result, context) = CreateInertiaResult(
            pageProps: new() { ["price"] = new Money(12.34m) },
            configureOptions: opts =>
            {
                opts.JsonSerializerOptions = new JsonSerializerOptions();
                opts.JsonSerializerOptions.Converters.Add(new MoneyJsonConverter());
            });

        await result.ExecuteAsync(context);

        var body = await context.ReadResponseBodyAsync();
        using var doc = JsonDocument.Parse(body);

        doc.RootElement.GetProperty("component").GetString().Should().Be("Test/Page");
        doc.RootElement.GetProperty("props").GetProperty("price").GetString().Should().Be("USD 12.34");
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

    private sealed record Money(decimal Amount);

    private sealed class MoneyJsonConverter : JsonConverter<Money>
    {
        public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
            => writer.WriteStringValue($"USD {value.Amount.ToString("0.00", CultureInfo.InvariantCulture)}");
    }
}
