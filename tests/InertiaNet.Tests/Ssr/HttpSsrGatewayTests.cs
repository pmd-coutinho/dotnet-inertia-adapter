using System.Net;
using System.Text;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Ssr;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InertiaNet.Tests.Ssr;

public class HttpSsrGatewayTests
{
    [Fact]
    public async Task DispatchAsync_ShouldUseProtocolSafeSerialization_AndCustomConverters()
    {
        var handler = new CapturingHttpMessageHandler(_ => CreateSsrResponse());
        var gateway = CreateGateway(
            handler,
            configureInertia: options =>
            {
                options.Ssr.Enabled = true;
                options.Ssr.Url = "http://ssr.example";
                options.JsonSerializerOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = null,
                    DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
                };
                options.JsonSerializerOptions.Converters.Add(new MoneyJsonConverter());
            });

        var response = await gateway.DispatchAsync(new InertiaPage
        {
            Component = "Dashboard",
            Props = new Dictionary<string, object?>
            {
                ["userName"] = "Alice",
                ["price"] = new Money(12.34m),
            },
            Url = "/dashboard",
            Version = "tenant-version",
        });

        response.Should().NotBeNull();
        response!.Body.Should().Be("<div>SSR</div>");
        handler.RequestUri.Should().Be(new Uri("http://ssr.example/render"));

        using var doc = JsonDocument.Parse(handler.RequestBody!);
        doc.RootElement.GetProperty("component").GetString().Should().Be("Dashboard");
        doc.RootElement.GetProperty("props").GetProperty("userName").GetString().Should().Be("Alice");
        doc.RootElement.GetProperty("props").GetProperty("price").GetString().Should().Be("USD 12.34");
        doc.RootElement.GetProperty("url").GetString().Should().Be("/dashboard");
        doc.RootElement.GetProperty("version").GetString().Should().Be("tenant-version");
        doc.RootElement.TryGetProperty("Component", out _).Should().BeFalse();
    }

    [Fact]
    public async Task DispatchAsync_ShouldUseConfiguredViteHotFile_WhenPresent()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("inertianet-ssr-");

        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory.FullName, "custom.hot"),
                "http://127.0.0.1:5173");

            var handler = new CapturingHttpMessageHandler(_ => CreateSsrResponse());
            var gateway = CreateGateway(
                handler,
                configureInertia: options =>
                {
                    options.Ssr.Enabled = true;
                    options.Ssr.Url = "http://ssr.example";
                },
                configureVite: options =>
                {
                    options.PublicDirectory = tempDirectory.FullName;
                    options.HotFile = "custom.hot";
                });

            await gateway.DispatchAsync(new InertiaPage
            {
                Component = "Dashboard",
                Props = new Dictionary<string, object?>(),
                Url = "/",
            });

            handler.RequestUri.Should().Be(new Uri("http://127.0.0.1:5173/__inertia_ssr"));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task DispatchAsync_ShouldReturnNull_WhenSsrFails_AndThrowOnErrorIsDisabled()
    {
        var handler = new CapturingHttpMessageHandler(_ => throw new HttpRequestException("SSR server unavailable"));
        var gateway = CreateGateway(
            handler,
            configureInertia: options =>
            {
                options.Ssr.Enabled = true;
                options.Ssr.ThrowOnError = false;
            });

        var response = await gateway.DispatchAsync(new InertiaPage
        {
            Component = "Dashboard",
            Props = new Dictionary<string, object?>(),
            Url = "/dashboard",
        });

        response.Should().BeNull();
    }

    [Fact]
    public async Task DispatchAsync_ShouldThrowSsrException_WhenSsrFails_AndThrowOnErrorIsEnabled()
    {
        var handler = new CapturingHttpMessageHandler(_ => throw new HttpRequestException("SSR server unavailable"));
        var gateway = CreateGateway(
            handler,
            configureInertia: options =>
            {
                options.Ssr.Enabled = true;
                options.Ssr.ThrowOnError = true;
            });

        var act = () => gateway.DispatchAsync(new InertiaPage
        {
            Component = "Dashboard",
            Props = new Dictionary<string, object?>(),
            Url = "/dashboard",
        });

        var exception = await act.Should().ThrowAsync<SsrException>();
        exception.Which.ErrorType.Should().Be(SsrErrorType.Connection);
        exception.Which.Component.Should().Be("Dashboard");
    }

    private static HttpSsrGateway CreateGateway(
        HttpMessageHandler handler,
        Action<InertiaOptions>? configureInertia = null,
        Action<ViteOptions>? configureVite = null)
    {
        var inertiaOptions = new InertiaOptions();
        configureInertia?.Invoke(inertiaOptions);

        var viteOptions = new ViteOptions();
        configureVite?.Invoke(viteOptions);

        return new HttpSsrGateway(
            new FakeHttpClientFactory(handler),
            Options.Create(inertiaOptions),
            Options.Create(viteOptions),
            NullLogger<HttpSsrGateway>.Instance);
    }

    private static HttpResponseMessage CreateSsrResponse()
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"head\":[],\"body\":\"<div>SSR</div>\"}", Encoding.UTF8, "application/json"),
        };

    private sealed record Money(decimal Amount);

    private sealed class MoneyJsonConverter : JsonConverter<Money>
    {
        public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
            => writer.WriteStringValue($"USD {value.Amount.ToString("0.00", CultureInfo.InvariantCulture)}");
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CapturingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return responseFactory(request);
        }
    }
}
