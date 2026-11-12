using InertiaNet.Extensions;
using InertiaNet.Core;
using InertiaNet.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace InertiaNet.Middleware;

/// <summary>
/// Result filter that automatically serializes ModelState validation errors
/// to TempData when the action result is a redirect. This allows the Inertia
/// middleware to restore errors on the next request. Keys are normalized per path segment
/// according to <see cref="InertiaOptions.ValidationErrorKeyFormat"/>.
/// </summary>
public sealed class InertiaValidationFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.HttpContext.Request.IsInertia()
            && context.HttpContext.RequestServices.GetService<IOptions<InertiaOptions>>()?.Value.RequestPredicate?.Invoke(context.HttpContext) != false
            && !context.ModelState.IsValid && IsRedirectResult(context.Result))
        {
            var tempDataFactory = context.HttpContext.RequestServices
                .GetService<ITempDataDictionaryFactory>();
            if (tempDataFactory is not null)
            {
                var tempData = tempDataFactory.GetTempData(context.HttpContext);

                var errors = ValidationErrorKeys.From(context.HttpContext).Normalize(context.ModelState
                    .Where(kvp => kvp.Value is { Errors.Count: > 0 })
                    .Select(kvp => new KeyValuePair<string, string[]>(kvp.Key,
                        kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray())));
                tempData[SessionKeys.ValidationErrors] = JsonSerializer.Serialize(errors);

                var errorBag = context.HttpContext.Request.GetErrorBag();
                if (errorBag is not null)
                    tempData[SessionKeys.ErrorBag] = errorBag;

                tempData.Save();
            }
        }
        await next();
    }

    private static bool IsRedirectResult(IActionResult result) =>
        result is RedirectResult or RedirectToActionResult
            or RedirectToRouteResult or RedirectToPageResult
            or LocalRedirectResult;
}
