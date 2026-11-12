using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaNet.Tests.Integration;

public class PrecognitionIntegrationTests
{
    [Theory]
    [InlineData("mvc")]
    [InlineData("minimal")]
    public async Task InvalidValidationOnlyRequest_UsesClientContractAndSkipsSideEffects(string endpoint)
    {
        await using var app = await App();
        using var client = app.GetTestClient();
        using var request = Request(endpoint, new { email = "invalid", name = "", addresses = new[] { new { street = "" } } });
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Headers.GetValues(HeaderNames.Precognition).Should().Equal("true");
        response.Headers.Contains(HeaderNames.PrecognitionSuccess).Should().BeFalse();
        response.Headers.Vary.Should().Contain(HeaderNames.Precognition).And.Contain(HeaderNames.Inertia);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = doc.RootElement.GetProperty("errors");
        errors.GetProperty("email").GetArrayLength().Should().BeGreaterThan(0);
        errors.GetProperty("name").GetArrayLength().Should().BeGreaterThan(0);
        errors.GetProperty("addresses.0.street").GetArrayLength().Should().BeGreaterThan(0);
        app.Services.GetRequiredService<PrecognitionSideEffects>().Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("mvc")]
    [InlineData("minimal")]
    public async Task FieldSelection_IgnoresErrorsOutsideTheSelectedFields(string endpoint)
    {
        await using var app = await App();
        using var client = app.GetTestClient();
        using var request = Request(endpoint, new { email = "ana@example.com", name = "" });
        request.Headers.Add(HeaderNames.PrecognitionValidateOnly, "email");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues(HeaderNames.Precognition).Should().Equal("true");
        response.Headers.GetValues(HeaderNames.PrecognitionSuccess).Should().Equal("true");
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
        app.Services.GetRequiredService<PrecognitionSideEffects>().Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("mvc")]
    [InlineData("minimal")]
    public async Task NestedFieldSelection_UsesTheSameDotPathsForMvcAndMinimalApis(string endpoint)
    {
        await using var app = await App();
        using var client = app.GetTestClient();
        using var request = Request(endpoint, new { email = "invalid", name = "", addresses = new[] { new { street = "Main" }, new { street = "" } } });
        request.Headers.Add(HeaderNames.PrecognitionValidateOnly, "addresses.1.street");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().Equal("addresses.1.street");
        app.Services.GetRequiredService<PrecognitionSideEffects>().Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("mvc", null)]
    [InlineData("minimal", null)]
    [InlineData("mvc", "Addresses.1.Street")]
    [InlineData("minimal", "Addresses.1.Street")]
    [InlineData("mvc", "addresses.1.street")]
    [InlineData("minimal", "addresses[1].street")]
    public async Task NullPropertyNamingPolicy_ReturnsClrNamedKeysThatSelectThemselves(string endpoint, string? validateOnly)
    {
        // Custom options without a property naming policy serialize props with CLR names; error keys match them.
        await using var app = await App(options => options.JsonSerializerOptions = new JsonSerializerOptions());
        using var client = app.GetTestClient();
        using var request = Request(endpoint, new { email = "invalid", name = "", addresses = new[] { new { street = "Main" }, new { street = "" } } });
        if (validateOnly is not null) request.Headers.Add(HeaderNames.PrecognitionValidateOnly, validateOnly);
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var keys = doc.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name);
        if (validateOnly is null) keys.Should().BeEquivalentTo("Email", "Name", "Addresses.1.Street");
        else keys.Should().Equal("Addresses.1.Street");
        app.Services.GetRequiredService<PrecognitionSideEffects>().Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("mvc", false, false)]
    [InlineData("minimal", false, false)]
    [InlineData("mvc", true, false)]
    [InlineData("minimal", true, false)]
    [InlineData("mvc", false, true)]
    [InlineData("minimal", false, true)]
    public async Task ErrorBody_IgnoresTheApplicationJsonOptions(string endpoint, bool nullInertiaPolicy, bool renameProperties)
    {
        // Application JSON options that would rename the error keys (dictionary policy) and, with a renaming
        // property policy, the envelope, if the 422 body went through the MVC or Minimal API serializer.
        void Hostile(JsonSerializerOptions json)
        {
            json.PropertyNamingPolicy = renameProperties ? JsonNamingPolicy.KebabCaseUpper : null;
            json.DictionaryKeyPolicy = JsonNamingPolicy.KebabCaseUpper;
        }
        await using var app = await App(
            options => { if (nullInertiaPolicy) options.JsonSerializerOptions = new JsonSerializerOptions(); },
            Hostile);
        using var client = app.GetTestClient();
        using var request = Request(endpoint, new { email = "invalid", name = "", addresses = new[] { new { street = "Main" }, new { street = "" } } });
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("message", "errors");
        doc.RootElement.GetProperty("message").GetString().Should().Be("The given data was invalid.");
        var keys = doc.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name);
        if (nullInertiaPolicy) keys.Should().BeEquivalentTo("Email", "Name", "Addresses.1.Street");
        else keys.Should().BeEquivalentTo("email", "name", "addresses.1.street");
        app.Services.GetRequiredService<PrecognitionSideEffects>().Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("mvc")]
    [InlineData("minimal")]
    public async Task OrdinarySubmission_StillExecutesTheHandler(string endpoint)
    {
        await using var app = await App();
        using var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync($"/precognition/{endpoint}", new { email = "ana@example.com", name = "Ana" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        app.Services.GetRequiredService<PrecognitionSideEffects>().Calls.Should().Be(1);
    }

    [Fact]
    public async Task OrdinaryApiControllerValidation_RemainsEnabled()
    {
        await using var app = await App();
        using var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync("/precognition/mvc", new { email = "invalid", name = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        app.Services.GetRequiredService<PrecognitionSideEffects>().Calls.Should().Be(0);
    }

    private static HttpRequestMessage Request(string endpoint, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/precognition/{endpoint}") { Content = JsonContent.Create(payload) };
        request.Headers.Add(HeaderNames.Precognition, "true");
        request.Headers.Add(HeaderNames.Inertia, "true");
        return request;
    }

    private static async Task<WebApplication> App(Action<InertiaOptions>? configure = null,
        Action<JsonSerializerOptions>? configureApplicationJson = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<PrecognitionSideEffects>();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(PrecognitionProbeController).Assembly)
            .AddJsonOptions(options => configureApplicationJson?.Invoke(options.JsonSerializerOptions));
        builder.Services.ConfigureHttpJsonOptions(options => configureApplicationJson?.Invoke(options.SerializerOptions));
        builder.Services.AddInertia(configure ?? (_ => { }));
        var app = builder.Build();
        app.UseInertia();
        app.MapControllers();
        app.MapGroup("/precognition").WithInertiaPrecognition()
            .MapPost("/minimal", ([FromBody] PrecognitionInput input, PrecognitionSideEffects effects) =>
            { effects.Calls++; return Results.Ok(new { saved = true }); });
        await app.StartAsync();
        return app;
    }
}

public sealed class PrecognitionSideEffects { public int Calls { get; set; } }
public sealed class PrecognitionInput
{
    [Required, EmailAddress] public string? Email { get; set; }
    [Required] public string? Name { get; set; }
    public List<PrecognitionAddress>? Addresses { get; set; }
}
public sealed class PrecognitionAddress { [Required] public string? Street { get; set; } }

[ApiController]
[Route("precognition/mvc")]
public sealed class PrecognitionProbeController(PrecognitionSideEffects effects) : ControllerBase
{
    [HttpPost]
    public IActionResult Store(PrecognitionInput input) { effects.Calls++; return Ok(new { saved = true }); }
}
