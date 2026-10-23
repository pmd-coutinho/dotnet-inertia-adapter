using System.Collections.ObjectModel;
using System.Text.Json;
using FluentAssertions;
using InertiaNet.Context;
using InertiaNet.Core;
using InertiaNet.Interfaces;
using InertiaNet.Props;
using InertiaNet.Resolution;
using InertiaNet.Support;
using InertiaNet.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InertiaNet.Tests.Resolution;

public class PropsResolverConformanceTests
{
    [Fact]
    public async Task Except_TakesPrecedenceOverOnly_WithoutDroppingParents()
    {
        var ctx = HttpContextHelper.CreateInertia(partialComponent: "Page", partialOnly: "auth,title", partialExcept: "auth.password");
        var (props, _) = await new PropsResolver(ctx, "Page", ctx.RequestServices).ResolveAsync([], new()
        {
            ["auth"] = new Dictionary<string, object?> { ["name"] = "Ana", ["password"] = "secret" },
            ["title"] = "Home", ["extra"] = "excluded",
        });
        props.Keys.Should().BeEquivalentTo("auth", "title");
        ((Dictionary<string, object?>)props["auth"]!).Should().Equal(new Dictionary<string, object?> { ["name"] = "Ana" });
    }

    [Fact]
    public async Task ExcludedCallbacks_AreNeverInvoked()
    {
        var ctx = HttpContextHelper.CreateInertia(partialComponent: "Page", partialOnly: "a,b", partialExcept: "b");
        var (props, _) = await new PropsResolver(ctx, "Page", ctx.RequestServices).ResolveAsync([], new()
        { ["a"] = 1, ["b"] = (Func<object?>)(() => throw new Exception("Excluded callback ran")) });
        props.Keys.Should().Equal("a");
    }

    [Fact]
    public async Task ReturnedDeferredWrapper_IsExcludedAndContributesMetadata()
    {
        var ctx = HttpContextHelper.CreateInertia();
        var callbackRuns = 0;
        var (props, meta) = await new PropsResolver(ctx, "Page", ctx.RequestServices).ResolveAsync([], new()
        {
            ["items"] = (Func<object?>)(() => new DeferProp((_, _) => { callbackRuns++; return Task.FromResult<object?>(new[] { 1 }); })
                .Merge().MatchOn("id").Once().As("cached-items")),
        });
        props.Should().BeEmpty();
        callbackRuns.Should().Be(0);
        meta.DeferredProps!["default"].Should().Equal("items");
        meta.MergeProps.Should().Equal("items");
        meta.MatchPropsOn.Should().Equal("items.id");
        JsonSerializer.SerializeToElement(meta.OnceProps!["cached-items"]).GetProperty("prop").GetString().Should().Be("items");
    }

    [Fact]
    public async Task LoadedDeferredOnce_DoesNotScheduleAnotherFetch()
    {
        var ctx = HttpContextHelper.CreateInertia(exceptOnceProps: "cache");
        var (props, meta) = await new PropsResolver(ctx, "Page", ctx.RequestServices).ResolveAsync([], new()
        { ["items"] = new DeferProp((_, _) => throw new Exception("Must not run")).Once().As("cache") });
        props.Should().BeEmpty(); meta.DeferredProps.Should().BeNull(); meta.OnceProps.Should().ContainKey("cache");
    }

    [Fact]
    public async Task PartialReload_ExplicitlyRefreshesLoadedOnce()
    {
        var ctx = HttpContextHelper.CreateInertia(partialComponent: "Page", partialOnly: "items", exceptOnceProps: "items");
        var (props, _) = await new PropsResolver(ctx, "Page", ctx.RequestServices).ResolveAsync([], new()
        { ["items"] = new DeferProp((_, _) => Task.FromResult<object?>(42)).Once() });
        props["items"].Should().Be(42);
    }

    [Fact]
    public async Task OrdinaryHtml_IgnoresTheClientsOnceExclusionHeader()
    {
        var ctx = HttpContextHelper.Create(headers: new() { [HeaderNames.ExceptOnceProps] = "items" });
        var (props, _) = await new PropsResolver(ctx, "Page", ctx.RequestServices).ResolveAsync([], new()
        { ["items"] = new OnceProp((_, _) => Task.FromResult<object?>(42)) });
        props["items"].Should().Be(42);
    }

