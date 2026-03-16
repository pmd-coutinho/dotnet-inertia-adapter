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
        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderNames.Precognition, out var precog)
            || !precog.ToString().Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        HeaderDictionaryExtensions.Append(context.HttpContext.Response.Headers, "Vary", HeaderNames.Precognition);
        context.HttpContext.Response.Headers[HeaderNames.PrecognitionSuccess] = "true";

        if (context.ModelState.IsValid)
        {
            context.Result = new StatusCodeResult(204);
        }
        else
        {
            var errors = new Dictionary<string, string[]>();
            foreach (var kvp in context.ModelState)
            {
                if (kvp.Value.Errors.Count > 0)
                {
                    var key = JsonNamingPolicy.CamelCase.ConvertName(kvp.Key);
                    errors[key] = kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray();
                }
            }
            context.Result = new UnprocessableEntityObjectResult(errors);
        }
    }
}
