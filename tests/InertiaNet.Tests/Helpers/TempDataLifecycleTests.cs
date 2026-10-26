using FluentAssertions;

namespace InertiaNet.Tests.Helpers;

public class TempDataLifecycleTests
{
    [Fact]
    public void ReadsExpireWhilePeekAndSelectedKeepSurviveTheNextRequest()
    {
        var seed = new FakeTempData { ["read"] = "one", ["kept"] = "two", ["peeked"] = "three" };
        var request = seed.NextRequest();
        request["read"].Should().Be("one");
        request.TryGetValue("kept", out var kept).Should().BeTrue();
        kept.Should().Be("two");
        request.Keep("kept");
        request.Peek("peeked").Should().Be("three");
        var next = request.NextRequest();
        next.ContainsKey("read").Should().BeFalse();
        next["kept"].Should().Be("two");
        next["peeked"].Should().Be("three");
        next.NextRequest().Should().BeEmpty();
    }

    [Fact]
    public void KeepAllRetainsReadValuesButDoesNotResurrectRemovedEntries()
    {
        var seed = new FakeTempData { ["kept"] = "one", ["removed"] = "two" };
        var request = seed.NextRequest();
        request["kept"].Should().Be("one");
        request.Remove("removed");
        request.Keep();
        var next = request.NextRequest();
        next["kept"].Should().Be("one");
        next.ContainsKey("removed").Should().BeFalse();
    }
}
