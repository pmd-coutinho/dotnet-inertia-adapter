using FluentAssertions;
using Wolverine.Http;
using Xunit;

namespace InertiaNet.Wolverine.Tests;

public class WolverineHttpOptionsExtensionsTests
{
    [Fact]
    public void UseInertia_ShouldRejectNullOptions()
    {
        var act = () => WolverineHttpOptionsExtensions.UseInertia(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
