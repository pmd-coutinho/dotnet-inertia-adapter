using FluentAssertions;
using InertiaNet.Testing;

namespace InertiaNet.Tests.Testing;

public class AssertableInertiaPageTests
{
    [Fact]
    public void HasProp_ShouldSupportNestedPaths_AndExactJsonComparison()
    {
        var page = AssertableInertiaPage.FromJson(
            """
            {
              "component": "Posts/Index",
              "props": {
                "posts": [
                  { "title": "Hello", "tags": ["dotnet", "inertia"] },
                  { "title": "World", "tags": [] }
                ],
                "profile": { "name": "Alice Cooper" }
              },
              "url": "/posts"
            }
            """);

        page.HasProp("posts[0].title", "Hello")
            .HasProp("posts[0].tags[1]", "inertia")
            .HasPropCount("posts", 2)
            .HasPropCount("posts[0].tags", 2)
            .DoesNotHaveProp("posts[2]")
            .DoesNotHaveProp("posts[0].missing");

        var act = () => page.HasProp("profile.name", "Alice");

        act.Should().Throw<AssertionException>();
    }

    [Fact]
    public void HasFlash_ShouldSupportNestedPaths_AndAbsenceChecks()
    {
        var page = AssertableInertiaPage.FromJson(
            """
            {
              "component": "Users/Create",
              "props": {},
              "flash": {
                "message": "Created",
                "meta": { "level": "success" }
              },
              "url": "/users/create"
            }
            """);

        page.HasFlash("message", "Created")
            .HasFlash("meta.level", "success")
            .DoesNotHaveFlash("errors")
            .DoesNotHaveFlash("meta.code");
    }
}
