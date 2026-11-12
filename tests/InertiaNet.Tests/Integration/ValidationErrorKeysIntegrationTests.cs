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

    [Fact]
    public async Task ErrorBagName_IsNotRenamedByTheDictionaryKeyPolicy()
    {
        await using var app = await App(options => options.JsonSerializerOptions = new JsonSerializerOptions
        {
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

    private static async Task<WebApplication> App(Action<InertiaOptions> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ValidationKeysProbeController).Assembly);
        builder.Services.Replace(ServiceDescriptor.Singleton<ITempDataDictionaryFactory>(new FakeTempDataFactory()));
        builder.Services.AddInertia(configure);
        var app = builder.Build();
        app.UseInertia();
        app.MapControllers();
        app.MapGet("/validation-keys/form", (IInertiaService inertia) => inertia.Render("Form", new { title = "Form" }));
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
