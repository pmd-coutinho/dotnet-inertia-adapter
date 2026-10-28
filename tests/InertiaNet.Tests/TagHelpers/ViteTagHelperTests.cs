using System.Text.Json;
using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.TagHelpers;
using InertiaNet.Vite;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InertiaNet.Tests.TagHelpers;

public class ViteTagHelperTests
{
    [Fact]
    public void Production_PropagatesPathBaseNonceAndPerAssetIntegrityThroughDependencies()
    {
        using var fixture = new Fixture();
        fixture.Manifest("""
            {
              "src/app.ts":{"file":"assets/app.js","integrity":"sha384-app","css":["assets/app.css"],"imports":["_shared.js"]},
              "_shared.js":{"file":"assets/shared.js","integrity":"sha384-shared","css":["assets/shared.css","assets/app.css"]},
              "_app.css":{"file":"assets/app.css","integrity":"sha384-app-css"},
              "_shared.css":{"file":"assets/shared.css","integrity":"sha384-shared-css"}
            }
            """);
        var context = new DefaultHttpContext();
        context.Request.PathBase = "/portal";
        var html = fixture.Render(context, "request\"<nonce");
        html.Should().Contain("src=\"/portal/build/assets/app.js\"")
            .And.Contain("href=\"/portal/build/assets/app.css\"")
            .And.Contain("href=\"/portal/build/assets/shared.css\"")
            .And.Contain("rel=\"modulepreload\" href=\"/portal/build/assets/shared.js\"")
            .And.Contain("integrity=\"sha384-app\"")
            .And.Contain("integrity=\"sha384-shared\"")
            .And.Contain("integrity=\"sha384-app-css\"")
            .And.Contain("integrity=\"sha384-shared-css\"");
        html.Split("nonce=\"request&quot;&lt;nonce\"").Should().HaveCount(5);
        html.Split("crossorigin=\"anonymous\"").Should().HaveCount(5);
        html.Split("assets/app.css").Should().HaveCount(2);
        html.Should().NotContain("/portal/portal/");
    }

    [Fact]
    public void Integrity_IsOptionalConfigurableAndHtmlEncoded()
    {
        using var fixture = new Fixture();
        fixture.Manifest("""{"src/app.ts":{"file":"app.js","sri":"sha384-\"<hash"}}""");
        fixture.Render().Should().NotContain("integrity=").And.NotContain("crossorigin=");
        fixture.Options.IntegrityKey = "sri";
        fixture.Render().Should().Contain("integrity=\"sha384-&quot;&lt;hash\" crossorigin=\"anonymous\"");
        fixture.Options.IntegrityKey = null;
        fixture.Render().Should().NotContain("integrity=").And.NotContain("crossorigin=");
    }

    [Fact]
    public void NonceProvider_UsesTheCurrentRequest_AndExplicitNonceTakesPrecedence()
    {
        using var fixture = new Fixture();
        fixture.Manifest("""{"src/app.ts":{"file":"app.js"}}""");
        fixture.Options.NonceProvider = context => (string?)context.Items["nonce"];
        var first = new DefaultHttpContext();
        first.Items["nonce"] = "first";
        var second = new DefaultHttpContext();
        second.Items["nonce"] = "second";
        fixture.Render(first).Should().Contain("nonce=\"first\"");
        fixture.Render(second).Should().Contain("nonce=\"second\"").And.NotContain("first");
        fixture.Render(second, "explicit").Should().Contain("nonce=\"explicit\"").And.NotContain("second");
    }

