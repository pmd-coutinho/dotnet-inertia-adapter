using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Props;
using InertiaNet.Resolution;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaNet.Tests.Resolution;

public class TypedFactoryPropsTests
{
    [Fact]
    public async Task TypedFactoriesTasksValueTasksAndLazyValues_ResolveToTheirWireValues()
    {
        using var services = new ServiceCollection().AddOptions().BuildServiceProvider();
        var resolver = new PropsResolver(new DefaultHttpContext(), "Page", services);
        var (props, _) = await resolver.ResolveAsync([], new()
        {
            ["integer"] = (Func<int>)(() => 1),
            ["async"] = (Func<Task<int>>)(() => Task.FromResult(2)),
            ["valueTask"] = new ValueTask<int>(3),
            ["task"] = Task.FromResult(new ValueDto(4)),
            ["lazy"] = new Lazy<ValueDto>(() => new(5)),
            ["services"] = (Func<IServiceProvider, CancellationToken, Task<ValueDto>>)((provider, _) => Task.FromResult(new ValueDto(ReferenceEquals(provider, services) ? 6 : -1))),
            ["nested"] = new AlwaysProp(new Lazy<Func<Task<int>>>(() => () => Task.FromResult(7))),
        });
        var json = JsonSerializer.SerializeToElement(props);
        json.GetProperty("integer").GetInt32().Should().Be(1);
        json.GetProperty("async").GetInt32().Should().Be(2);
        json.GetProperty("valueTask").GetInt32().Should().Be(3);
        json.GetProperty("task").GetProperty("value").GetInt32().Should().Be(4);
        json.GetProperty("lazy").GetProperty("value").GetInt32().Should().Be(5);
        json.GetProperty("services").GetProperty("value").GetInt32().Should().Be(6);
        json.GetProperty("nested").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task ExcludedWrappers_DoNotEvaluateTheirTypedLazyResults()
    {
        using var services = new ServiceCollection().AddOptions().BuildServiceProvider();
        var called = 0;
        var values = new Dictionary<string, object?>
        {
            ["optional"] = new OptionalProp((_, _) => Task.FromResult<object?>(new Lazy<int>(() => ++called))),
            ["deferred"] = new DeferProp((_, _) => Task.FromResult<object?>(new Lazy<int>(() => ++called))),
        };
        (await new PropsResolver(new DefaultHttpContext(), "Page", services).ResolveAsync([], values)).Props.Should().BeEmpty();
        called.Should().Be(0);
        var context = Partial("optional");
        var (props, _) = await new PropsResolver(context, "Page", services).ResolveAsync([], values);
        props["optional"].Should().Be(1);
        called.Should().Be(1);
    }

    [Fact]
    public async Task FactoryFailures_KeepTheOriginalException_AndCanBeRescued()
    {
        using var services = new ServiceCollection().AddOptions().BuildServiceProvider();
        var failure = new InvalidOperationException("original failure");
        var resolver = new PropsResolver(new DefaultHttpContext(), "Page", services);
        var resolve = () => resolver.ResolveAsync([], new() { ["value"] = new Lazy<int>(() => throw failure) });
        (await resolve.Should().ThrowExactlyAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        var rescued = new DeferProp((_, _) => Task.FromResult<object?>((Func<int>)(() => throw failure))).Rescue();
        var (props, metadata) = await new PropsResolver(Partial("value"), "Page", services).ResolveAsync([], new() { ["value"] = rescued });
        props.Should().BeEmpty();
        metadata.RescuedProps.Should().Equal("value");
    }

    [Fact]
    public async Task WaitingForAnExistingTask_ObservesRequestCancellation()
    {
        using var services = new ServiceCollection().AddOptions().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolving = new PropsResolver(new DefaultHttpContext(), "Page", services).ResolveAsync([], new() { ["value"] = source.Task }, cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolving);
        source.SetResult(1);
    }

    [Fact]
    public async Task GenericReadOnlyDictionaries_KeepTheirObjectShape_AndResolveTypedValues()
    {
        using var services = new ServiceCollection().AddOptions().BuildServiceProvider();
        var value = new ReadOnlyValues(new() { ["first"] = () => 1, ["second"] = () => 2 });
        var (props, _) = await new PropsResolver(new DefaultHttpContext(), "Page", services).ResolveAsync([], new() { ["lookup"] = value });
        var json = JsonSerializer.SerializeToElement(props);
        json.GetProperty("lookup").GetProperty("first").GetInt32().Should().Be(1);
        json.GetProperty("lookup").GetProperty("second").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ShareProps_UsesSerializerMetadata_AndKeepsTypedFactoriesLazy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInertia();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var called = 0;
        var inertia = scope.ServiceProvider.GetRequiredService<IInertiaService>();
        inertia.ShareProps(new SharedDto { Count = () => ++called });
        called.Should().Be(0);
        inertia.GetSharedProps().Should().ContainKey("shared-count").And.NotContainKey("hidden");
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers["X-Inertia"] = "true";
        context.Response.Body = new MemoryStream();
        await inertia.Render("Page").ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        json.RootElement.GetProperty("props").GetProperty("shared-count").GetInt32().Should().Be(1);
    }

    private static DefaultHttpContext Partial(string only)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Inertia"] = "true";
        context.Request.Headers["X-Inertia-Partial-Component"] = "Page";
        context.Request.Headers["X-Inertia-Partial-Data"] = only;
        return context;
    }
    private sealed record ValueDto(int Value);
    private sealed class SharedDto
    {
        [JsonPropertyName("shared-count")] public Func<int> Count { get; init; } = () => 0;
        [JsonIgnore] public string Hidden => throw new InvalidOperationException();
    }
    private sealed class ReadOnlyValues(Dictionary<string, Func<int>> values) : IReadOnlyDictionary<string, Func<int>>
    {
        public Func<int> this[string key] => values[key];
        public IEnumerable<string> Keys => values.Keys;
        public IEnumerable<Func<int>> Values => values.Values;
        public int Count => values.Count;
        public bool ContainsKey(string key) => values.ContainsKey(key);
        public bool TryGetValue(string key, out Func<int> value) => values.TryGetValue(key, out value!);
        public IEnumerator<KeyValuePair<string, Func<int>>> GetEnumerator() => values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
