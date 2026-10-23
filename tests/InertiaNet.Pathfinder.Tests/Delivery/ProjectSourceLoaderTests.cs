using FluentAssertions;
using InertiaNet.Pathfinder.Tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp;

namespace InertiaNet.Pathfinder.Tests.Delivery;

public class ProjectSourceLoaderTests
{
    [Fact]
    public async Task ProjectEvaluation_RespectsCompileItemsLinkedFilesFrameworksAndSymbols_WithoutBuilding()
    {
        using var project = new TemporaryProject();
        project.File("Directory.Build.props", """
            <Project>
              <PropertyGroup><ConfigRoot>$(MSBuildThisFileDirectory)config/</ConfigRoot></PropertyGroup>
              <Import Project="$(ConfigRoot)first.props" />
            </Project>
            """);
        project.File("config/first.props", "<Project><Import Project=\"second.props\" /></Project>");
        var nestedImport = project.File("config/second.props", "<Project><PropertyGroup><DefineConstants>$(DefineConstants);CUSTOM_SYMBOL</DefineConstants></PropertyGroup></Project>");
        var linked = project.File("shared/Linked.cs", "public class Linked {}");
        var excluded = project.File("app/Excluded.cs", "this is not valid C#");
        var source = project.File("app/Program.cs", """
            #if NET10_0 && CUSTOM_SYMBOL
            class Selected {}
            #else
            this must not be parsed
            #endif
            """);
        var frameworkOnly = project.File("app/Framework.cs", "class FrameworkOnly {}");
        var csproj = project.File("app/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFrameworks>net9.0;net10.0</TargetFrameworks><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
              <ItemGroup>
                <Compile Remove="Excluded.cs;Framework.cs" />
                <Compile Include="../shared/Linked.cs" Link="Linked.cs" />
                <Compile Include="Framework.cs" Condition="'$(TargetFramework)' == 'net10.0'" />
              </ItemGroup>
              <Target Name="MustNeverRun" BeforeTargets="CoreCompile"><Error Text="Pathfinder must not compile the app" /></Target>
            </Project>
            """);
        var config = new PathfinderConfig { ProjectPath = csproj, Framework = "net10.0" };
        var inputs = await ProjectSourceLoader.LoadAsync(config);
        inputs.Files.Should().BeEquivalentTo([source, linked, frameworkOnly]);
        inputs.Files.Should().NotContain(excluded);
        inputs.ParseOptions.PreprocessorSymbolNames.Should().Contain(["NET10_0", "CUSTOM_SYMBOL"]);
        CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(source), inputs.ParseOptions).GetDiagnostics().Should().BeEmpty();
        inputs.GlobalUsings.Should().Contain("global using System;");
        inputs.ConfigurationFiles.Should().Contain(project.PathFor("Directory.Build.props"));
        inputs.ConfigurationFiles.Should().Contain(nestedImport);
        inputs.WatchDirectories.Should().Contain(project.PathFor("shared"));
        Directory.Exists(project.PathFor("app/bin")).Should().BeFalse();
        config.Framework = null;
        (await ProjectSourceLoader.LoadAsync(config)).Framework.Should().Be("net9.0");
        config.Framework = "net8.0";
        await Assert.ThrowsAsync<ArgumentException>(() => ProjectSourceLoader.LoadAsync(config));
    }

    [Fact]
    public async Task DirectoryMode_ExcludesDependencies_AndRequiresExplicitProjectWhenAmbiguous()
    {
        using var project = new TemporaryProject();
        var source = project.File("src/Program.cs", "class Program {}");
        project.File("node_modules/Library.cs", "bad");
        project.File("obj/Generated.cs", "bad");
        project.File("bin/Generated.cs", "bad");
        var config = new PathfinderConfig { ProjectPath = project.Root };
        (await ProjectSourceLoader.LoadAsync(config)).Files.Should().Equal(source);
        project.File("First.csproj", "<Project />");
        project.File("Second.csproj", "<Project />");
        await Assert.ThrowsAsync<ArgumentException>(() => ProjectSourceLoader.LoadAsync(config));
    }
}