    [Fact]
    public void HotMode_NoncesBothModulesAndReactPreamble_WithoutPrefixingTheDevServer()
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.Root, "hot"), "http://localhost:5173/");
        var context = new DefaultHttpContext();
        context.Request.PathBase = "/portal";
        context.Items["nonce"] = "dev-nonce";
        fixture.Options.NonceProvider = ctx => (string?)ctx.Items["nonce"];
        var html = fixture.Render(context);
        html.Split("nonce=\"dev-nonce\"").Should().HaveCount(3);
        html.Should().Contain("http://localhost:5173/@vite/client")
            .And.Contain("http://localhost:5173/src/app.ts")
            .And.NotContain("/portal").And.NotContain("integrity=");
        var refresh = new ViteReactRefreshTagHelper(Options.Create(fixture.Options), new HttpContextAccessor { HttpContext = context });
        Render(refresh).Should().Contain("<script type=\"module\" nonce=\"dev-nonce\">")
            .And.Contain("http://localhost:5173/@react-refresh");
        refresh.Nonce = "explicit\"nonce";
        Render(refresh).Should().Contain("nonce=\"explicit&quot;nonce\"");
        File.Delete(Path.Combine(fixture.Root, "hot"));
        Render(refresh).Should().BeEmpty();
    }

    [Fact]
    public void HotMode_CssEntrypoint_LoadsAsAStylesheetWithoutJavaScript()
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.Root, "hot"), "http://localhost:5173/\n");
        var context = new DefaultHttpContext();
        context.Request.PathBase = "/portal";
        fixture.Options.NonceProvider = _ => "style\"nonce";
        var helper = new ViteInputTagHelper(Options.Create(fixture.Options), fixture.Cache,
            new HttpContextAccessor { HttpContext = context }) { Src = "src/style.css" };

        Render(helper).Should().Be("<link rel=\"stylesheet\" href=\"http://localhost:5173/src/style.css\" nonce=\"style&quot;nonce\" />");
    }

    [Fact]
    public void Production_TraversesStaticImports_DeduplicatesCssAndPreloads_AndStopsCycles()
    {
        using var fixture = new Fixture();
        fixture.Manifest("""
            {
              "src/app.ts": { "file":"assets/app.js", "css":["assets/app.css"], "imports":["_shared.js"], "dynamicImports":["_lazy.js"] },
              "_shared.js": { "file":"assets/shared.js", "css":["assets/app.css","assets/shared.css"], "imports":["_leaf.js"] },
              "_leaf.js": { "file":"assets/leaf.js", "css":["assets/leaf.css"], "imports":["_shared.js","src/app.ts"] },
              "_lazy.js": { "file":"assets/lazy.js", "css":["assets/lazy.css"] }
            }
            """);
        var html = fixture.Render();
        html.Should().Contain("<script type=\"module\" src=\"/build/assets/app.js\"></script>")
            .And.Contain("href=\"/build/assets/app.css\"")
            .And.Contain("href=\"/build/assets/shared.css\"")
            .And.Contain("href=\"/build/assets/leaf.css\"")
            .And.Contain("rel=\"modulepreload\" href=\"/build/assets/shared.js\"")
            .And.Contain("rel=\"modulepreload\" href=\"/build/assets/leaf.js\"")
            .And.NotContain("lazy.");
        html.Split("assets/app.css").Should().HaveCount(2);
        html.Split("assets/shared.js").Should().HaveCount(2);
        html.Should().NotContain("modulepreload\" href=\"/build/assets/app.js");
    }

    [Fact]
    public void CssEntrypoint_EmitsAStylesheetWithoutScript()
    {
        using var fixture = new Fixture();
        fixture.Manifest("""{"src/app.ts":{"file":"assets/styles.css"}}""");
        fixture.Render().Should().Be("<link rel=\"stylesheet\" href=\"/build/assets/styles.css\" />");
    }

    [Fact]
    public void Cache_RefreshesOnTimestampOrLengthChanges_AndDoesNotHideDeletion()
    {
        using var fixture = new Fixture();
        fixture.Manifest("""{"src/app.ts":{"file":"assets/old.js"}}""");
        var initial = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(fixture.ManifestPath, initial);
        fixture.Render().Should().Contain("old.js");
        fixture.Manifest("""{"src/app.ts":{"file":"assets/new.js"}}""");
        File.SetLastWriteTimeUtc(fixture.ManifestPath, initial.AddSeconds(1));
        fixture.Render().Should().Contain("new.js").And.NotContain("old.js");
        fixture.Manifest("""{"src/app.ts":{"file":"assets/new-longer-name.js"}}""");
        File.SetLastWriteTimeUtc(fixture.ManifestPath, initial.AddSeconds(1));
        fixture.Render().Should().Contain("new-longer-name.js");
        File.Delete(fixture.ManifestPath);
        var render = () => fixture.Render();
        render.Should().Throw<InvalidOperationException>().WithMessage("*manifest not found*");
    }

    [Fact]
    public void Cache_CanBeDisabledForDeploymentsThatPreserveFileMetadata()
    {
        using var fixture = new Fixture();
        fixture.Manifest("""{"src/app.ts":{"file":"assets/old.js"}}""");
        fixture.Render().Should().Contain("old.js");
        var timestamp = File.GetLastWriteTimeUtc(fixture.ManifestPath);
        fixture.Manifest("""{"src/app.ts":{"file":"assets/new.js"}}""");
        File.SetLastWriteTimeUtc(fixture.ManifestPath, timestamp);
        fixture.Options.CacheManifest = false;
        fixture.Render().Should().Contain("new.js");
    }

    [Fact]
    public void ParsedManifest_IsSharedAcrossHelpers_AndIsolatedByFullPath()
    {
        using var first = new Fixture();
        using var second = new Fixture();
        first.Manifest("""{"src/app.ts":{"file":"assets/one.js"}}""");
        second.Manifest("""{"src/app.ts":{"file":"assets/two.js"}}""");
        var manifest = ViteManifest.Read(first.ManifestPath, first.Cache);
        ViteManifest.Read(first.ManifestPath, first.Cache).Equals(manifest).Should().BeTrue();
        ViteManifest.Read(second.ManifestPath, first.Cache).GetProperty("src/app.ts").GetProperty("file").GetString().Should().Be("assets/two.js");
    }

    [Fact]
    public void HotMode_BypassesTheCachedManifest_AndProductionResumesAfterRemoval()
    {
        using var fixture = new Fixture();
        fixture.Manifest("""{"src/app.ts":{"file":"assets/old.js"}}""");
        fixture.Render().Should().Contain("old.js");
        var hot = Path.Combine(fixture.Root, "hot");
        File.WriteAllText(hot, "http://localhost:5173/\n");
        fixture.Render().Should().Contain("http://localhost:5173/@vite/client").And.Contain("http://localhost:5173/src/app.ts").And.NotContain("old.js");
        fixture.Manifest("""{"src/app.ts":{"file":"assets/deployed.js"}}""");
        File.Delete(hot);
        fixture.Render().Should().Contain("deployed.js").And.NotContain("@vite/client");
    }

    [Theory]
    [InlineData("[]", "object")]
    [InlineData("{\"src/app.ts\":{\"file\":\"app.js\",\"imports\":[\"_missing\"]}}", "Imported entry '_missing'")]
    public void MalformedManifests_ProduceExplicitErrors(string json, string message)
    {
        using var fixture = new Fixture();
        fixture.Manifest(json);
        var render = () => fixture.Render();
        render.Should().Throw<Exception>().Where(exception => exception is JsonException || exception is InvalidOperationException).WithMessage("*" + message + "*");
    }

    [Fact]
    public void RegisteredHelper_ResolvesWithTheSharedCache()
    {
        using var fixture = new Fixture();
        fixture.Manifest("""{"src/app.ts":{"file":"assets/app.js"}}""");
        using var services = new ServiceCollection().AddViteHelper(options => options.PublicDirectory = fixture.Root).BuildServiceProvider();
        services.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext();
        services.GetRequiredService<IHttpContextAccessor>().HttpContext!.Request.PathBase = "/portal";
        var helper = ActivatorUtilities.CreateInstance<ViteInputTagHelper>(services);
        helper.Src = "src/app.ts";
        Render(helper).Should().Contain("assets/app.js");
        // Razor's activator uses CreateFactory with no explicit constructor arguments.
        var factory = ActivatorUtilities.CreateFactory(typeof(ViteInputTagHelper), Type.EmptyTypes);
        var razorHelper = (ViteInputTagHelper)factory(services, []);
        razorHelper.Src = "src/app.ts";
        Render(razorHelper).Should().Contain("src=\"/portal/build/assets/app.js\"");
        var refreshFactory = ActivatorUtilities.CreateFactory(typeof(ViteReactRefreshTagHelper), Type.EmptyTypes);
        Render((ViteReactRefreshTagHelper)refreshFactory(services, [])).Should().BeEmpty();
    }

    private static string Render(TagHelper helper)
    {
        var output = new TagHelperOutput("vite-input", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        helper.Process(new TagHelperContext([], new Dictionary<object, object>(), "test"), output);
        return output.Content.GetContent();
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("inertianet-vite-tests-").FullName;
        public string ManifestPath => Path.Combine(Root, "build/manifest.json");
        public MemoryCache Cache { get; } = new(new MemoryCacheOptions());
        public ViteOptions Options { get; }
        public Fixture() { Options = new() { PublicDirectory = Root }; Directory.CreateDirectory(Path.Combine(Root, "build")); }
        public void Manifest(string json) => File.WriteAllText(ManifestPath, json);
        public string Render(HttpContext? context = null, string? nonce = null) => ViteTagHelperTests.Render(
            new ViteInputTagHelper(Microsoft.Extensions.Options.Options.Create(Options), Cache, new HttpContextAccessor { HttpContext = context })
            { Src = "src/app.ts", Nonce = nonce });
        public void Dispose() { Cache.Dispose(); Directory.Delete(Root, true); }
    }
}
