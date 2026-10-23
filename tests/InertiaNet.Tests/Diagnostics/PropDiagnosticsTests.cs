using System.Collections.Concurrent;
using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Diagnostics;
using InertiaNet.Props;
using InertiaNet.Resolution;
using InertiaNet.Extensions;
using System.Text;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InertiaNet.Tests.Diagnostics;

public class PropDiagnosticsTests
{
    [Theory]
    [InlineData(false, "Development", true, 0)]
    [InlineData(true, "Production", true, 0)]
    [InlineData(true, "Development", true, 1)]
    [InlineData(true, "Production", false, 1)]
    public async Task Collection_IsOptIn_AndRestrictedToDevelopmentByDefault(bool enabled, string environment, bool developmentOnly, int count)
    {
        var options = new InertiaOptions { PropDiagnostics = new() { Enabled = enabled, DevelopmentOnly = developmentOnly } };
        using var services = Services(options, environment);
        var context = new DefaultHttpContext();
        var resolver = new PropsResolver(context, "Page", services);
        await resolver.ResolveAsync([], new() { ["name"] = "private value" });
        context.GetInertiaPropDiagnostics().Should().HaveCount(count);
    }

    [Fact]
    public async Task InitialLoad_ClassifiesWrappersCallbacksAndCachedProps_WithoutCapturingValues()
    {
        var log = new RecordingLogger();
        using var services = Services(new() { PropDiagnostics = new() { Enabled = true, SlowPropThreshold = TimeSpan.Zero } }, logger: log);
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new EndpointNameMetadata("page.index")), "page"));
        context.Request.Headers["X-Inertia"] = "true";
        context.Request.Headers["X-Inertia-Except-Once-Props"] = "cache-key";
        var called = 0;
        await new PropsResolver(context, "Page", services).ResolveAsync([], new()
        {
            ["name"] = "do-not-record-this-value",
            ["deferred"] = new DeferProp((_, _) => Task.FromResult<object?>(++called), "slow"),
            ["optional"] = new OptionalProp((_, _) => Task.FromResult<object?>(++called)),
            ["once"] = new OnceProp((_, _) => Task.FromResult<object?>(++called)).As("cache-key"),
            ["returned"] = (Func<object?>)(() => new DeferProp((_, _) => Task.FromResult<object?>(++called))),
            ["lazy"] = (Func<object?>)(() => "do-not-record-this-value"),
            ["nested"] = new Dictionary<string, object?> { ["child"] = (Func<object?>)(() => 42) },
        });
        called.Should().Be(0);
        var diagnostics = context.GetInertiaPropDiagnostics();
        diagnostics.Should().Contain(entry => entry.Path == "name" && entry.Kind == "value" && entry.Outcome == "resolved" && entry.IsSlow);
        diagnostics.Should().Contain(entry => entry.Path == "deferred" && entry.Kind == "deferred" && entry.Outcome == "deferred");
        diagnostics.Single(entry => entry.Path == "deferred").DeferredGroup.Should().Be("slow");
        diagnostics.Single(entry => entry.Path == "once").OnceKey.Should().Be("cache-key");
        diagnostics.Should().OnlyContain(entry => entry.RouteName == "page.index");
        diagnostics.Should().Contain(entry => entry.Path == "optional" && entry.Outcome == "optional");
        diagnostics.Should().Contain(entry => entry.Path == "once" && entry.Outcome == "cached");
        diagnostics.Should().Contain(entry => entry.Path == "returned" && entry.Kind == "deferred" && entry.Outcome == "deferred");
        diagnostics.Should().Contain(entry => entry.Path == "nested.child" && entry.Kind == "callback" && entry.Outcome == "resolved");
        diagnostics.Should().OnlyContain(entry => entry.Duration >= TimeSpan.Zero);
        log.Entries.Should().NotBeEmpty().And.NotContain(entry => entry.Contains("do-not-record-this-value"));
    }

    [Fact]
    public async Task JsonRender_CountsActualBytes_WithoutEvaluatingFactoriesAgain()
    {
        var collection = new ServiceCollection().AddLogging();
        collection.AddInertia(options => { options.PropDiagnostics.Enabled = true; options.PropDiagnostics.DevelopmentOnly = false; });
        using var services = collection.BuildServiceProvider();
        using var scope = services.CreateScope();
        using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new EndpointNameMetadata("page.index")), "page"));
        context.Request.Headers["X-Inertia"] = "true";
        context.Response.Body = body;
        var calls = 0;
        await scope.ServiceProvider.GetRequiredService<IInertiaService>()
            .Render("Page", new { value = (Func<object?>)(() => { calls++; return "private 日本語"; }) }).ExecuteAsync(context);
        calls.Should().Be(1);
        context.Response.Body.Should().BeSameAs(body);
        var render = context.GetInertiaRenderDiagnostics()!;
        render.BytesWritten.Should().Be(body.Length).And.BeGreaterThan(0);
        render.RouteName.Should().Be("page.index");
        render.Format.Should().Be("json");
        render.Component.Should().Be("Page");
        render.Succeeded.Should().BeTrue();
        render.ToString().Should().NotContain("private");
        body.CanWrite.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenderScope_CountsHtmlWrites_AndRestoresTheStreamOnFailures(bool fail)
    {
        using var services = Services(new() { PropDiagnostics = new() { Enabled = true } });
        using var body = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;
        var trace = PropDiagnosticsTrace.Create(context, services, new() { Enabled = true, DevelopmentOnly = false })!;
        const string html = "<p>Olá 日本語</p>";
        var calls = 0;
        Func<Task> render = () => trace.MeasureRenderAsync(context, "Page", "html", async () =>
        {
            calls++;
            await context.Response.WriteAsync(html);
            if (fail) throw new InvalidOperationException("render failure");
        });
        if (fail) await render.Should().ThrowAsync<InvalidOperationException>();
        else await render();
        calls.Should().Be(1);
        context.Response.Body.Should().BeSameAs(body);
        body.CanWrite.Should().BeTrue();
        var snapshot = context.GetInertiaRenderDiagnostics()!;
        snapshot.BytesWritten.Should().Be(Encoding.UTF8.GetByteCount(html));
        snapshot.Succeeded.Should().Be(!fail);
        snapshot.Duration.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task PartialRequests_ReportExclusionsAndRescuedFailures()
    {
        using var services = Services(new() { PropDiagnostics = new() { Enabled = true } });
        var context = Partial("rescued");
        await new PropsResolver(context, "Page", services).ResolveAsync([], new()
        {
            ["excluded"] = (Func<object?>)(() => throw new InvalidOperationException("must not run")),
            ["rescued"] = new DeferProp((_, _) => throw new InvalidOperationException("failure")).Rescue(),
        });
        context.GetInertiaPropDiagnostics().Should().Contain(entry => entry.Path == "excluded" && entry.Outcome == "excluded")
            .And.Contain(entry => entry.Path == "rescued" && entry.Outcome == "rescued");
    }

    [Theory]
    [InlineData(false, "failed")]
    [InlineData(true, "cancelled")]
    public async Task FailedAndCancelledResolution_RemainObservable_AndPropagate(bool cancelled, string outcome)
    {
        using var services = Services(new() { PropDiagnostics = new() { Enabled = true } });
        var context = new DefaultHttpContext();
        var resolver = new PropsResolver(context, "Page", services);
        Func<object?> callback = cancelled ? () => throw new OperationCanceledException() : () => throw new InvalidOperationException();
        var resolve = () => resolver.ResolveAsync([], new() { ["failure"] = callback });
        await resolve.Should().ThrowAsync<Exception>();
        context.GetInertiaPropDiagnostics().Should().ContainSingle().Which.Outcome.Should().Be(outcome);
    }

    [Fact]
    public async Task EntryLimit_RetainsParentTimings_WhenNestedCollectionsAreLarge()
    {
        using var services = Services(new() { PropDiagnostics = new() { Enabled = true, MaxEntries = 2 } });
        var context = new DefaultHttpContext();
        await new PropsResolver(context, "Page", services).ResolveAsync([], new() { ["rows"] = Enumerable.Range(0, 100).ToArray() });
        var entries = context.GetInertiaPropDiagnostics();
        entries.Should().HaveCount(2);
        entries[0].Path.Should().Be("rows");
        entries[0].Outcome.Should().Be("resolved");
        entries[1].Path.Should().Be("rows.0");
    }

    private static DefaultHttpContext Partial(string only)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Inertia"] = "true";
        context.Request.Headers["X-Inertia-Partial-Component"] = "Page";
        context.Request.Headers["X-Inertia-Partial-Data"] = only;
        return context;
    }
    private static ServiceProvider Services(InertiaOptions options, string environment = "Development", RecordingLogger? logger = null)
    {
        var services = new ServiceCollection();
        services.AddOptions<InertiaOptions>().Configure(value => value.PropDiagnostics = options.PropDiagnostics);
        services.AddSingleton<IHostEnvironment>(new TestEnvironment { EnvironmentName = environment });
        services.AddLogging(builder => { builder.SetMinimumLevel(LogLevel.Debug); if (logger is not null) builder.AddProvider(logger); });
        return services.BuildServiceProvider();
    }
    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class RecordingLogger : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Enqueue(formatter(state, exception));
    }
}
