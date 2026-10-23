using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
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

    private static async Task<WebApplication> App()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<PrecognitionSideEffects>();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(PrecognitionProbeController).Assembly);
        builder.Services.AddInertia();
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
