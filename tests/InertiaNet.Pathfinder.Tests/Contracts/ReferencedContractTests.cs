using System.Text.Json;
using FluentAssertions;
using InertiaNet.Pathfinder.Analysis;
using InertiaNet.Pathfinder.Tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp;

namespace InertiaNet.Pathfinder.Tests.Contracts;

public class ReferencedContractTests
{
    [Theory]
    [InlineData("net8.0", "Active", false)]
    [InlineData("net9.0", "wire-active", true)]
    [InlineData("net10.0", "wire-active", true)]
    public async Task FrameworkMetadata_ComesFromTheSelectedTargetingPack(string framework, string label, bool hasMemberNameAttribute)
    {
        using var project = new TemporaryProject();
        var csproj = project.File("App.csproj", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks></PropertyGroup></Project>""");
        project.File("Page.cs", """
            using System.Text.Json.Serialization;
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public enum State
            {
            #if NET9_0_OR_GREATER
                [JsonStringEnumMemberName("wire-active")]
            #endif
                Active
            }
            public class Props { public State State { get; set; } }
            class Page { void Index() { inertia.Render("Framework", new Props()); } }
            """);
        var config = new PathfinderConfig { ProjectPath = csproj, Framework = framework };
        var sources = await ProjectSourceLoader.LoadAsync(config);
        sources.FrameworkFiles.Should().Contain(file => file.Replace('\\', '/').EndsWith("/ref/" + framework + "/System.Text.Json.dll", StringComparison.Ordinal));
        var trees = await ReadTrees(sources);
        var graph = await ProjectCompilationLoader.LoadAsync(sources, trees, config);
        (graph.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute") is not null).Should().Be(hasMemberNameAttribute);
        PathfinderDiagnostics.Clear();
        var contracts = new SemanticContractDiscoverer(graph.Compilation, config).Discover(trees, []);
        contracts.Enums.Single().Members.Single().Value.Should().Be(label);
    }

    [Fact]
    public async Task ReferencedProjects_ProvideDtoSymbolsAndInputs_WithoutBuildingOrStartingTheApplication()
    {
        using var project = new TemporaryProject();
        var dto = project.File("contracts/Props.cs", """
            #nullable enable
            using System.Text.Json.Serialization;
            namespace Contracts;
            public record Item(int Id);
            public class Props { [JsonPropertyName("account-id")] public int AccountId { get; set; } public Item Item { get; set; } = new(1); }
            """);
        var reference = project.File("contracts/Contracts.csproj", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""");
        var app = project.File("app/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><ProjectReference Include="../contracts/Contracts.csproj" /></ItemGroup>
              <Target Name="NeverBuild" BeforeTargets="CoreCompile"><Error Text="Application must not be built" /></Target>
            </Project>
            """);
        project.File("app/Page.cs", """class Page { void Index() { inertia.Render("External", new Contracts.Props()); } }""");
        var config = new PathfinderConfig { ProjectPath = app };
        var sources = await ProjectSourceLoader.LoadAsync(config);
        var trees = await ReadTrees(sources);
        var graph = await ProjectCompilationLoader.LoadAsync(sources, trees, config);
        PathfinderDiagnostics.Clear();
        var contracts = new SemanticContractDiscoverer(graph.Compilation, config).Discover(trees, []);
        contracts.Pages.Single().Props.Should().Contain(field => field.Name == "account-id" && field.TypeScriptType == "number");
        contracts.Models.Single().Properties.Should().Contain(property => property.Name == "id");
        graph.Inputs.Should().Contain(dto).And.Contain(reference);
        (sources with { AdditionalInputs = graph.Inputs }).WatchDirectories.Should().Contain(project.PathFor("contracts"));
        Directory.Exists(project.PathFor("app/bin")).Should().BeFalse();
        Directory.Exists(project.PathFor("contracts/bin")).Should().BeFalse();
    }

    [Fact]
    public async Task RestoredPackageCompileAssets_ProvideReferencedDtoMetadata_AndAreTracked()
    {
        using var project = new TemporaryProject();
        var binary = project.PathFor("cache/dtopackage/1.0.0/lib/net10.0/DtoPackage.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
        var metadata = ProjectCompilationLoader.CreateForSources([CSharpSyntaxTree.ParseText("""
            using System.Text.Json.Serialization;
            namespace Package;
            public class Dto { [JsonPropertyName("wire-name")] public string Name { get; set; } = ""; }
            """)]);
        using (var stream = File.Create(binary)) metadata.Emit(stream).Success.Should().BeTrue();
        var csproj = project.File("App.csproj", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""");
        project.File("Page.cs", """class Page { void Index() { inertia.Render("Package", new Package.Dto()); } }""");
        var assets = project.File("obj/project.assets.json", JsonSerializer.Serialize(new
        {
            version = 3,
            targets = new Dictionary<string, object> { ["net10.0"] = new Dictionary<string, object> { ["DtoPackage/1.0.0"] = new { compile = new Dictionary<string, object> { ["lib/net10.0/DtoPackage.dll"] = new { } } } } },
            libraries = new Dictionary<string, object> { ["DtoPackage/1.0.0"] = new { type = "package", path = "dtopackage/1.0.0" } },
            packageFolders = new Dictionary<string, object> { [project.PathFor("cache")] = new { } },
        }));
        var config = new PathfinderConfig { ProjectPath = csproj };
        var sources = await ProjectSourceLoader.LoadAsync(config);
        sources.MetadataFiles.Should().Contain(binary);
        var trees = await ReadTrees(sources);
        var graph = await ProjectCompilationLoader.LoadAsync(sources, trees, config);
        PathfinderDiagnostics.Clear();
        var contracts = new SemanticContractDiscoverer(graph.Compilation, config).Discover(trees, []);
        contracts.Pages.Single().Props.Should().Contain(field => field.Name == "wire-name" && field.TypeScriptType == "string");
        graph.Inputs.Should().Contain(binary).And.Contain(assets);
    }

    private static async Task<Microsoft.CodeAnalysis.SyntaxTree[]> ReadTrees(ProjectSources source)
    {
        var trees = new List<Microsoft.CodeAnalysis.SyntaxTree>();
        foreach (var file in source.Files)
            trees.Add(CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(file), source.ParseOptions, path: Path.GetRelativePath(source.Root, file).Replace('\\', '/')));
        return trees.ToArray();
    }
}
