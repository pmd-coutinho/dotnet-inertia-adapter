using FluentAssertions;
using System.Diagnostics;
using System.Collections.Concurrent;
using InertiaNet.Diagnostics;
using InertiaNet.Extensions;
using InertiaNet.Pathfinder.Analysis;
using InertiaNet.Pathfinder.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.CodeAnalysis.CSharp;

namespace InertiaNet.Pathfinder.Tests.Contracts;

public class EndpointManifestContractTests
{
    [Fact]
    public async Task RegisteredRoutesAndComponents_AgreeWithStaticDiscovery()
    {
        PathfinderDiagnostics.Clear();
        using var project = new TemporaryProject();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapGet("/items/{id:int=42}", (int id) => id).WithName("items.show");
        app.MapInertia("/about", "About").WithName("about");
        var path = project.PathFor("endpoints.json");
        await InertiaEndpointManifest.Capture(app).WriteAsync(path);
        var tree = CSharpSyntaxTree.ParseText("""
            using Microsoft.AspNetCore.Builder;
            using InertiaNet.Extensions;
            var app = WebApplication.Create();
            app.MapGet("/items/{id:int=42}", (int id) => id).WithName("items.show");
            app.MapInertia("/about", "About").WithName("about");
            """, path: "Program.cs");
        var compilation = ProjectCompilationLoader.CreateForSources([tree]);
        var routes = MinimalApiRouteDiscoverer.Discover(tree, compilation.GetSemanticModel(tree));
        EndpointManifestValidator.Validate(routes, path, new());
        PathfinderDiagnostics.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Differences_ReportMissingChangedAndUndiscoveredNamedEndpoints()
    {
        PathfinderDiagnostics.Clear();
        using var project = new TemporaryProject();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapPost("/changed", () => "native").WithName("changed");
        app.MapGet("/dynamic", () => "registered elsewhere").WithName("dynamic");
        var path = project.PathFor("endpoints.json");
        await InertiaEndpointManifest.Capture(app).WriteAsync(path);
        EndpointManifestValidator.Validate([
            Route("changed", "/source"), Route("missing", "/missing"),
        ], path, new());
        PathfinderDiagnostics.Entries.Select(entry => entry.Code).Should().BeEquivalentTo("PATHFINDER400", "PATHFINDER401", "PATHFINDER402");
    }

    [Fact]
    public async Task Cli_StrictManifestCheck_PreservesLastGoodOutput_AndTracksSnapshotInput()
    {
        using var project = new TemporaryProject();
        project.File("Program.cs", "app.MapGet(\"/source\", () => \"ok\").WithName(\"source\");");
        var path = project.PathFor("endpoints.json");
        await new InertiaEndpointManifest(1, [new("source", "/source", ["GET"], [], null, null, null, 0, false, false)]).WriteAsync(path);
        var args = new[] { TemporaryProject.ToolAssembly, "-p", project.Root, "-o", project.PathFor("output"), "--endpoint-manifest", path, "--strict", "--quiet" };
        var initial = await TemporaryProject.RunAsync(project.Root, args);
        initial.ExitCode.Should().Be(0, initial.Error);
        var module = project.PathFor("output/routes/index.ts");
        var bytes = File.ReadAllBytes(module);
        File.ReadAllLines(project.PathFor("output/.pathfinder-inputs")).Should().Contain("endpoints.json");
        await new InertiaEndpointManifest(1, []).WriteAsync(path);
        var stale = await TemporaryProject.RunAsync(project.Root, args);
        stale.ExitCode.Should().Be(1, stale.Error);
        stale.Error.Should().Contain("PATHFINDER400");
        File.ReadAllBytes(module).Should().Equal(bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Watch_RevalidatesJsonInputs_AfterAnInitialStrictFailure(bool external)
    {
        using var project = new TemporaryProject();
        project.File("app/Program.cs", "app.MapGet(\"/source\", () => \"ok\").WithName(\"source\");");
        var path = project.PathFor(external ? "external/endpoints.json" : "app/endpoints.json");
        await new InertiaEndpointManifest(1, []).WriteAsync(path);
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = project.Root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { TemporaryProject.ToolAssembly, "-p", project.PathFor("app"), "-o", project.PathFor("output"), "--endpoint-manifest", path, "--strict", "--watch", "--debounce", "20" }) start.ArgumentList.Add(argument);
        var watching = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new ConcurrentQueue<string>();
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => { if (e.Data?.Contains("Watching", StringComparison.Ordinal) == true) watching.TrySetResult(); };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            errors.Enqueue(e.Data);
            if (e.Data.Contains("PATHFINDER400", StringComparison.Ordinal)) failed.TrySetResult();
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await Task.WhenAll(watching.Task, failed.Task).WaitAsync(TimeSpan.FromSeconds(20));
            errors.Should().Contain(line => line.Contains("PATHFINDER400"));
            await new InertiaEndpointManifest(1, [new("source", "/source", ["GET"], [], null, null, null, 0, false, false)]).WriteAsync(path);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!File.Exists(project.PathFor("output/.pathfinder.stamp"))) await Task.Delay(50, deadline.Token);
            File.ReadAllText(project.PathFor("output/routes/index.ts")).Should().Contain("export const source");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData("{\"schemaVersion\":99,\"endpoints\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"endpoints\":[null]}")]
    [InlineData("{\"schemaVersion\":1,\"endpoints\":[{\"pattern\":\"/\",\"methods\":[],\"hosts\":[]}]}")]
    public void InvalidManifest_FailsExplicitly(string json)
    {
        using var project = new TemporaryProject();
        var path = project.File("invalid.json", json);
        Action validate = () => EndpointManifestValidator.Validate([], path, new());
        validate.Should().Throw<ArgumentException>();
    }

    private static RouteInfo Route(string name, string template)
        => new("__MinimalApi__", "MinimalApi", name, ["get"], template, [], name);
}
