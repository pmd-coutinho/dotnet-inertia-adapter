using FluentAssertions;
using InertiaNet.Ssr;

namespace InertiaNet.Tests.Ssr;

public class SsrPathMatcherTests
{
    [Theory]
    [InlineData("/admin", "/admin", true)]
    [InlineData("/admin/users", "/admin", false)]
    [InlineData("/admin", "/admin/*", true)]
    [InlineData("/admin/users", "/admin/*", true)]
    [InlineData("/admin/users/42", "/admin/*", true)]
    [InlineData("/reports/2024", "/reports/*/summary", false)]
    [InlineData("/reports/2024/summary", "/reports/*/summary", true)]
    [InlineData("/", "/*", true)]
    public void IsMatch_ShouldRespectExactAndWildcardPatterns(string path, string pattern, bool expected)
    {
        SsrPathMatcher.IsMatch(path, pattern).Should().Be(expected);
    }

    [Fact]
    public void IsExcluded_ShouldMatchAnyConfiguredPattern()
    {
        var patterns = new[] { "/admin/*", "/reports" };

        SsrPathMatcher.IsExcluded("/reports", patterns).Should().BeTrue();
        SsrPathMatcher.IsExcluded("/admin/users", patterns).Should().BeTrue();
        SsrPathMatcher.IsExcluded("/dashboard", patterns).Should().BeFalse();
    }
}