    [Fact]
    public async Task ReturnedMergeWrapper_UsesItsOwnMetadata()
    {
        var ctx = HttpContextHelper.CreateInertia();
        var (_, meta) = await new PropsResolver(ctx, "Page", ctx.RequestServices).ResolveAsync([], new()
        { ["items"] = (Func<object?>)(() => new MergeProp(new[] { 1 }).DeepMerge().MatchOn("id")) });
        meta.DeepMergeProps.Should().Equal("items");
        meta.MergeProps.Should().BeNull(); meta.PrependProps.Should().BeNull(); meta.MatchPropsOn.Should().Equal("items.id");
    }

    [Fact]
    public async Task AlwaysAndResolvedParentValues_KeepTheirChildrenDuringPartialReloads()
    {
        var ctx = HttpContextHelper.CreateInertia(partialComponent: "Page", partialOnly: "title,computed.name");
        var (props, _) = await new PropsResolver(ctx, "Page", ctx.RequestServices).ResolveAsync([], new()
        {
            ["title"] = "Home", ["errors"] = new AlwaysProp(new Dictionary<string, object?> { ["email"] = "Required" }),
            ["computed"] = (Func<object?>)(() => new Dictionary<string, object?> { ["name"] = "Ana", ["role"] = "admin" }),
        });
        ((Dictionary<string, object?>)props["errors"]!).Should().ContainKey("email");
        ((Dictionary<string, object?>)props["computed"]!).Keys.Should().BeEquivalentTo("name", "role");
    }

    [Fact]
    public async Task SharedKeys_AreTopLevelAndUnique()
    {
        var ctx = HttpContextHelper.CreateInertia();
        var (_, meta) = await new PropsResolver(ctx, "Page", ctx.RequestServices).ResolveAsync(new()
        { ["auth.user"] = "Ana", ["auth.permissions"] = new[] { "read" } }, []);
        meta.SharedPropKeys.Should().Equal("auth");
    }

