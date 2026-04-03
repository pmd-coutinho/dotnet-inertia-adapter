using System.Net;
using System.Text;
using FluentAssertions;
using InertiaNet.Support;
using InertiaNet.Testing;

namespace InertiaNet.Tests.Testing;

public class InertiaTestExtensionsTests
{
    [Fact]
    public async Task AssertInertiaAsync_ShouldParseInertiaPagePayload()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"component\":\"Posts/Index\",\"props\":{\"posts\":[]},\"url\":\"/posts\"}",
                Encoding.UTF8,
                "application/json"),
        };
        response.Headers.Add(HeaderNames.Inertia, "true");

        var page = await response.AssertInertiaAsync();

        page.HasComponent("Posts/Index").HasProp("posts").HasUrl("/posts");
    }

    [Fact]
    public async Task AssertInertiaAsync_ShouldRequireInertiaResponseHeader()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"component\":\"Posts/Index\",\"props\":{},\"url\":\"/posts\"}",
                Encoding.UTF8,
                "application/json"),
        };

        var act = () => response.AssertInertiaAsync();

        await act.Should().ThrowAsync<AssertionException>();
    }

    [Fact]
    public void AssertInertiaRedirect_ShouldAccept_409Conflict_WithInertiaLocationHeader()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict);
        response.Headers.Add(HeaderNames.Location, "/external");

        var redirect = response.AssertInertiaRedirect();

        redirect.Location.Should().Be("/external");
    }

    [Fact]
    public void AssertFragmentRedirect_ShouldAccept_409Conflict_WithFragmentHeader()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict);
        response.Headers.Add("X-Inertia-Redirect", "/reports#summary");

        var redirect = response.AssertFragmentRedirect();

        redirect.RedirectUrl.Should().Be("/reports#summary");
    }

    [Fact]
    public void AssertVersionRedirect_ShouldAccept_409Conflict_WithInertiaLocationHeader()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict);
        response.Headers.Add(HeaderNames.Location, "/dashboard");

        var redirect = response.AssertVersionRedirect();

        redirect.RedirectUrl.Should().Be("/dashboard");
    }
}
