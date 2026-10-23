using FluentAssertions;
using InertiaNet.Pathfinder.Analysis;
using InertiaNet.Pathfinder.Generation;
using InertiaNet.Pathfinder.Tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp;

namespace InertiaNet.Pathfinder.Tests.Generation;

public class FeatureFlagTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void DisablingExperimentalTypes_DoesNotLeaveDanglingReferences(bool models, bool enums)
    {
        using var project = new TemporaryProject();
        var tree = CSharpSyntaxTree.ParseText("""
            using System.Collections.Generic;
            enum Status { Ready }
            class Post { public Status Status { get; set; } }
            class Props { public Dictionary<string, Post> Posts { get; set; } public Status Status { get; set; } }
            class Page { void Index() { inertia.Render<Props>("Posts", new Props()); } }
            """);
        TypeMapper.ClearEnums();
        TypeMapper.RegisterEnum("Status", "Status");
        var discoveredEnums = EnumDiscoverer.Discover([tree]);
        var props = PropsDiscoverer.Discover([tree]);
        var discoveredModels = ModelDiscoverer.Discover([tree], props);
        TypeScriptGenerator.Generate([], discoveredEnums, props, discoveredModels, new()
        {
            ProjectPath = project.Root, OutputPath = project.PathFor("output"), GenerateModels = models, GenerateEnums = enums,
        });
        var page = File.ReadAllText(project.PathFor("output/types/Posts.ts"));
        if (!models) page.Should().Contain("Record<string, unknown>").And.NotContain("models/Post");
        if (!enums)
        {
            page.Should().Contain("status: number").And.NotContain("enums/Status").And.NotContain("enums/number");
            Directory.Exists(project.PathFor("output/enums")).Should().BeFalse();
            if (models) File.ReadAllText(project.PathFor("output/models/Post.ts")).Should().Contain("status: number");
        }
    }
}
