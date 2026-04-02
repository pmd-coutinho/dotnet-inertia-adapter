using FluentAssertions;
using InertiaNet.Middleware;
using InertiaNet.Support;
using InertiaNet.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

namespace InertiaNet.Tests.Middleware;

public class InertiaPrecognitionFilterTests
{
    private static ActionExecutingContext CreateActionContext(
        ModelStateDictionary? modelState = null,
        Dictionary<string, string>? headers = null)
    {
        var httpContext = HttpContextHelper.Create("POST", "/submit", headers);
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

        context.Result.Should().BeOfType<UnprocessableEntityObjectResult>();
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
}
