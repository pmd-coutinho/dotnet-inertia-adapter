using InertiaNet.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Text.Json;

namespace InertiaNet.Middleware;

/// <summary>
/// Action filter that handles Precognition requests — validation-only requests
/// that return 204 (valid) or 422 (invalid) without executing the action body.
/// </summary>
public sealed class InertiaPrecognitionFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!PrecognitionValidation.IsPrecognitive(context.HttpContext.Request))
        {
            await next();
            return;
        }

        var errors = PrecognitionValidation.SelectErrors(context.HttpContext.Request,
            context.ModelState.Where(pair => pair.Value?.Errors.Count > 0)
                .Select(pair => new KeyValuePair<string, string[]>(pair.Key,
                    pair.Value!.Errors.Select(error => string.IsNullOrEmpty(error.ErrorMessage)
                        ? "The value is invalid." : error.ErrorMessage).ToArray())));
        PrecognitionValidation.SetHeaders(context.HttpContext.Response, errors.Count == 0);
        if (errors.Count == 0)
        {
            context.Result = new StatusCodeResult(204);
        }
        else
        {
            context.Result = new UnprocessableEntityObjectResult(PrecognitionValidation.ErrorPayload(errors));
        }
    }
}
