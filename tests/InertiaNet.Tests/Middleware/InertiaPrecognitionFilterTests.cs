using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Middleware;
using InertiaNet.Support;
using InertiaNet.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace InertiaNet.Tests.Middleware;

public class InertiaPrecognitionFilterTests
{
    private static ActionExecutingContext CreateActionContext(
        ModelStateDictionary? modelState = null,
        Dictionary<string, string>? headers = null,
        Action<InertiaOptions>? configure = null)
    {
        var httpContext = HttpContextHelper.Create("POST", "/submit", headers, services =>
        {
            if (configure is not null) services.Configure(configure);
        });
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor(),
            modelState ?? new ModelStateDictionary());

        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: null!);
    }

    [Fact]
    public async Task OnActionExecutionAsync_ShouldReturn204_WhenPrecognitionHeaderPresentAndValid()
    {
        var context = CreateActionContext(
            headers: new() { [HeaderNames.Precognition] = "true" });

        var filter = new InertiaPrecognitionFilter();
        await filter.OnActionExecutionAsync(context, () =>
            throw new InvalidOperationException("Next should not be called"));

        context.Result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(204);
        context.HttpContext.Response.Headers[HeaderNames.PrecognitionSuccess].ToString()
            .Should().Be("true");
    }

    [Fact]
    public async Task OnActionExecutionAsync_ShouldReturn422_WhenPrecognitionHeaderPresentAndInvalid()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Email", "Required");

        var context = CreateActionContext(
            modelState,
            headers: new() { [HeaderNames.Precognition] = "true" });

        var filter = new InertiaPrecognitionFilter();
        await filter.OnActionExecutionAsync(context, () =>
            throw new InvalidOperationException("Next should not be called"));

        context.Result.Should().BeOfType<PrecognitionErrorResult>();
        await context.Result!.ExecuteResultAsync(context);
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        context.HttpContext.Response.ContentType.Should().Be("application/json; charset=utf-8");
        (await context.HttpContext.ReadResponseBodyAsync()).Should()
            .Be("{\"message\":\"The given data was invalid.\",\"errors\":{\"email\":[\"Required\"]}}");
    }

    [Fact]
    public async Task OnActionExecutionAsync_ShouldCallNext_WhenNoPrecognitionHeader()
    {
        var context = CreateActionContext();

        var filter = new InertiaPrecognitionFilter();
        var nextCalled = false;
        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(
                context, new List<IFilterMetadata>(), controller: null!));
        });

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task OnActionExecutionAsync_ShouldSetVaryHeader_WhenPrecognitionHeaderPresent()
    {
        var context = CreateActionContext(
            headers: new() { [HeaderNames.Precognition] = "true" });

        var filter = new InertiaPrecognitionFilter();
        await filter.OnActionExecutionAsync(context, () =>
            throw new InvalidOperationException("Next should not be called"));

        context.HttpContext.Response.Headers["Vary"].ToString()
            .Should().Contain(HeaderNames.Precognition);
    }

    [Theory]
    [InlineData(ValidationErrorKeyFormat.Brackets)]
    [InlineData(ValidationErrorKeyFormat.Dots)]
    [InlineData(ValidationErrorKeyFormat.Preserve)]
    public async Task OnActionExecutionAsync_ShouldAlwaysReturnDotPathErrorKeys(ValidationErrorKeyFormat format)
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Email", "Required");
        modelState.AddModelError("Address.Street", "Required");
        modelState.AddModelError("Values[3].Code", "Invalid");
        modelState.AddModelError("Prices[EUR]", "Required");
        modelState.AddModelError("name", "Required");
        var context = CreateActionContext(modelState, new() { [HeaderNames.Precognition] = "true" },
            options => options.ValidationErrorKeyFormat = format);

        await new InertiaPrecognitionFilter().OnActionExecutionAsync(context, () =>
            throw new InvalidOperationException("Next should not be called"));

        (await ErrorKeys(context)).Should().BeEquivalentTo("email", "address.street", "values.3.code", "prices.EUR", "name");
    }

    [Theory]
    [InlineData("values.3.code", "values.3.code")]
    [InlineData("values", "values.3.code")]
    [InlineData("address", "address.street")]
    public async Task OnActionExecutionAsync_ShouldSelectNestedAndIndexedFields(string validateOnly, string expected)
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Email", "Required");
        modelState.AddModelError("Address.Street", "Required");
        modelState.AddModelError("Values[3].Code", "Invalid");
        var context = CreateActionContext(modelState, new()
        {
            [HeaderNames.Precognition] = "true",
            [HeaderNames.PrecognitionValidateOnly] = validateOnly,
        });

        await new InertiaPrecognitionFilter().OnActionExecutionAsync(context, () =>
            throw new InvalidOperationException("Next should not be called"));

        (await ErrorKeys(context)).Should().Equal(expected);
    }

    [Fact]
    public async Task OnActionExecutionAsync_ShouldUseTheConfiguredPropertyNamingPolicy()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("ShippingAddress.PostCode", "Required");
        modelState.AddModelError("Email", "Required");
        var context = CreateActionContext(modelState, new()
        {
            [HeaderNames.Precognition] = "true",
            [HeaderNames.PrecognitionValidateOnly] = "shipping_address.post_code",
        }, options => options.JsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });

        await new InertiaPrecognitionFilter().OnActionExecutionAsync(context, () =>
            throw new InvalidOperationException("Next should not be called"));

        (await ErrorKeys(context)).Should().Equal("shipping_address.post_code");
    }

    [Theory]
    [InlineData(null, "Email", "ShippingAddress.PostCode", "Values.3.Code")]
    [InlineData("ShippingAddress.PostCode", "ShippingAddress.PostCode")]
    [InlineData("shippingAddress.postCode", "ShippingAddress.PostCode")]
    [InlineData("values[3][code]", "Values.3.Code")]
    public async Task OnActionExecutionAsync_ShouldNotRenameMembersWhenTheConfiguredPolicyIsNull(string? validateOnly, params string[] expected)
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Email", "Required");
        modelState.AddModelError("ShippingAddress.PostCode", "Required");
        modelState.AddModelError("Values[3].Code", "Invalid");
        var headers = new Dictionary<string, string> { [HeaderNames.Precognition] = "true" };
        if (validateOnly is not null) headers[HeaderNames.PrecognitionValidateOnly] = validateOnly;
        var context = CreateActionContext(modelState, headers,
            options => options.JsonSerializerOptions = new JsonSerializerOptions());

        await new InertiaPrecognitionFilter().OnActionExecutionAsync(context, () =>
            throw new InvalidOperationException("Next should not be called"));

        (await ErrorKeys(context)).Should().BeEquivalentTo(expected);
    }

    public static TheoryData<string> NamingConfigurations => ["default", "null_policy", "snake_case", "dictionary_policy"];

    [Theory]
    [MemberData(nameof(NamingConfigurations))]
    public async Task OnActionExecutionAsync_EveryReturnedKeySelectsItselfWhenSentBack(string naming)
    {
        Action<InertiaOptions> configure = naming switch
        {
            "null_policy" => options => options.JsonSerializerOptions = new JsonSerializerOptions(),
            "snake_case" => options => options.JsonSerializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower },
            "dictionary_policy" => options => options.JsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DictionaryKeyPolicy = JsonNamingPolicy.KebabCaseLower,
            },
            _ => _ => { },
        };
        static ModelStateDictionary ModelState()
        {
            var modelState = new ModelStateDictionary();
            foreach (var key in new[] { "Email", "ShippingAddress.PostCode", "Values[3].Code", "Prices[EUR]", "Prices[EurRate].Amount", "Addresses[0].ID", "$.lines[0].quantity", "$['first name']" })
                modelState.AddModelError(key, "Invalid");
            return modelState;
        }

        var all = CreateActionContext(ModelState(), new() { [HeaderNames.Precognition] = "true" }, configure);
        await new InertiaPrecognitionFilter().OnActionExecutionAsync(all, () =>
            throw new InvalidOperationException("Next should not be called"));
        var returned = (await ErrorKeys(all)).ToList();
        returned.Should().HaveCount(8);

        foreach (var key in returned)
        {
            var context = CreateActionContext(ModelState(), new()
            {
                [HeaderNames.Precognition] = "true",
                [HeaderNames.PrecognitionValidateOnly] = key,
            }, configure);

            await new InertiaPrecognitionFilter().OnActionExecutionAsync(context, () =>
                throw new InvalidOperationException("Next should not be called"));

            (await ErrorKeys(context)).Should().Contain(key, "sending back '{0}' must not report the field as valid", key);
        }
    }

    [Theory]
    [InlineData("prices.EUR", "prices.EUR")]
    [InlineData("Prices[EUR]", "prices.EUR")]
    [InlineData("prices.eur", "prices.EUR")]
    [InlineData("values[3][code]", "values.3.code")]
    public async Task OnActionExecutionAsync_ShouldSelectFieldsInEitherSpelling(string validateOnly, string expected)
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Prices[EUR]", "Required");
        modelState.AddModelError("Values[3].Code", "Invalid");
        var context = CreateActionContext(modelState, new()
        {
            [HeaderNames.Precognition] = "true",
            [HeaderNames.PrecognitionValidateOnly] = validateOnly,
        });

        await new InertiaPrecognitionFilter().OnActionExecutionAsync(context, () =>
            throw new InvalidOperationException("Next should not be called"));

        (await ErrorKeys(context)).Should().Equal(expected);
    }

    [Theory]
    [InlineData("value")]
    [InlineData("values.3.cod")]
    [InlineData("email")]
    public async Task OnActionExecutionAsync_ShouldReturn204_WhenOnlyUnrelatedFieldsAreSelected(string validateOnly)
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Values[3].Code", "Invalid");
        var context = CreateActionContext(modelState, new()
        {
            [HeaderNames.Precognition] = "true",
            [HeaderNames.PrecognitionValidateOnly] = validateOnly,
        });

        await new InertiaPrecognitionFilter().OnActionExecutionAsync(context, () =>
            throw new InvalidOperationException("Next should not be called"));

        context.Result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(204);
        context.HttpContext.Response.Headers[HeaderNames.PrecognitionSuccess].ToString().Should().Be("true");
    }

    // Reads the keys from the written body, not the result object, so the test sees what the client receives.
    private static async Task<IEnumerable<string>> ErrorKeys(ActionExecutingContext context)
    {
        await context.Result.Should().BeOfType<PrecognitionErrorResult>().Which.ExecuteResultAsync(context);
        using var doc = JsonDocument.Parse(await context.HttpContext.ReadResponseBodyAsync());
        return doc.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
    }
}
