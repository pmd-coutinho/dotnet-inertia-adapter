using FluentAssertions;
using InertiaNet.Context;
using InertiaNet.Interfaces;
using InertiaNet.Props;
using InertiaNet.Resolution;
using InertiaNet.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaNet.Tests.Resolution;

public class PropsResolverTests
{
    private static readonly IServiceProvider EmptyServices = new ServiceCollection().BuildServiceProvider();

    // ── Partial reload: only filtering ────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ShouldOnlyIncludeRequestedProps_WhenPartialOnlyHeaderSet()
    {
        var context = HttpContextHelper.CreateInertia(
            partialComponent: "Users/Index",
            partialOnly: "name");

        var resolver = new PropsResolver(context, "Users/Index", EmptyServices);
        var (props, _) = await resolver.ResolveAsync(
            sharedProps: new(),
            pageProps: new()
            {
                ["name"] = "Alice",
                ["email"] = "alice@example.com"
            });

        props.Should().ContainKey("name");
        props.Should().NotContainKey("email");
    }

    // ── Partial reload: except filtering ──────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ShouldExcludeSpecifiedProps_WhenPartialExceptHeaderSet()
    {
        var context = HttpContextHelper.CreateInertia(
            partialComponent: "Users/Index",
            partialExcept: "email");

        var resolver = new PropsResolver(context, "Users/Index", EmptyServices);
        var (props, _) = await resolver.ResolveAsync(
            sharedProps: new(),
            pageProps: new()
            {
                ["name"] = "Alice",
                ["email"] = "alice@example.com"
            });

        props.Should().ContainKey("name");
        props.Should().NotContainKey("email");
    }

    // ── AlwaysProp bypasses partial filtering ─────────────────────────────

    [Fact]
    public async Task ResolveAsync_ShouldIncludeAlwaysProp_EvenWhenNotInOnlyList()
    {
        var context = HttpContextHelper.CreateInertia(
            partialComponent: "Users/Index",
            partialOnly: "name");

        var resolver = new PropsResolver(context, "Users/Index", EmptyServices);
        var (props, _) = await resolver.ResolveAsync(
            sharedProps: new()
            {
                ["errors"] = new AlwaysProp(new { email = "required" })
            },
            pageProps: new()
            {
                ["name"] = "Alice",
                ["age"] = 30
            });

        props.Should().ContainKey("errors");
        props.Should().ContainKey("name");
        props.Should().NotContainKey("age");
    }

    // ── Deferred prop group collection ────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ShouldCollectDeferredPropGroups_OnInitialLoad()
    {
        var context = HttpContextHelper.CreateInertia(); // initial load (no partial component)

        var resolver = new PropsResolver(context, "Dashboard", EmptyServices);
        var (props, metadata) = await resolver.ResolveAsync(
            sharedProps: new(),
            pageProps: new()
            {
                ["title"] = "Dashboard",
                ["charts"] = new DeferProp(async (sp, ct) => new[] { 1, 2, 3 }, group: "charts"),
                ["stats"] = new DeferProp(async (sp, ct) => new { count = 5 }, group: "stats")
            });

        // Deferred props excluded from initial response
        props.Should().NotContainKey("charts");
        props.Should().NotContainKey("stats");
        props.Should().ContainKey("title");

        // Metadata should list deferred groups
        metadata.DeferredProps.Should().NotBeNull();
        metadata.DeferredProps!.Should().ContainKey("charts");
        metadata.DeferredProps!.Should().ContainKey("stats");
        metadata.DeferredProps!["charts"].Should().Contain("charts");
        metadata.DeferredProps!["stats"].Should().Contain("stats");
    }

    // ── Once-prop exclusion via header ────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ShouldExcludeLoadedOnceProps_WhenExceptOncePropsHeaderSet()
    {
        // The client tells us it already has "countries" cached
        var context = HttpContextHelper.CreateInertia(exceptOnceProps: "countries");

        var resolver = new PropsResolver(context, "Settings", EmptyServices);
        var (props, metadata) = await resolver.ResolveAsync(
            sharedProps: new(),
            pageProps: new()
            {
                ["countries"] = new OnceProp(async (sp, ct) => new[] { "UK", "US" }),
                ["name"] = "Test"
            });

        props.Should().NotContainKey("countries");
        props.Should().ContainKey("name");

        // Once metadata should still be present (tells client it's still valid)
        metadata.OnceProps.Should().NotBeNull();
        metadata.OnceProps!.Should().ContainKey("countries");
    }

