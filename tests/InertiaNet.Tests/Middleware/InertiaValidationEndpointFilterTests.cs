using FluentAssertions;
using InertiaNet.Extensions;
using InertiaNet.Core;
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
    [Fact]
    public async Task ExcludedRequest_DoesNotPersistExplicitErrors_AndStillRunsHandler()
    {
        var data = new FakeTempDataFactory();
        var context = HttpContextHelper.Create(configureServices: services =>
        {
            services.AddSingleton<ITempDataDictionaryFactory>(data);
            services.Configure<InertiaOptions>(options => options.RequestPredicate = _ => false);
        });
        var redirect = Results.Redirect("/form");
        var result = await new InertiaValidationEndpointFilter().InvokeAsync(new DefaultEndpointFilterInvocationContext(context), _ =>
        {
            context.SetInertiaValidationErrors(new Dictionary<string, string[]> { ["email"] = ["Required"] });
            return ValueTask.FromResult<object?>(redirect);
        });
        result.Should().BeSameAs(redirect);
        data.TempData.Should().BeEmpty();
    }

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
    public static TheoryData<string> ErrorSources => ["dictionary", "problem", "modelState"];

    [Theory]
    [MemberData(nameof(ErrorSources))]
    public async Task InvokeAsync_ShouldNormalizeKeysLikeTheMvcFilter(string source)
    {
        var tempDataFactory = new FakeTempDataFactory();
        var context = CreateContextWithTempData(tempDataFactory);
        var raw = new Dictionary<string, string[]>
        {
            ["Email"] = ["Email is required"],
            ["Address.Street"] = ["Street is required"],
            ["Values[3].Code"] = ["Code is invalid"],
            ["Prices[EUR]"] = ["Price is required"],
            ["name"] = ["Name is required"],
        };
        switch (source)
        {
            case "dictionary":
                context.SetInertiaValidationErrors(raw);
                break;
            case "problem":
                context.SetInertiaValidationErrors(new ValidationProblemDetails(raw));
                break;
            default:
                var modelState = new ModelStateDictionary();
                foreach (var (key, messages) in raw)
                    foreach (var message in messages) modelState.AddModelError(key, message);
                context.SetInertiaValidationErrors(modelState);
                break;
        }

        await new InertiaValidationEndpointFilter().InvokeAsync(new DefaultEndpointFilterInvocationContext(context),
            _ => ValueTask.FromResult<object?>(Results.Redirect("/create")));

        var errors = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            (string)tempDataFactory.TempData[SessionKeys.ValidationErrors]!)!;
        errors.Keys.Should().BeEquivalentTo("email", "address.street", "values[3].code", "prices[EUR]", "name");
        errors["values[3].code"].Should().Equal("Code is invalid");
    }

    [Theory]
    [InlineData(ValidationErrorKeyFormat.Dots, "values.3.code")]
    [InlineData(ValidationErrorKeyFormat.Preserve, "Values[3].Code")]
    public async Task InvokeAsync_ShouldHonorTheConfiguredKeyFormat(ValidationErrorKeyFormat format, string expected)
    {
        var tempDataFactory = new FakeTempDataFactory();
        var context = HttpContextHelper.Create("POST", "/submit", configureServices: services =>
        {
            services.AddSingleton<ITempDataDictionaryFactory>(tempDataFactory);
            services.Configure<InertiaOptions>(options => options.ValidationErrorKeyFormat = format);
        });
        context.SetInertiaValidationErrors(new Dictionary<string, string[]> { ["Values[3].Code"] = ["Invalid"] });

        await new InertiaValidationEndpointFilter().InvokeAsync(new DefaultEndpointFilterInvocationContext(context),
            _ => ValueTask.FromResult<object?>(Results.Redirect("/create")));

        var errors = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            (string)tempDataFactory.TempData[SessionKeys.ValidationErrors]!)!;
        errors.Keys.Should().Equal(expected);
    }

    [Fact]
    public async Task InvokeAsync_ShouldMergeKeysThatNormalizeToTheSamePath()
    {
        var tempDataFactory = new FakeTempDataFactory();
        var context = CreateContextWithTempData(tempDataFactory);
        context.SetInertiaValidationErrors(new Dictionary<string, string[]>
        {
            ["Email"] = ["Email is required"],
            ["email"] = ["Email is invalid"],
        });

        await new InertiaValidationEndpointFilter().InvokeAsync(new DefaultEndpointFilterInvocationContext(context),
            _ => ValueTask.FromResult<object?>(Results.Redirect("/create")));

        var errors = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            (string)tempDataFactory.TempData[SessionKeys.ValidationErrors]!)!;
        errors.Should().ContainSingle().Which.Value.Should().Equal("Email is required", "Email is invalid");
    }

    public static TheoryData<string, bool> ErrorSourcesWithCustomOptions => new()
    {
        { "dictionary", false }, { "problem", false }, { "modelState", false },
        { "dictionary", true }, { "problem", true }, { "modelState", true },
    };

    [Theory]
    [MemberData(nameof(ErrorSourcesWithCustomOptions))]
    public async Task InvokeAsync_ShouldNotRenameMembersWhenTheConfiguredPolicyIsNull(string source, bool customOptions)
    {
        var tempDataFactory = new FakeTempDataFactory();
        var context = HttpContextHelper.Create("POST", "/submit", configureServices: services =>
        {
            services.AddSingleton<ITempDataDictionaryFactory>(tempDataFactory);
            services.Configure<InertiaOptions>(options =>
            {
                options.ValidationErrorKeyFormat = ValidationErrorKeyFormat.Dots;
                if (customOptions) options.JsonSerializerOptions = new JsonSerializerOptions();
            });
        });
        var raw = new Dictionary<string, string[]>
        {
            ["Email"] = ["Email is required"],
            ["Address.Street"] = ["Street is required"],
            ["Values[3].Code"] = ["Code is invalid"],
        };
        switch (source)
        {
            case "dictionary":
                context.SetInertiaValidationErrors(raw);
                break;
            case "problem":
                context.SetInertiaValidationErrors(new ValidationProblemDetails(raw));
                break;
            default:
                var modelState = new ModelStateDictionary();
                foreach (var (key, messages) in raw)
                    foreach (var message in messages) modelState.AddModelError(key, message);
                context.SetInertiaValidationErrors(modelState);
                break;
        }

        await new InertiaValidationEndpointFilter().InvokeAsync(new DefaultEndpointFilterInvocationContext(context),
            _ => ValueTask.FromResult<object?>(Results.Redirect("/create")));

        var errors = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            (string)tempDataFactory.TempData[SessionKeys.ValidationErrors]!)!;
        errors.Keys.Should().BeEquivalentTo(customOptions
            ? new[] { "Email", "Address.Street", "Values.3.Code" }
            : new[] { "email", "address.street", "values.3.code" });
    }
}

