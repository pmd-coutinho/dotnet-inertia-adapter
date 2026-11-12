using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Support;
using InertiaNet.Tests.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InertiaNet.Tests.Integration;

/// <summary>
/// Follows a failed submission's redirect and asserts the <c>errors</c> prop the client receives,
/// so every key transformation between ModelState and the page JSON is covered.
/// </summary>
public class ValidationErrorKeysIntegrationTests
{
    private static readonly object InvalidPayload = new
    {
        name = "",
        address = new { street = "" },
        lines = new[] { new { quantity = 1 }, new { quantity = 0 } },
    };

    [Theory]
    [InlineData("mvc", ValidationErrorKeyFormat.Brackets, "lines[1].quantity")]
    [InlineData("minimal", ValidationErrorKeyFormat.Brackets, "lines[1].quantity")]
    [InlineData("mvc", ValidationErrorKeyFormat.Dots, "lines.1.quantity")]
    [InlineData("minimal", ValidationErrorKeyFormat.Dots, "lines.1.quantity")]
    public async Task RedirectedErrors_ReachThePageWithTheSameKeysFromMvcAndMinimalApis(
        string endpoint, ValidationErrorKeyFormat format, string indexed)
    {
        await using var app = await App(options => options.ValidationErrorKeyFormat = format);
        using var client = app.GetTestClient();

        using var submit = await client.SendAsync(Request(HttpMethod.Post, $"/validation-keys/{endpoint}", InvalidPayload));
        submit.StatusCode.Should().Be(HttpStatusCode.SeeOther);

        var errors = await Errors(client);
        errors.Keys.Should().BeEquivalentTo("name", "address.street", indexed);
        errors[indexed].Should().Be("Quantity must be at least 1.");
    }

    [Theory]
    [InlineData("mvc")]
    [InlineData("minimal")]
    public async Task RedirectedErrors_UseDotPathsByDefault(string endpoint)
    {
        await using var app = await App(_ => { });
        using var client = app.GetTestClient();

        using var submit = await client.SendAsync(Request(HttpMethod.Post, $"/validation-keys/{endpoint}", InvalidPayload));
        submit.StatusCode.Should().Be(HttpStatusCode.SeeOther);

        (await Errors(client)).Keys.Should().BeEquivalentTo("name", "address.street", "lines.1.quantity");
    }

    [Fact]
    public async Task DefaultOptions_GivePrecognitionAndRedirectedErrorsTheSameKeys()
    {
        await using var app = await App(_ => { });
        using var client = app.GetTestClient();

        using var precognitive = Request(HttpMethod.Post, "/validation-keys/mvc", InvalidPayload);
        precognitive.Headers.Add(HeaderNames.Precognition, "true");
        using var validation = await client.SendAsync(precognitive);
        validation.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = JsonDocument.Parse(await validation.Content.ReadAsStringAsync());
        var precognitionKeys = body.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();

        using var submit = await client.SendAsync(Request(HttpMethod.Post, "/validation-keys/mvc", InvalidPayload));
        submit.StatusCode.Should().Be(HttpStatusCode.SeeOther);

        precognitionKeys.Should().Contain("lines.1.quantity");
        (await Errors(client)).Keys.Should().BeEquivalentTo(precognitionKeys);
    }

    [Fact]
    public async Task RedirectedErrors_UseTheConfiguredPropertyNamingPolicy()
    {
        await using var app = await App(options =>
        {
            options.ValidationErrorKeyFormat = ValidationErrorKeyFormat.Dots;
            options.JsonSerializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        });
        using var client = app.GetTestClient();

        using var submit = await client.SendAsync(Request(HttpMethod.Post, "/validation-keys/minimal", new
        {
            name = "Ana",
            address = new { street = "Main", postCode = "" },
            lines = Array.Empty<object>(),
        }));

        (await Errors(client)).Keys.Should().Equal("address.post_code");
    }

    [Theory]
    [InlineData(ValidationErrorKeyFormat.Preserve, "Address.PostCode")]
    [InlineData(ValidationErrorKeyFormat.Dots, "address.postCode")]
    public async Task RedirectedErrorKeys_AreNotRenamedAgainByTheDictionaryKeyPolicy(ValidationErrorKeyFormat format, string expected)
    {
        await using var app = await App(options =>
        {
            options.ValidationErrorKeyFormat = format;
            options.JsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            };
        });
        using var client = app.GetTestClient();

        using var submit = await client.SendAsync(Request(HttpMethod.Post, "/validation-keys/minimal", new
        {
            name = "Ana",
            address = new { street = "Main", postCode = "" },
            lines = Array.Empty<object>(),
        }));

