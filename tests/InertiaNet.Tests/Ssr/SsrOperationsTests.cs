using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Ssr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace InertiaNet.Tests.Ssr;

public class SsrOperationsTests
{
    [Fact]
    public async Task NamedClientConfiguration_AppliesToRenderAndHealthProbe()
    {
        var paths = new List<string>();
        using var services = Services(new Handler((request, _) =>
        {
            request.Headers.GetValues("X-SSR-Key").Should().ContainSingle().Which.Should().Be("configured");
            paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(request.Method == HttpMethod.Get ? new HttpResponseMessage(HttpStatusCode.OK) : Response());
        }), configureClient: client => client.DefaultRequestHeaders.Add("X-SSR-Key", "configured"));
        var rendered = await services.GetRequiredService<ISsrGateway>().DispatchAsync(Page());
        rendered.Should().Be(new SsrResponse("<title>Page</title>", "<div id=\"app\">SSR</div>"));
        var health = await services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        health.Status.Should().Be(HealthStatus.Healthy);
        paths.Should().Equal("/render", "/health");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Timeout_FallsBackOrThrows_ClassifiedException(bool throwOnError)
    {
        using var services = Services(WaitingHandler(), options =>
        {
            options.Ssr.Timeout = TimeSpan.FromMilliseconds(25);
            options.Ssr.ThrowOnError = throwOnError;
        });
        var gateway = services.GetRequiredService<ISsrGateway>();
        var dispatch = () => gateway.DispatchAsync(Page()).WaitAsync(TimeSpan.FromSeconds(3));
        if (throwOnError) (await dispatch.Should().ThrowAsync<SsrException>()).Which.ErrorType.Should().Be(SsrErrorType.Timeout);
        else (await dispatch()).Should().BeNull();
    }

    [Fact]
    public async Task RequestCancellation_Propagates_WithoutCsrFallback()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var services = Services(new Handler(async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Response();
        }));
        using var cancellation = new CancellationTokenSource();
        var dispatch = services.GetRequiredService<ISsrGateway>().DispatchAsync(Page(), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatch);
    }

    [Theory]
    [InlineData("browser-api", SsrErrorType.BrowserApi)]
    [InlineData("component-resolution", SsrErrorType.ComponentResolution)]
    [InlineData("render", SsrErrorType.Render)]
    [InlineData("connection", SsrErrorType.Connection)]
    [InlineData("future-error", SsrErrorType.Unknown)]
    public async Task StructuredServerFailures_PreserveTheirClassification(string type, SsrErrorType expected)
    {
        using var services = Services(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent($$"""{"type":"{{type}}","error":"SSR render failed"}""", Encoding.UTF8, "application/json"),
        })), options => options.Ssr.ThrowOnError = true);
        var dispatch = () => services.GetRequiredService<ISsrGateway>().DispatchAsync(Page());
        var exception = (await dispatch.Should().ThrowAsync<SsrException>()).Which;
        exception.ErrorType.Should().Be(expected);
        exception.InnerException.Should().BeOfType<HttpRequestException>().Which.Message.Should().Be("SSR render failed");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"body\":\"rendered\",\"head\":[42]}")]
    [InlineData("{\"body\":\"rendered\",\"head\":42}")]
    public async Task InvalidSuccessPayloads_AreRenderFailures(string payload)
    {
        using var services = Services(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        })), options => options.Ssr.ThrowOnError = true);
        var dispatch = () => services.GetRequiredService<ISsrGateway>().DispatchAsync(Page());
        (await dispatch.Should().ThrowAsync<SsrException>()).Which.ErrorType.Should().Be(SsrErrorType.Render);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HealthCheck_RespectsDisabledState_AndConfiguredFailureStatus(bool enabled)
    {
        var count = 0;
        using var services = Services(new Handler((_, _) =>
        {
            count++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }), options => options.Ssr.Enabled = enabled);
        var report = await services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        report.Status.Should().Be(enabled ? HealthStatus.Degraded : HealthStatus.Healthy);
        count.Should().Be(enabled ? 1 : 0);
    }

    [Fact]
    public async Task HealthCheck_HasBoundedTimeout()
    {
        using var services = Services(WaitingHandler(), options => options.Ssr.Timeout = TimeSpan.FromMilliseconds(25));
        var report = await services.GetRequiredService<HealthCheckService>().CheckHealthAsync().WaitAsync(TimeSpan.FromSeconds(3));
        report.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task Telemetry_ReportsDurationOutcomeAndFailureType_WithoutPropValues()
    {
        using var root = new Activity("ssr-test").Start();
        var spans = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == InertiaSsr.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => { if (activity.TraceId == root.TraceId) spans.Enqueue(activity); },
        };
        ActivitySource.AddActivityListener(listener);
        var counts = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();
        var durations = new List<double>();
        using var meter = new MeterListener();
        meter.InstrumentPublished = (instrument, owner) => { if (instrument.Meter.Name == InertiaSsr.MeterName) owner.EnableMeasurementEvents(instrument); };
        meter.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            if (Activity.Current?.TraceId == root.TraceId) counts.Add((value, tags.ToArray()));
        });
        meter.SetMeasurementEventCallback<double>((_, value, _, _) => { if (Activity.Current?.TraceId == root.TraceId) durations.Add(value); });
        meter.Start();
        using var services = Services(new Handler((_, _) => throw new HttpRequestException("offline")));
        var page = Page();
        page.Props["secret"] = "must-not-be-a-telemetry-tag";
        (await services.GetRequiredService<ISsrGateway>().DispatchAsync(page)).Should().BeNull();
        spans.Should().ContainSingle();
        spans.Single().Status.Should().Be(ActivityStatusCode.Error);
        spans.Single().GetTagItem("inertia.ssr.outcome").Should().Be("fallback");
        spans.Single().GetTagItem("error.type").Should().Be("connection");
        counts.Should().ContainSingle().Which.Value.Should().Be(1);
        counts.Single().Tags.Should().BeEquivalentTo(new[]
        {
            new KeyValuePair<string, object?>("outcome", "fallback"),
            new KeyValuePair<string, object?>("error.type", "connection"),
        });
        durations.Should().ContainSingle().Which.Should().BeGreaterThanOrEqualTo(0);
        spans.Single().TagObjects.Should().NotContain(pair => Equals(pair.Value, "must-not-be-a-telemetry-tag"));
    }

    [Fact]
    public void InvalidTimeout_IsRejectedByOptionsValidation()
    {
        using var services = Services(WaitingHandler(), options => options.Ssr.Timeout = TimeSpan.Zero);
        var resolve = () => services.GetRequiredService<IOptions<InertiaOptions>>().Value;
        resolve.Should().Throw<OptionsValidationException>();
    }

    private static InertiaPage Page() => new() { Component = "SsrOperations", Url = "/", Props = new() };
    private static HttpResponseMessage Response() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"head":["<title>Page</title>"],"body":"<div id=\"app\">SSR</div>"}""", Encoding.UTF8, "application/json"),
    };
    private static Handler WaitingHandler() => new(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Response(); });
    private static ServiceProvider Services(HttpMessageHandler handler, Action<InertiaOptions>? configure = null, Action<HttpClient>? configureClient = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInertiaWithSsr(options => { options.Ssr.Url = "http://ssr.example"; configure?.Invoke(options); });
        services.AddHttpClient(InertiaSsr.HttpClientName, client => configureClient?.Invoke(client)).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddHealthChecks().AddInertiaSsr(failureStatus: HealthStatus.Degraded);
        return services.BuildServiceProvider();
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }
}
