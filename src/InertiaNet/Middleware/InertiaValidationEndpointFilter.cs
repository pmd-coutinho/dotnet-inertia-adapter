using InertiaNet.Extensions;
using InertiaNet.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace InertiaNet.Middleware;

/// <summary>
/// Minimal API endpoint filter that serializes validation errors to TempData
/// when the endpoint returns a redirect. This is the Minimal API equivalent of
/// <see cref="InertiaValidationFilter"/> (which only works with MVC controllers).
/// <para>
/// Usage:
/// <code>
/// app.MapPost("/users", handler)
///    .WithInertiaValidation();
/// </code>
/// </para>
/// <para>
/// Set validation errors in the endpoint via
/// <c>httpContext.SetInertiaValidationErrors(errors)</c> before returning a redirect.
/// </para>
/// </summary>
public sealed class InertiaValidationEndpointFilter : IEndpointFilter
{
    internal const string ValidationErrorsKey = "InertiaNet.ValidationErrors";
    internal const string ErrorBagKey = "InertiaNet.ErrorBag";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);

        if (result is IResult httpResult
            && context.HttpContext.Items.TryGetValue(ValidationErrorsKey, out var errorsObj)
            && errorsObj is Dictionary<string, string[]> errors
            && errors.Count > 0
            && IsRedirectResult(httpResult))
        {
            var tempDataFactory = context.HttpContext.RequestServices
                .GetService<ITempDataDictionaryFactory>();

            if (tempDataFactory is not null)
            {
                var tempData = tempDataFactory.GetTempData(context.HttpContext);
                tempData[SessionKeys.ValidationErrors] = JsonSerializer.Serialize(errors);

                if (context.HttpContext.Items.TryGetValue(ErrorBagKey, out var bagObj) && bagObj is string bag)
                    tempData[SessionKeys.ErrorBag] = bag;

                tempData.Save();
            }
        }

        return result;
    }

    private static bool IsRedirectResult(IResult result)
    {
        // IStatusCodeHttpResult covers some result types
        if (result is IStatusCodeHttpResult statusCodeResult)
            return statusCodeResult.StatusCode is >= 300 and < 400;

        // RedirectHttpResult doesn't implement IStatusCodeHttpResult — check by type name
        var typeName = result.GetType().Name;
        return typeName is "RedirectHttpResult" or "RedirectToRouteHttpResult";
    }
}