    // ── Merge metadata collection ─────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ShouldCollectMergeMetadata_WhenMergePropsUsed()
    {
        var context = HttpContextHelper.CreateInertia(
            partialComponent: "Users/Index",
            partialOnly: "items");

        var resolver = new PropsResolver(context, "Users/Index", EmptyServices);
        var (props, metadata) = await resolver.ResolveAsync(
            sharedProps: new(),
            pageProps: new()
            {
                ["items"] = new MergeProp(new[] { 1, 2, 3 })
            });

        props.Should().ContainKey("items");
        metadata.MergeProps.Should().NotBeNull();
        metadata.MergeProps!.Should().Contain("items");
    }

    [Fact]
    public async Task ResolveAsync_ShouldCollectDeepMergeMetadata()
    {
        var context = HttpContextHelper.CreateInertia(
            partialComponent: "Users/Index",
            partialOnly: "data");

        var resolver = new PropsResolver(context, "Users/Index", EmptyServices);
        var (_, metadata) = await resolver.ResolveAsync(
            sharedProps: new(),
            pageProps: new()
            {
                ["data"] = new MergeProp(new { a = 1 }, deepMerge: true)
            });

        metadata.DeepMergeProps.Should().NotBeNull();
        metadata.DeepMergeProps!.Should().Contain("data");
    }

    [Fact]
    public async Task ResolveAsync_ShouldCollectMatchesOnMetadata()
    {
        var context = HttpContextHelper.CreateInertia(
            partialComponent: "Users/Index",
            partialOnly: "items");

        var resolver = new PropsResolver(context, "Users/Index", EmptyServices);
        var (_, metadata) = await resolver.ResolveAsync(
            sharedProps: new(),
            pageProps: new()
            {
                ["items"] = new MergeProp(new[] { 1, 2 }).MatchOn("id")
            });

        metadata.MatchPropsOn.Should().NotBeNull();
        metadata.MatchPropsOn!.Should().ContainKey("items");
    }

    // ── Dot-notation unpacking ────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ShouldUnpackDotNotationKeys_IntoNestedDictionaries()
    {
        var context = HttpContextHelper.CreateInertia();

        var resolver = new PropsResolver(context, "Profile", EmptyServices);
        var (props, _) = await resolver.ResolveAsync(
            sharedProps: new(),
            pageProps: new()
            {
                ["user.name"] = "Alice",
                ["user.email"] = "alice@example.com",
                ["title"] = "Profile"
            });

        props.Should().ContainKey("user");
        props.Should().ContainKey("title");

        var user = props["user"].Should().BeOfType<Dictionary<string, object?>>().Subject;
        user.Should().ContainKey("name").WhoseValue.Should().Be("Alice");
        user.Should().ContainKey("email").WhoseValue.Should().Be("alice@example.com");
    }

    // ── IProvidesInertiaProperties expansion ──────────────────────────────

    [Fact]
    public async Task ResolveAsync_ShouldExpandIProvidesInertiaProperties()
    {
        var context = HttpContextHelper.CreateInertia();

        var resolver = new PropsResolver(context, "Dashboard", EmptyServices);
        var (props, metadata) = await resolver.ResolveAsync(
            sharedProps: new()
            {
                ["auth"] = new AuthProvider("Alice", "admin")
            },
            pageProps: new()
            {
                ["title"] = "Dashboard"
            });

        // The provider should expand into its constituent keys
        props.Should().ContainKey("userName");
        props.Should().ContainKey("userRole");
        props["userName"].Should().Be("Alice");
        props["userRole"].Should().Be("admin");

        // The expanded keys should be tracked as shared props
        metadata.SharedPropKeys.Should().Contain("userName");
        metadata.SharedPropKeys.Should().Contain("userRole");
    }

    /// <summary>Test implementation of IProvidesInertiaProperties</summary>
    private sealed class AuthProvider(string name, string role) : IProvidesInertiaProperties
    {
        public IEnumerable<KeyValuePair<string, object?>> ToInertiaProperties(RenderContext context)
        {
            yield return new("userName", name);
            yield return new("userRole", role);
        }
    }
}
