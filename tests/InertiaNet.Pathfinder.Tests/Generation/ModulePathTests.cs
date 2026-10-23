using FluentAssertions;
using InertiaNet.Pathfinder.Generation;

namespace InertiaNet.Pathfinder.Tests.Generation;

public class ModulePathTests
{
    [Fact]
    public void ControllerPaths_AreUniqueAcrossRootNamespacesReservedBarrelsAndNaturalSuffixes()
    {
        string[] types = ["First.Controllers.ItemsController", "Second.Controllers.ItemsController", "App.Controllers.IndexController", "App.Controllers.indexController", "__MinimalApi__", "MinimalApi"];
        var paths = ModulePaths.Actions(types);
        paths.Values.Select(value => value.ToLowerInvariant()).Should().OnlyHaveUniqueItems();
        paths.Values.Should().NotContain("index");
        ModulePaths.Actions(types.Reverse()).Should().BeEquivalentTo(paths);
    }

    [Fact]
    public void RouteDirectories_StayInsideOutputAndPreserveAllCaseAndPunctuationVariants()
    {
        var directories = ModulePaths.RouteDirectories(["Users.show", "users.index", "some-name.index", "some_name.index", "a.b.index", "..empty", "../escaped.index"]);
        directories.Values.Select(value => value.ToLowerInvariant()).Should().OnlyHaveUniqueItems();
        directories.Values.Should().OnlyContain(value => !value.Contains("..") && !value.Contains('\\'));
        directories["a.b"].Should().Be(directories["a"] + "/b");
    }
}
