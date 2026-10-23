using System.Text.Json;
using FluentAssertions;
using InertiaNet.Diagnostics;
using InertiaNet.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaNet.Tests.Diagnostics;

public class EndpointManifestTests
{
    [Fact]
    public async Task Capture_ReadsActualMvcAndMinimalMetadata_WithoutRunningFactories()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddInertia();
        builder.Services.AddControllers().AddApplicationPart(typeof(ManifestFixtureController).Assembly);
        await using var app = builder.Build();
        app.MapControllers();
        var calls = 0;
        app.MapGroup("/manifest").MapInertia("/about", "About", (HttpContext _) => { calls++; return new { }; })
            .WithName("manifest.about").RequireHost("example.com");
        var manifest = InertiaEndpointManifest.Capture(app);
        calls.Should().Be(0);
        manifest.SchemaVersion.Should().Be(1);
        var about = manifest.Endpoints.Single(endpoint => endpoint.Name == "manifest.about");
        about.Pattern.Should().Be("/manifest/about");
        about.Methods.Should().Equal("GET");
        about.Hosts.Should().Equal("example.com");
        about.Component.Should().Be("About");
        var mvc = manifest.Endpoints.Single(endpoint => endpoint.Name == "manifest.items.show");
        mvc.Pattern.Should().Be("manifest/items/{id:int}");
        mvc.Controller.Should().Be(typeof(ManifestFixtureController).FullName);
        mvc.Action.Should().Be(nameof(ManifestFixtureController.Show));
        mvc.Methods.Should().Equal("GET");
    }

    [Fact]
    public void Capture_ReconstructsPatternsWithoutRawText_AndRepresentsUnrestrictedMethods()
    {
        var parsed = RoutePatternFactory.Parse("/generated/{id:int=42}");
        var pattern = RoutePatternFactory.Pattern(parsed.PathSegments.ToArray());
        var endpoint = new RouteEndpoint(_ => Task.CompletedTask, pattern, 1, EndpointMetadataCollection.Empty, "generated");
        var manifest = InertiaEndpointManifest.Capture([new DefaultEndpointDataSource(endpoint)]);
        manifest.Endpoints.Single().Pattern.Should().Be("/generated/{id:int=42}");
        manifest.Endpoints.Single().Methods.Should().Equal("*");
    }

    [Fact]
    public async Task Write_UsesVersionedWireNames_AndCancellationRetainsTheLastSnapshot()
    {
        var directory = Directory.CreateTempSubdirectory("inertia-endpoints-");
        try
        {
            var manifest = new InertiaEndpointManifest(1, []);
            var path = Path.Combine(directory.FullName, "manifest.json");
            await manifest.WriteAsync(path);
            var original = await File.ReadAllTextAsync(path);
            using var json = JsonDocument.Parse(original);
            json.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
            json.RootElement.GetProperty("endpoints").GetArrayLength().Should().Be(0);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Func<Task> write = () => manifest.WriteAsync(path, cancelled.Token);
            await write.Should().ThrowAsync<OperationCanceledException>();
            (await File.ReadAllTextAsync(path)).Should().Be(original);
            directory.GetFiles("*.tmp").Should().BeEmpty();
        }
        finally { directory.Delete(true); }
    }
}

[ApiController, Route("manifest/items")]
public sealed class ManifestFixtureController : ControllerBase
{
    [HttpGet("{id:int}", Name = "manifest.items.show")]
    public object Show(int id) => new { id };
}
