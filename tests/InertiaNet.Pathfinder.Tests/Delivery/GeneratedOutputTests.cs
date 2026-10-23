using FluentAssertions;
using InertiaNet.Pathfinder.Generation;
using InertiaNet.Pathfinder.Tests.TestHelpers;

namespace InertiaNet.Pathfinder.Tests.Delivery;

public class GeneratedOutputTests
{
    [Fact]
    public void Synchronize_DeletesOnlyOwnedFiles_AndPreservesUnchangedTimestamps()
    {
        using var project = new TemporaryProject();
        var stage = project.PathFor("stage");
        project.File("stage/index.ts", "export const value = 1;\n");
        project.File("stage/stale.ts", "export {};\n");
        var config = Config(project);
        GeneratedOutput.Synchronize(stage, config).Should().BeTrue();
        var module = project.PathFor("output/index.ts");
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(module, old);
        File.SetLastWriteTimeUtc(project.PathFor("output/.pathfinder.stamp"), old);
        project.File("output/user.ts", "// user content");
        File.Delete(project.PathFor("stage/stale.ts"));
        GeneratedOutput.Synchronize(stage, config).Should().BeTrue();
        File.Exists(project.PathFor("output/stale.ts")).Should().BeFalse();
        File.ReadAllText(project.PathFor("output/user.ts")).Should().Be("// user content");
        File.GetLastWriteTimeUtc(module).Should().Be(old);
        File.GetLastWriteTimeUtc(project.PathFor("output/.pathfinder.stamp")).Should().BeAfter(old);
        GeneratedOutput.Synchronize(stage, config).Should().BeFalse();
    }

    [Fact]
    public void NoClean_RetainsOwnership_UntilCleaningIsEnabled()
    {
        using var project = new TemporaryProject();
        var stage = project.PathFor("stage");
        project.File("stage/old.ts", "export {};\n");
        var config = Config(project);
        GeneratedOutput.Synchronize(stage, config);
        File.Delete(project.PathFor("stage/old.ts"));
        config.Clean = false;
        GeneratedOutput.Synchronize(stage, config);
        File.Exists(project.PathFor("output/old.ts")).Should().BeTrue();
        config.Clean = true;
        GeneratedOutput.Synchronize(stage, config);
        File.Exists(project.PathFor("output/old.ts")).Should().BeFalse();
    }

    [Fact]
    public void Check_DetectsDrift_WithoutCreatingOrModifyingOutput()
    {
        using var project = new TemporaryProject();
        project.File("stage/index.ts", "export {};\n");
        var config = Config(project);
        config.Check = true;
        GeneratedOutput.Synchronize(project.PathFor("stage"), config).Should().BeTrue();
        Directory.Exists(config.OutputPath).Should().BeFalse();
        config.Check = false;
        GeneratedOutput.Synchronize(project.PathFor("stage"), config);
        var original = Directory.GetFiles(config.OutputPath).ToDictionary(path => path, File.GetLastWriteTimeUtc);
        config.Check = true;
        GeneratedOutput.Synchronize(project.PathFor("stage"), config).Should().BeFalse();
        project.File("stage/index.ts", "export const modified = true;\n");
        GeneratedOutput.Synchronize(project.PathFor("stage"), config).Should().BeTrue();
        foreach (var (path, time) in original) File.GetLastWriteTimeUtc(path).Should().Be(time);
    }

    [Fact]
    public void ExistingFiles_RequireExplicitAdoption_AndUnrelatedFilesSurvive()
    {
        using var project = new TemporaryProject();
        project.File("stage/index.ts", "export {};\n");
        project.File("output/index.ts", "legacy generated code");
        project.File("output/notes.txt", "keep");
        var config = Config(project);
        var generate = () => GeneratedOutput.Synchronize(project.PathFor("stage"), config);
        generate.Should().Throw<InvalidOperationException>().WithMessage("*unowned file*");
        File.ReadAllText(project.PathFor("output/index.ts")).Should().Be("legacy generated code");
        config.Adopt = true;
        generate.Should().NotThrow();
        File.ReadAllText(project.PathFor("output/notes.txt")).Should().Be("keep");
    }

    [Fact]
    public void FailedCommit_RestoresContentsAndTimestamps_IncludingOwnership()
    {
        using var project = new TemporaryProject();
        project.File("stage/index.ts", "export {};\n");
        project.File("stage/removed.ts", "export {};\n");
        var config = Config(project);
        GeneratedOutput.Synchronize(project.PathFor("stage"), config);
        var before = Directory.GetFiles(config.OutputPath).ToDictionary(path => path,
            path => (Bytes: File.ReadAllBytes(path), Time: File.GetLastWriteTimeUtc(path)));
        project.File("stage/index.ts", "export const changed = true;\n");
        project.File("stage/new.ts", "export {};\n");
        File.Delete(project.PathFor("stage/removed.ts"));
        var generate = () => GeneratedOutput.Synchronize(project.PathFor("stage"), config,
            relative => { if (relative == "removed.ts") throw new IOException("Injected write failure"); });
        generate.Should().Throw<IOException>();
        Directory.GetFiles(config.OutputPath).Should().BeEquivalentTo(before.Keys);
        foreach (var (path, snapshot) in before)
        {
            File.ReadAllBytes(path).Should().Equal(snapshot.Bytes);
            File.GetLastWriteTimeUtc(path).Should().Be(snapshot.Time);
        }
    }

    [Fact]
    public void RejectsProjectAncestorAndSymbolicLinks_AndManifestTraversal()
    {
        using var project = new TemporaryProject();
        project.File("stage/index.ts", "export {};\n");
        var config = Config(project);
        config.OutputPath = project.Root;
        var generate = () => GeneratedOutput.Synchronize(project.PathFor("stage"), config);
        generate.Should().Throw<InvalidOperationException>().WithMessage("*ancestor*");
        config.OutputPath = project.PathFor("output");
        Directory.CreateDirectory(config.OutputPath);
        File.CreateSymbolicLink(project.PathFor("output/index.ts"), project.File("victim.ts", "private content"));
        generate.Should().Throw<InvalidOperationException>().WithMessage("*symbolic link*");
        File.ReadAllText(project.PathFor("victim.ts")).Should().Be("private content");
        File.Delete(project.PathFor("output/index.ts"));
        project.File("output/.pathfinder-manifest.json", """{"version":1,"files":{"../victim.ts":"hash"}}""");
        generate.Should().Throw<InvalidOperationException>().WithMessage("*Invalid generated-file path*");
    }

    [Theory]
    [InlineData("../outside.ts")]
    [InlineData("/tmp/outside.ts")]
    public void StagingPaths_CannotEscape(string relative)
    {
        using var project = new TemporaryProject();
        var resolve = () => GeneratedOutput.PathUnder(project.Root, relative);
        resolve.Should().Throw<InvalidOperationException>();
    }

    private static PathfinderConfig Config(TemporaryProject project) => new() { ProjectPath = project.Root, OutputPath = project.PathFor("output") };
}
