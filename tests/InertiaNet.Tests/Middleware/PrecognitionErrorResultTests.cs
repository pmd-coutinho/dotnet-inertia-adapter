using FluentAssertions;
using InertiaNet.Middleware;
using InertiaNet.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace InertiaNet.Tests.Middleware;

public class PrecognitionErrorResultTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldWriteTheLaravelEnvelopeWithKeysAsGiven()
    {
        var context = HttpContextHelper.Create("POST", "/submit");
        var result = new PrecognitionErrorResult(new Dictionary<string, string[]>
        {
            ["Email"] = ["Required", "Must be an \"email\" address"],
            ["first name"] = ["Café <b>&</b>"],
            ["prices.EUR"] = [],
        });

        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        context.Response.ContentType.Should().Be("application/json; charset=utf-8");
        using var doc = JsonDocument.Parse(await context.ReadResponseBodyAsync());
        doc.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("message", "errors");
        doc.RootElement.GetProperty("message").GetString().Should().Be(PrecognitionErrorResult.Message);
        var errors = doc.RootElement.GetProperty("errors");
        errors.EnumerateObject().Select(p => p.Name).Should().Equal("Email", "first name", "prices.EUR");
        errors.GetProperty("Email").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("Required", "Must be an \"email\" address");
        errors.GetProperty("first name")[0].GetString().Should().Be("Café <b>&</b>");
        errors.GetProperty("prices.EUR").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void Result_ShouldReportItsStatusContentTypeAndEnvelope_ToFilters()
    {
        var errors = new Dictionary<string, string[]> { ["email"] = ["Required"] };
        var result = new PrecognitionErrorResult(errors);

        ((IStatusCodeHttpResult)result).StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)result).StatusCode
            .Should().Be(StatusCodes.Status422UnprocessableEntity);
        ((IContentTypeHttpResult)result).ContentType.Should().Be("application/json; charset=utf-8");
        JsonSerializer.Serialize(((IValueHttpResult)result).Value)
            .Should().Be("""{"message":"The given data was invalid.","errors":{"email":["Required"]}}""");
    }
}
