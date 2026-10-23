using System.Security;
using FluentAssertions;
using InertiaNet.Pathfinder.Tests.TestHelpers;
using InertiaNet.Diagnostics;

namespace InertiaNet.Pathfinder.Tests.Delivery;

public class BuildIntegrationTests
{
    [Fact]
    public async Task Build_TracksDeletionLinkedFilesMissingOutputsAndOptions_WithoutRewritingUnchangedModules()
    {
        using var project = new TemporaryProject();
        var app = "app space $(touch SHOULD_NOT_EXIST) 'quoted'";
        var root = project.PathFor(app);
        project.File("shared/Linked.cs", "class Linked {}");
        project.File("build/settings.props", "<Project><Import Project=\"nested.props\" /></Project>");
        project.File("build/nested.props", "<Project />");
        project.File(app + "/Program.cs", "app.MapGet(\"/users\", () => \"users\").WithName(\"users.index\");");
        var obsolete = project.File(app + "/Obsolete.cs", "app.MapGet(\"/obsolete\", () => \"old\").WithName(\"obsolete.index\");");
        var csproj = project.File(app + "/App.csproj", ProjectXml());

        async Task<(string Output, string Error)> Build(bool expectedGeneration, params string[] extra)
        {
            var fingerprintPath = Path.Combine(root, "obj/Debug/net10.0/pathfinder.inputs");
            var previousFingerprint = File.Exists(fingerprintPath) ? File.ReadAllText(fingerprintPath) : null;
            var result = await TemporaryProject.RunAsync(root,
                ["msbuild", csproj, "-nologo", "-target:PathfinderGenerate", "-verbosity:minimal", .. extra]);
            result.ExitCode.Should().Be(0, result.Output + result.Error);
            if (!expectedGeneration) File.ReadAllText(fingerprintPath).Should().Be(previousFingerprint);
            if (expectedGeneration) result.Output.Should().Contain("Running Pathfinder");
            else result.Output.Should().NotContain("Running Pathfinder");
            return (result.Output, result.Error);
        }

        await Build(true);
        var module = Path.Combine(root, "generated/index.ts");
        var unchanged = File.GetLastWriteTimeUtc(module);
        await Build(false);
        File.GetLastWriteTimeUtc(module).Should().Be(unchanged);
        // A source edit which leaves the generated contract unchanged runs once.
        project.File(app + "/Program.cs", "// implementation-only edit\napp.MapGet(\"/users\", () => \"users\").WithName(\"users.index\");");
        await Build(true);
        File.GetLastWriteTimeUtc(module).Should().Be(unchanged);
        await Build(false);
        File.Delete(obsolete);
        await Build(true);
        File.Exists(Path.Combine(root, "generated/routes/obsolete/index.ts")).Should().BeFalse();
        await Build(false);
        project.File("shared/Linked.cs", "class Linked { public string? Name { get; set; } }");
        await Build(true);
        await Build(false);
        project.File("build/nested.props", "<Project><PropertyGroup><ContractVersion>2</ContractVersion></PropertyGroup></Project>");
        await Build(true);
        await Build(false);
        File.Delete(Path.Combine(root, "generated/routes/users/index.ts"));
        await Build(true);
        File.Exists(Path.Combine(root, "generated/routes/users/index.ts")).Should().BeTrue();
        File.Delete(Path.Combine(root, "generated/.pathfinder-files"));
        await Build(true);
        await Build(false);
        await Build(true, "-property:PathfinderGenerateForms=false");
        File.ReadAllText(Path.Combine(root, "generated/routes/users/index.ts")).Should().NotContain(".form =");
        await Build(false, "-property:PathfinderGenerateForms=false");
        await Build(true, "-property:PathfinderProjectPath=App.csproj");
        await Build(false, "-property:PathfinderProjectPath=App.csproj");
        project.File(app + "/wire.json", "{}");
        await Build(true, "-property:PathfinderContractFile=wire.json");
        await Build(false, "-property:PathfinderContractFile=wire.json");
        await new InertiaEndpointManifest(1, [new("users.index", "/users", ["GET"], [], null, null, null, 0, false, false)]).WriteAsync(Path.Combine(root, "endpoints.json"));
        await Build(true, "-property:PathfinderEndpointManifest=endpoints.json");
        await Build(false, "-property:PathfinderEndpointManifest=endpoints.json");
        await new InertiaEndpointManifest(1, [new("users.index", "/changed", ["GET"], [], null, null, null, 0, false, false)]).WriteAsync(Path.Combine(root, "endpoints.json"));
        await Build(true, "-property:PathfinderEndpointManifest=endpoints.json");
        await Build(false, "-property:PathfinderEndpointManifest=endpoints.json");
        project.File(app + "/wire.json", """{"output":{"naming":"preserve"}}""");
        await Build(true, "-property:PathfinderContractFile=wire.json");
        await Build(false, "-property:PathfinderContractFile=wire.json");
        Directory.GetFiles(project.Root, "SHOULD_NOT_EXIST", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task Build_PassesSemicolonFiltersAsOneEnvironmentValue()
    {
        using var project = new TemporaryProject();
        project.File("Program.cs", """
            [Route("foo")] class FooController { [HttpGet] public void Index() {} }
            [Route("bar")] class BarController { [HttpGet] public void Index() {} }
            [Route("keep")] class KeepController { [HttpGet] public void Index() {} }
            """);
        var csproj = project.File("App.csproj", ProjectXml("<PathfinderSkipPatterns>Foo*;Bar*</PathfinderSkipPatterns>"));
        var result = await TemporaryProject.RunAsync(project.Root,
            "msbuild", csproj, "-nologo", "-target:PathfinderGenerate", "-verbosity:minimal");
        result.ExitCode.Should().Be(0, result.Output + result.Error);
        Directory.GetFiles(project.PathFor("generated/actions"), "*.ts", SearchOption.AllDirectories)
            .Select(Path.GetFileName).Should().Contain("keep.ts").And.NotContain("foo.ts").And.NotContain("bar.ts");
    }

    private static string ProjectXml(string properties = "")
    {
        var build = Path.Combine(TemporaryProject.RepositoryRoot, "src/InertiaNet.Pathfinder.Build/buildTransitive/InertiaNet.Pathfinder.Build");
        string Xml(string value) => SecurityElement.Escape(value)!;
        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="{Xml(build)}.props" />
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework><PathfinderEnabled>true</PathfinderEnabled>
                <PathfinderQuiet>false</PathfinderQuiet><PathfinderOutputPath>$(MSBuildProjectDirectory)/generated</PathfinderOutputPath>
                <PathfinderToolCommand>dotnet &quot;{Xml(TemporaryProject.ToolAssembly)}&quot;</PathfinderToolCommand>
                {properties}
              </PropertyGroup>
              <ItemGroup><Compile Include="../shared/Linked.cs" Condition="Exists('../shared/Linked.cs')" /></ItemGroup>
              <Import Project="../build/settings.props" Condition="Exists('../build/settings.props')" />
              <Import Project="{Xml(build)}.targets" />
            </Project>
            """;
    }
}