    [Fact]
    public async Task NestedProvidersAndDictionaries_ReceiveTheFullPath()
    {
        var ctx = HttpContextHelper.CreateInertia();
        var (props, _) = await new PropsResolver(ctx, "Page", ctx.RequestServices).ResolveAsync([], new()
        {
            ["settings"] = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?> { ["provider"] = new NestedProvider() }),
            ["labels"] = new Dictionary<string, string> { ["hello"] = "Olá" },
        });
        var settings = (Dictionary<string, object?>)props["settings"]!;
        settings["path"].Should().Be("settings.path");
        ((Dictionary<string, object?>)props["labels"]!)["hello"].Should().Be("Olá");
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("nested")]
    [InlineData("page")]
    public async Task ScrollMetadata_UsesTheProviderInterfaceAtEveryLocation(string location)
    {
        var ctx = HttpContextHelper.CreateInertia(extraHeaders: new() { [HeaderNames.Reset] = "nested.items,items" });
        var scroll = new ScrollProp(new { data = new[] { 1 } }, metadata: new CursorMetadata());
        var resolver = new PropsResolver(ctx, "Page", ctx.RequestServices);
        var (_, meta) = await resolver.ResolveAsync(location == "shared" ? new() { ["items"] = scroll } : [],
            location == "nested" ? new() { ["nested"] = new Dictionary<string, object?> { ["items"] = scroll } }
            : location == "page" ? new() { ["items"] = scroll } : []);
        var key = location == "nested" ? "nested.items" : "items";
        var metadata = JsonSerializer.SerializeToElement(meta.ScrollProps![key]);
        metadata.GetProperty("pageName").GetString().Should().Be("cursor");
        metadata.GetProperty("nextPage").GetString().Should().Be("next");
        metadata.GetProperty("reset").GetBoolean().Should().BeTrue();
        meta.MergeProps.Should().BeNull();
    }

    [Fact]
    public async Task DeferredScroll_UsesItsGroupAndWrapperMergePath()
    {
        var ctx = HttpContextHelper.CreateInertia();
        var (_, meta) = await new PropsResolver(ctx, "Page", ctx.RequestServices).ResolveAsync([], new()
        { ["feed"] = new ScrollProp(new { data = new[] { 1 } }).Defer("feed-group") });
        meta.DeferredProps!["feed-group"].Should().Equal("feed");
        meta.MergeProps.Should().Equal("feed.data");
    }

    [Fact]
    public async Task ScrollMergeIntent_DoesNotAccumulateAcrossResolutions()
    {
        var ctx = HttpContextHelper.CreateInertia();
        var scroll = new ScrollProp(new { data = new[] { 1 } });
        scroll.ConfigureMergeIntent(ctx);
        ctx.Request.Headers[HeaderNames.InfiniteScrollMergeIntent] = "prepend";
        var (_, meta) = await new PropsResolver(ctx, "Page", ctx.RequestServices).ResolveAsync([], new() { ["feed"] = scroll });
        meta.MergeProps.Should().BeNull(); meta.PrependProps.Should().Equal("feed.data");
    }

    [Fact]
    public async Task RescuedDeferredFailure_LeavesOtherPropsAndLogsTheFailure()
    {
        var logger = new RecordingLogger();
        var services = new ServiceCollection().AddSingleton<ILogger<PropsResolver>>(logger).BuildServiceProvider();
        var ctx = HttpContextHelper.CreateInertia(partialComponent: "Page", partialOnly: "items,title");
        var (props, meta) = await new PropsResolver(ctx, "Page", services).ResolveAsync([], new()
        { ["items"] = new DeferProp((_, _) => throw new InvalidOperationException("Failed")).Rescue(), ["title"] = "Home" });
        props.Keys.Should().Equal("title"); meta.RescuedProps.Should().Equal("items"); logger.Exception.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task DeferredFailures_PropagateUnlessRescueIsEnabled()
    {
        var ctx = HttpContextHelper.CreateInertia(partialComponent: "Page", partialOnly: "items");
        var action = () => new PropsResolver(ctx, "Page", ctx.RequestServices).ResolveAsync([], new()
        { ["items"] = new DeferProp((_, _) => throw new InvalidOperationException("Failed")) });
        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Rescue_NeverSwallowsCancellation()
    {
        var ctx = HttpContextHelper.CreateInertia(partialComponent: "Page", partialOnly: "items");
        var action = () => new PropsResolver(ctx, "Page", ctx.RequestServices).ResolveAsync([], new()
        { ["items"] = new DeferProp((_, _) => throw new OperationCanceledException()).Rescue() });
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task TypedNestedObjects_ResolveWrappersWithoutLosingSerializedNames()
    {
        var ctx = HttpContextHelper.CreateInertia();
        var (props, meta) = await new PropsResolver(ctx, "Page", ctx.RequestServices).ResolveAsync([], new()
        { ["nested"] = new NestedDto { Stats = new DeferProp((_, _) => throw new Exception("Must not run")) } });
        ((Dictionary<string, object?>)props["nested"]!).Keys.Should().Equal("display_name");
        meta.DeferredProps!["default"].Should().Equal("nested.stats");
    }

    [Fact]
    public async Task TypedNestedObjects_RespectPartialExclusions()
    {
        var ctx = HttpContextHelper.CreateInertia(partialComponent: "Page", partialExcept: "nested.stats");
        var (props, _) = await new PropsResolver(ctx, "Page", ctx.RequestServices).ResolveAsync([], new()
        { ["nested"] = new NestedDto { Stats = new DeferProp((_, _) => throw new Exception("Must not run")) } });
        ((Dictionary<string, object?>)props["nested"]!).Keys.Should().Equal("display_name");
    }

    [Fact]
    public async Task CyclicProps_FailWithJsonExceptionInsteadOfRecursingIndefinitely()
    {
        var ctx = HttpContextHelper.CreateInertia();
        var nested = new Dictionary<string, object?>(); nested["self"] = nested;
        var action = () => new PropsResolver(ctx, "Page", ctx.RequestServices).ResolveAsync([], new() { ["nested"] = nested });
        await action.Should().ThrowAsync<JsonException>();
    }

    private sealed class NestedDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("display_name")] public string Name { get; set; } = "Ana";
        public DeferProp? Stats { get; set; }
    }

    private sealed class CursorMetadata : IProvidesScrollMetadata
    {
        public string GetPageName() => "cursor";
        public object? GetPreviousPage() => null;
        public object GetNextPage() => "next";
        public object GetCurrentPage() => "current";
    }
    private sealed class NestedProvider : IProvidesInertiaProperties
    {
        public IEnumerable<KeyValuePair<string, object?>> ToInertiaProperties(RenderContext context)
            => new Dictionary<string, object?> { ["path"] = new PathProvider() };
    }
    private sealed class PathProvider : IProvidesInertiaProperty
    { public object ToInertiaProperty(PropertyContext context) => context.Key; }
    private sealed class RecordingLogger : ILogger<PropsResolver>
    {
        public Exception? Exception { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Exception = exception;
    }
}