        (await Errors(client)).Keys.Should().Equal(expected);
    }

    public static TheoryData<string, string?> NamingPolicies => new()
    {
        { "mvc", null }, { "minimal", null },
        { "mvc", "none" }, { "minimal", "none" },
        { "mvc", "snake" }, { "minimal", "snake" },
    };

    [Theory]
    [MemberData(nameof(NamingPolicies))]
    public async Task RedirectedErrorKeys_NameTheSerializedPropsUnderTheSameOptions(string endpoint, string? policy)
    {
        await using var app = await App(options =>
        {
            options.ValidationErrorKeyFormat = ValidationErrorKeyFormat.Dots;
            options.JsonSerializerOptions = policy switch
            {
                null => null,
                "none" => new JsonSerializerOptions(),
                _ => new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower },
            };
        });
        using var client = app.GetTestClient();

        using var submit = await client.SendAsync(Request(HttpMethod.Post, $"/validation-keys/{endpoint}", InvalidPayload));
        submit.StatusCode.Should().Be(HttpStatusCode.SeeOther);

        using var page = await client.SendAsync(Request(HttpMethod.Get, "/validation-keys/form"));
        using var doc = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        var props = doc.RootElement.GetProperty("props");
        var keys = props.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
        keys.Should().HaveCount(3);
        if (policy == "none") keys.Should().BeEquivalentTo("Name", "Address.Street", "Lines.1.Quantity");
        foreach (var key in keys)
        {
            var node = props.GetProperty("input");
            foreach (var segment in key.Split('.'))
            {
                if (node.ValueKind == JsonValueKind.Array)
                {
                    node = node[int.Parse(segment)];
                    continue;
                }
                node.TryGetProperty(segment, out var child).Should().BeTrue($"error key {key} should name a field of the input prop");
                node = child;
            }
        }
    }

    [Fact]
    public async Task ErrorBagName_IsNotRenamedByTheDictionaryKeyPolicy()
    {
        await using var app = await App(options => options.JsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        });
        using var client = app.GetTestClient();
        using var request = Request(HttpMethod.Post, "/validation-keys/minimal", new { name = "", lines = Array.Empty<object>() });
        request.Headers.Add(HeaderNames.ErrorBag, "createUser");

        using var submit = await client.SendAsync(request);

        using var page = await client.SendAsync(Request(HttpMethod.Get, "/validation-keys/form"));
        using var doc = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("props").GetProperty("errors").GetProperty("createUser")
            .EnumerateObject().Select(p => p.Name).Should().Contain("name");
    }

    [Fact]
    public async Task JsonBindingErrors_DropTheJsonPathRoot()
    {
        await using var app = await App(options => options.ValidationErrorKeyFormat = ValidationErrorKeyFormat.Dots);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/validation-keys/mvc")
        {
            Content = new StringContent("""{"name":"Ana","lines":[{"quantity":"many"}]}""", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(HeaderNames.Inertia, "true");

        using var submit = await client.SendAsync(request);

        submit.StatusCode.Should().Be(HttpStatusCode.SeeOther);
        // System.Text.Json reports "$.lines[0].quantity"; the "$" JSON path root is not a form field.
        (await Errors(client)).Keys.Should().Contain("lines.0.quantity").And.NotContain(key => key.StartsWith('$'));
    }

    [Fact]
    public async Task ErrorBagWithAllErrors_MergesTheMessagesOfKeysThatNormalizeToTheSamePath()
    {
        await using var app = await App(options =>
        {
            options.ValidationErrorKeyFormat = ValidationErrorKeyFormat.Dots;
            options.WithAllErrors = true;
        });
        using var client = app.GetTestClient();
        using var request = Request(HttpMethod.Post, "/validation-keys/merged");
        request.Headers.Add(HeaderNames.ErrorBag, "createOrder");

        using var submit = await client.SendAsync(request);
        submit.StatusCode.Should().Be(HttpStatusCode.SeeOther);

        using var page = await client.SendAsync(Request(HttpMethod.Get, "/validation-keys/form"));
        using var doc = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        var bag = doc.RootElement.GetProperty("props").GetProperty("errors").GetProperty("createOrder");
        bag.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("address.street", "lines.1.quantity");
        bag.GetProperty("address.street").EnumerateArray().Select(e => e.GetString()).Should().Equal("Required.", "Too short.");
        bag.GetProperty("lines.1.quantity").EnumerateArray().Select(e => e.GetString()).Should().Equal("Too low.", "Not a number.");
    }

    [Theory]
    [InlineData("mvc")]
    [InlineData("minimal")]
    public async Task RedirectedErrors_SurviveTheCookieTempDataProvider(string endpoint)
    {
        await using var app = await App(options => options.ValidationErrorKeyFormat = ValidationErrorKeyFormat.Dots, fakeTempData: false);
        using var client = app.GetTestClient();

        using var submit = await client.SendAsync(Request(HttpMethod.Post, $"/validation-keys/{endpoint}", InvalidPayload));
        submit.StatusCode.Should().Be(HttpStatusCode.SeeOther);
        // TestServer's client has no cookie jar, so carry the TempData cookie to the next request by hand.
        var cookies = submit.Headers.GetValues("Set-Cookie").Select(cookie => cookie.Split(';')[0]).ToList();
        cookies.Should().Contain(cookie => cookie.StartsWith(".AspNetCore.Mvc.CookieTempDataProvider=", StringComparison.Ordinal));

        using var follow = Request(HttpMethod.Get, "/validation-keys/form");
        follow.Headers.Add("Cookie", string.Join("; ", cookies));
        using var page = await client.SendAsync(follow);
        using var doc = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        var errors = doc.RootElement.GetProperty("props").GetProperty("errors");
        errors.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("name", "address.street", "lines.1.quantity");
        errors.GetProperty("lines.1.quantity").GetString().Should().Be("Quantity must be at least 1.");
    }

    private static async Task<Dictionary<string, string>> Errors(HttpClient client)
    {
        using var page = await client.SendAsync(Request(HttpMethod.Get, "/validation-keys/form"));
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("props").GetProperty("errors").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString()!);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, object? payload = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (payload is not null) request.Content = JsonContent.Create(payload);
        request.Headers.Add(HeaderNames.Inertia, "true");
        return request;
    }

    private static async Task<WebApplication> App(Action<InertiaOptions> configure, bool fakeTempData = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ValidationKeysProbeController).Assembly);
        if (fakeTempData)
            builder.Services.Replace(ServiceDescriptor.Singleton<ITempDataDictionaryFactory>(new FakeTempDataFactory()));
        builder.Services.AddInertia(configure);
        var app = builder.Build();
        app.UseInertia();
        app.MapControllers();
        app.MapGet("/validation-keys/form", (IInertiaService inertia) => inertia.Render("Form", new
        {
            title = "Form",
            input = new ValidationKeysInput { Name = "", Address = new() { Street = "" }, Lines = [new(), new()] },
        }));
        app.MapPost("/validation-keys/minimal", (HttpContext context, [FromBody] ValidationKeysInput input) =>
        {
            // The raw ASP.NET member paths a FluentValidation or DataAnnotations bridge would supply.
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrEmpty(input.Name)) errors["Name"] = ["The Name field is required."];
            if (string.IsNullOrEmpty(input.Address?.Street)) errors["Address.Street"] = ["The Street field is required."];
            if (input.Address is { PostCode: "" }) errors["Address.PostCode"] = ["The PostCode field is required."];
            for (var i = 0; i < input.Lines.Count; i++)
                if (input.Lines[i].Quantity < 1) errors[$"Lines[{i}].Quantity"] = ["Quantity must be at least 1."];
            context.SetInertiaValidationErrors(new ValidationProblemDetails(errors));
            return Results.Redirect("/validation-keys/form");
        }).WithInertiaValidation();
        app.MapPost("/validation-keys/merged", (HttpContext context) =>
        {
            // ASP.NET member paths and hand-written client paths for the same fields.
            context.SetInertiaValidationErrors(new Dictionary<string, string[]>
            {
                ["Address.Street"] = ["Required."],
                ["address.street"] = ["Too short."],
                ["Lines[1].Quantity"] = ["Too low."],
                ["$.lines[1].quantity"] = ["Not a number."],
            });
            return Results.Redirect("/validation-keys/form");
        }).WithInertiaValidation();
        await app.StartAsync();
        return app;
    }
}

public sealed class ValidationKeysInput
{
    [Required] public string? Name { get; set; }
    public ValidationKeysAddress? Address { get; set; }
    public List<ValidationKeysLine> Lines { get; set; } = [];
}

public sealed class ValidationKeysAddress
{
    [Required] public string? Street { get; set; }
    public string? PostCode { get; set; }
}

public sealed class ValidationKeysLine
{
    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1.")] public int Quantity { get; set; }
}

[Route("validation-keys/mvc")]
public sealed class ValidationKeysProbeController : Controller
{
    [HttpPost]
    public IActionResult Store([FromBody] ValidationKeysInput input)
        => ModelState.IsValid ? Ok() : Redirect("/validation-keys/form");
}
