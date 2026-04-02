using FluentAssertions;
using InertiaNet.Props;
using InertiaNet.Interfaces;
using InertiaNet.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaNet.Tests.Props;

public class PropTypeTests
{
    private static readonly IServiceProvider EmptyServices = new ServiceCollection().BuildServiceProvider();

    // ── AlwaysProp ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AlwaysProp_ShouldResolveStaticValue()
    {
        var prop = new AlwaysProp("hello");

        var result = await prop.ResolveAsync(EmptyServices);

        result.Should().Be("hello");
    }

    [Fact]
    public async Task AlwaysProp_ShouldResolveAsyncCallback()
    {
        var prop = new AlwaysProp(async (sp, ct) => await Task.FromResult<object?>(42));

        var result = await prop.ResolveAsync(EmptyServices);

        result.Should().Be(42);
    }

    // ── OptionalProp ───────────────────────────────────────────────────────

    [Fact]
    public void OptionalProp_ShouldImplementIIgnoreFirstLoad()
    {
        var prop = new OptionalProp(async (sp, ct) => "data");

        prop.Should().BeAssignableTo<IIgnoreFirstLoad>();
    }

    [Fact]
    public void OptionalProp_ShouldNotBeOnceByDefault()
    {
        var prop = new OptionalProp(async (sp, ct) => "data");

        prop.ShouldResolveOnce().Should().BeFalse();
    }

    [Fact]
    public void OptionalProp_ShouldSupportOnceWithTtl()
    {
        var prop = new OptionalProp(async (sp, ct) => "data")
            .Once()
            .Until(TimeSpan.FromMinutes(5));

        prop.ShouldResolveOnce().Should().BeTrue();
        prop.ExpiresAt().Should().NotBeNull();
        prop.ExpiresAt().Should().BeGreaterThan(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    // ── DeferProp ──────────────────────────────────────────────────────────

    [Fact]
    public void DeferProp_ShouldImplementIIgnoreFirstLoadAndIDeferrable()
    {
        var prop = new DeferProp(async (sp, ct) => "data");

        prop.Should().BeAssignableTo<IIgnoreFirstLoad>();
        prop.Should().BeAssignableTo<IDeferrable>();
        prop.ShouldDefer().Should().BeTrue();
    }

    [Fact]
    public void DeferProp_ShouldUseDefaultGroupWhenNoneSpecified()
    {
        var prop = new DeferProp(async (sp, ct) => "data");

        prop.Group().Should().Be("default");
    }

    [Fact]
    public void DeferProp_ShouldUseCustomGroup()
    {
        var prop = new DeferProp(async (sp, ct) => "data", group: "charts");

        prop.Group().Should().Be("charts");
    }

    [Fact]
    public void DeferProp_Merge_ShouldSetMergeAndAppendAtRoot()
    {
        var prop = new DeferProp(async (sp, ct) => new[] { 1, 2, 3 }).Merge();

        prop.ShouldMerge().Should().BeTrue();
        prop.AppendsAtRoot().Should().BeTrue();
        prop.ShouldDeepMerge().Should().BeFalse();
    }

    [Fact]
    public void DeferProp_DeepMerge_ShouldSetDeepMergeFlag()
    {
        var prop = new DeferProp(async (sp, ct) => new { a = 1 }).DeepMerge();

        prop.ShouldMerge().Should().BeTrue();
        prop.ShouldDeepMerge().Should().BeTrue();
    }

    // ── MergeProp ──────────────────────────────────────────────────────────

    [Fact]
    public void MergeProp_ShouldMergeByDefault()
    {
        var prop = new MergeProp(new[] { 1, 2 });

        prop.ShouldMerge().Should().BeTrue();
        prop.AppendsAtRoot().Should().BeTrue();
    }

    [Fact]
    public void MergeProp_Append_ShouldAddPathAndDisableRoot()
    {
        var prop = new MergeProp(new[] { 1 }).Append("items");

        prop.AppendsAtRoot().Should().BeFalse();
        prop.AppendsAtPaths().Should().Contain("items");
    }

    [Fact]
    public void MergeProp_Prepend_ShouldSetPrependAtRoot()
    {
        var prop = new MergeProp(new[] { 1 }).Prepend();

        prop.PrependsAtRoot().Should().BeTrue();
        prop.AppendsAtRoot().Should().BeFalse();
    }

    [Fact]
    public void MergeProp_MatchOn_ShouldSetDeduplicationKeys()
    {
        var prop = new MergeProp(new[] { 1 }).MatchOn("id");

        prop.MatchesOn().Should().ContainSingle().Which.Should().Be("id");
    }

    // ── OnceProp ───────────────────────────────────────────────────────────

    [Fact]
    public void OnceProp_ShouldAlwaysResolveOnce()
    {
        var prop = new OnceProp(async (sp, ct) => "cached");

        prop.ShouldResolveOnce().Should().BeTrue();
    }

    [Fact]
    public void OnceProp_Fresh_ShouldSetRefreshFlag()
    {
        var prop = new OnceProp(async (sp, ct) => "cached").Fresh();

        prop.ShouldBeRefreshed().Should().BeTrue();
    }

    [Fact]
    public void OnceProp_Until_ShouldSetExpiresAtFromTimeSpan()
    {
        var before = DateTimeOffset.UtcNow.Add(TimeSpan.FromHours(1)).ToUnixTimeMilliseconds();
        var prop = new OnceProp(async (sp, ct) => "cached").Until(TimeSpan.FromHours(1));
        var after = DateTimeOffset.UtcNow.Add(TimeSpan.FromHours(1)).ToUnixTimeMilliseconds();

        prop.ExpiresAt().Should().NotBeNull();
        prop.ExpiresAt()!.Value.Should().BeInRange(before - 1000, after + 1000);
    }

    [Fact]
    public void OnceProp_As_ShouldSetCustomKey()
    {
        var prop = new OnceProp(async (sp, ct) => "cached").As("my-key");

        prop.GetKey().Should().Be("my-key");
    }
}
