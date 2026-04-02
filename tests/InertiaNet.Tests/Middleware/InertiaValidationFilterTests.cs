using FluentAssertions;
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
        Dictionary<string, string>? headers = null)
    {
        var httpContext = HttpContextHelper.Create(headers: headers, configureServices: services =>
        {
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
            headers: new Dictionary<string, string> { ["X-Inertia-Error-Bag"] = "loginForm" });

        var filter = new InertiaValidationFilter();
        await filter.OnResultExecutionAsync(context, () =>
            Task.FromResult(new ResultExecutedContext(
                context, new List<IFilterMetadata>(), context.Result, controller: null!)));

        tempDataFactory.TempData[SessionKeys.ErrorBag].Should().Be("loginForm");
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
}
