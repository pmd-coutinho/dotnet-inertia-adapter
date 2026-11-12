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
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace InertiaNet.Tests.Middleware;

public class InertiaValidationFilterTests
{
    private static ResultExecutingContext CreateResultContext(
        IActionResult result,
        ModelStateDictionary? modelState = null,
        FakeTempDataFactory? tempDataFactory = null,
        Dictionary<string, string>? headers = null,
        Action<InertiaOptions>? configure = null)
    {
        headers ??= new() { [HeaderNames.Inertia] = "true" };
        var httpContext = HttpContextHelper.Create(headers: headers, configureServices: services =>
        {
            if (configure is not null) services.Configure(configure);
            if (tempDataFactory is not null)
                services.AddSingleton<ITempDataDictionaryFactory>(tempDataFactory);
        });

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor(),
            modelState ?? new ModelStateDictionary());

        return new ResultExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            result,
            controller: null!);
    }

    [Fact]
    public async Task OnResultExecutionAsync_ShouldSerializeErrorsToTempData_WhenModelStateInvalidAndRedirect()
    {
        var tempDataFactory = new FakeTempDataFactory();
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Email", "Email is required");
        modelState.AddModelError("Name", "Name is too short");

        var context = CreateResultContext(
            new RedirectResult("/create"),
            modelState,
            tempDataFactory);

        var filter = new InertiaValidationFilter();
        var nextCalled = false;
        await filter.OnResultExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ResultExecutedContext(
                context, new List<IFilterMetadata>(), context.Result, controller: null!));
        });

        nextCalled.Should().BeTrue();
        tempDataFactory.TempData[SessionKeys.ValidationErrors].Should().NotBeNull();
        var errorsJson = (string)tempDataFactory.TempData[SessionKeys.ValidationErrors]!;
        var errors = JsonSerializer.Deserialize<Dictionary<string, string[]>>(errorsJson);
        errors.Should().ContainKey("email"); // camelCase
        errors.Should().ContainKey("name");
    }

    [Fact]
    public async Task OnResultExecutionAsync_ShouldNotTouchTempData_WhenModelStateIsValid()
    {
        var tempDataFactory = new FakeTempDataFactory();
        var context = CreateResultContext(
            new RedirectResult("/home"),
            new ModelStateDictionary(),
            tempDataFactory);

        var filter = new InertiaValidationFilter();
        await filter.OnResultExecutionAsync(context, () =>
            Task.FromResult(new ResultExecutedContext(
                context, new List<IFilterMetadata>(), context.Result, controller: null!)));

        tempDataFactory.TempData.Count.Should().Be(0);
    }

    [Fact]
    public async Task OnResultExecutionAsync_ShouldForwardErrorBag_WhenHeaderPresent()
    {
        var tempDataFactory = new FakeTempDataFactory();
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Field", "Error");

        var context = CreateResultContext(
            new RedirectResult("/create"),
            modelState,
            tempDataFactory,
            headers: new Dictionary<string, string> { ["X-Inertia"] = "true", ["X-Inertia-Error-Bag"] = "loginForm" });

        var filter = new InertiaValidationFilter();
        await filter.OnResultExecutionAsync(context, () =>
            Task.FromResult(new ResultExecutedContext(
                context, new List<IFilterMetadata>(), context.Result, controller: null!)));

        tempDataFactory.TempData[SessionKeys.ErrorBag].Should().Be("loginForm");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonInertiaOrExcludedRequests_DoNotCaptureMvcErrors(bool excluded)
    {
        var data = new FakeTempDataFactory();
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Email", "Required");
        var context = CreateResultContext(new RedirectResult("/form"), modelState, data,
            headers: excluded ? new() { [HeaderNames.Inertia] = "true" } : new(),
            configure: options => options.RequestPredicate = _ => !excluded);
        var nextCalled = false;
        await new InertiaValidationFilter().OnResultExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ResultExecutedContext(context, [], context.Result, null!));
        });
        nextCalled.Should().BeTrue();
        data.TempData.Should().BeEmpty();
    }

    [Fact]
    public async Task OnResultExecutionAsync_ShouldNotTouchTempData_WhenResultIsNotRedirect()
    {
        var tempDataFactory = new FakeTempDataFactory();
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Field", "Error");

        var context = CreateResultContext(
            new OkResult(),
            modelState,
            tempDataFactory);

        var filter = new InertiaValidationFilter();
        await filter.OnResultExecutionAsync(context, () =>
            Task.FromResult(new ResultExecutedContext(
                context, new List<IFilterMetadata>(), context.Result, controller: null!)));

        tempDataFactory.TempData.Count.Should().Be(0);
    }
    [Theory]
    [InlineData(ValidationErrorKeyFormat.Brackets, "address.street", "values[3].code", "prices[EUR]")]
    [InlineData(ValidationErrorKeyFormat.Dots, "address.street", "values.3.code", "prices.EUR")]
    [InlineData(ValidationErrorKeyFormat.Preserve, "Address.Street", "Values[3].Code", "Prices[EUR]")]
    public async Task OnResultExecutionAsync_ShouldNormalizeNestedAndIndexedKeysPerSegment(
        ValidationErrorKeyFormat format, string nested, string indexed, string dictionaryKeyed)
    {
        var tempDataFactory = new FakeTempDataFactory();
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Address.Street", "Street is required");
        modelState.AddModelError("Values[3].Code", "Code is invalid");
        modelState.AddModelError("Prices[EUR]", "Price is required");
        modelState.AddModelError("email", "Email is required");

        var context = CreateResultContext(new RedirectResult("/create"), modelState, tempDataFactory,
            configure: options => options.ValidationErrorKeyFormat = format);

        await new InertiaValidationFilter().OnResultExecutionAsync(context, () =>
            Task.FromResult(new ResultExecutedContext(context, [], context.Result, controller: null!)));

        var errors = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            (string)tempDataFactory.TempData[SessionKeys.ValidationErrors]!)!;
        errors.Keys.Should().BeEquivalentTo(nested, indexed, dictionaryKeyed, "email");
        errors[nested].Should().Equal("Street is required");
        errors[indexed].Should().Equal("Code is invalid");
    }

    [Fact]
    public async Task OnResultExecutionAsync_ShouldUseTheConfiguredPropertyNamingPolicy()
    {
        var tempDataFactory = new FakeTempDataFactory();
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("ShippingAddress.PostCode", "Required");

        var context = CreateResultContext(new RedirectResult("/create"), modelState, tempDataFactory,
            configure: options => options.JsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            });

        await new InertiaValidationFilter().OnResultExecutionAsync(context, () =>
            Task.FromResult(new ResultExecutedContext(context, [], context.Result, controller: null!)));

        var errors = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            (string)tempDataFactory.TempData[SessionKeys.ValidationErrors]!)!;
        errors.Keys.Should().Equal("shipping_address.post_code");
    }

    [Theory]
    [InlineData(false, "email", "address.street", "values.3.code")]
    [InlineData(true, "Email", "Address.Street", "Values.3.Code")]
    public async Task OnResultExecutionAsync_ShouldNotRenameMembersWhenTheConfiguredPolicyIsNull(
        bool customOptions, string flat, string nested, string indexed)
    {
        var tempDataFactory = new FakeTempDataFactory();
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Email", "Email is required");
        modelState.AddModelError("Address.Street", "Street is required");
        modelState.AddModelError("Values[3].Code", "Code is invalid");

        var context = CreateResultContext(new RedirectResult("/create"), modelState, tempDataFactory,
            configure: options =>
            {
                options.ValidationErrorKeyFormat = ValidationErrorKeyFormat.Dots;
                // Custom options with no PropertyNamingPolicy serialize props with CLR names, so keys keep them too.
                if (customOptions) options.JsonSerializerOptions = new JsonSerializerOptions();
            });

        await new InertiaValidationFilter().OnResultExecutionAsync(context, () =>
            Task.FromResult(new ResultExecutedContext(context, [], context.Result, controller: null!)));

        var errors = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            (string)tempDataFactory.TempData[SessionKeys.ValidationErrors]!)!;
        errors.Keys.Should().BeEquivalentTo(flat, nested, indexed);
    }
}
