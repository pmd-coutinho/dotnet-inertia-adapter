using FluentAssertions;
using InertiaNet.Extensions;
using InertiaNet.Middleware;
using InertiaNet.Support;
using InertiaNet.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace InertiaNet.Tests.Middleware;

public class InertiaValidationEndpointFilterTests
{
    private static DefaultHttpContext CreateContextWithTempData(FakeTempDataFactory tempDataFactory)
    {
        return HttpContextHelper.Create("POST", "/submit", configureServices: services =>
        {
            services.AddSingleton<ITempDataDictionaryFactory>(tempDataFactory);
        });
    }

    [Fact]
    public async Task InvokeAsync_ShouldSerializeErrorsToTempData_WhenValidationErrorsSetAndRedirect()
    {
        var tempDataFactory = new FakeTempDataFactory();
        var context = CreateContextWithTempData(tempDataFactory);
        context.SetInertiaValidationErrors(new Dictionary<string, string[]>
        {
            ["email"] = ["Email is required"]
        });

        var filter = new InertiaValidationEndpointFilter();
        var invocationContext = new DefaultEndpointFilterInvocationContext(context);
        var result = await filter.InvokeAsync(invocationContext,
            _ => ValueTask.FromResult<object?>(Results.Redirect("/create")));

        tempDataFactory.TempData[SessionKeys.ValidationErrors].Should().NotBeNull();
        var errorsJson = (string)tempDataFactory.TempData[SessionKeys.ValidationErrors]!;
        var errors = JsonSerializer.Deserialize<Dictionary<string, string[]>>(errorsJson);
        errors.Should().ContainKey("email");
    }

    [Fact]
    public async Task InvokeAsync_ShouldNotTouchTempData_WhenNoValidationErrors()
    {
        var tempDataFactory = new FakeTempDataFactory();
        var context = CreateContextWithTempData(tempDataFactory);

        var filter = new InertiaValidationEndpointFilter();
        var invocationContext = new DefaultEndpointFilterInvocationContext(context);
        await filter.InvokeAsync(invocationContext,
            _ => ValueTask.FromResult<object?>(Results.Redirect("/home")));

        tempDataFactory.TempData.Count.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_ShouldNotTouchTempData_WhenValidationErrorsSetButNotRedirect()
    {
        var tempDataFactory = new FakeTempDataFactory();
        var context = CreateContextWithTempData(tempDataFactory);
        context.SetInertiaValidationErrors(new Dictionary<string, string[]>
        {
            ["email"] = ["Email is required"]
        });

        var filter = new InertiaValidationEndpointFilter();
        var invocationContext = new DefaultEndpointFilterInvocationContext(context);
        await filter.InvokeAsync(invocationContext,
            _ => ValueTask.FromResult<object?>(Results.Ok(new { success = true })));

        tempDataFactory.TempData.Count.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_ShouldSerializeValidationProblemDetails_WithErrorBag()
    {
        var tempDataFactory = new FakeTempDataFactory();
        var context = CreateContextWithTempData(tempDataFactory);
        context.SetInertiaValidationErrors(
            new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["email"] = ["Email is required"],
            }),
            bag: "createUser");

        var filter = new InertiaValidationEndpointFilter();
        var invocationContext = new DefaultEndpointFilterInvocationContext(context);
        await filter.InvokeAsync(invocationContext,
            _ => ValueTask.FromResult<object?>(Results.Redirect("/create")));

        tempDataFactory.TempData[SessionKeys.ErrorBag].Should().Be("createUser");
        var errorsJson = (string)tempDataFactory.TempData[SessionKeys.ValidationErrors]!;
        var errors = JsonSerializer.Deserialize<Dictionary<string, string[]>>(errorsJson);
        errors.Should().ContainKey("email");
    }

    [Fact]
    public async Task InvokeAsync_ShouldSerializeModelStateErrors()
    {
        var tempDataFactory = new FakeTempDataFactory();
        var context = CreateContextWithTempData(tempDataFactory);
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("email", "Email is required");
        modelState.AddModelError("password", string.Empty);

        context.SetInertiaValidationErrors(modelState);

        var filter = new InertiaValidationEndpointFilter();
        var invocationContext = new DefaultEndpointFilterInvocationContext(context);
        await filter.InvokeAsync(invocationContext,
            _ => ValueTask.FromResult<object?>(Results.Redirect("/create")));

        var errorsJson = (string)tempDataFactory.TempData[SessionKeys.ValidationErrors]!;
        var errors = JsonSerializer.Deserialize<Dictionary<string, string[]>>(errorsJson)!;
        errors["email"].Should().ContainSingle().Which.Should().Be("Email is required");
        errors["password"].Should().ContainSingle().Which.Should().Be("The input was not valid.");
    }
}
