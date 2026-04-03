using InertiaNet.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace InertiaNet.Extensions;

/// <summary>
/// Extension methods on <see cref="HttpContext"/> for Inertia validation in Minimal APIs.
/// </summary>
public static class HttpContextExtensions
{
    /// <summary>
    /// Sets validation errors to be forwarded via TempData when the endpoint returns a redirect.
    /// Use with <see cref="InertiaValidationEndpointFilter"/> on Minimal API endpoints.
    /// </summary>
    public static void SetInertiaValidationErrors(
        this HttpContext context, Dictionary<string, string[]> errors)
        => SetInertiaValidationErrors(context, (IDictionary<string, string[]>)errors);

    /// <summary>
    /// Sets validation errors to be forwarded via TempData when the endpoint returns a redirect.
    /// Use with <see cref="InertiaValidationEndpointFilter"/> on Minimal API endpoints.
    /// </summary>
    public static void SetInertiaValidationErrors(
        this HttpContext context,
        IDictionary<string, string[]> errors,
        string? bag = null)
    {
        context.Items[InertiaValidationEndpointFilter.ValidationErrorsKey] = new Dictionary<string, string[]>(errors);

        if (!string.IsNullOrWhiteSpace(bag))
            context.SetInertiaErrorBag(bag);
    }

    /// <summary>
    /// Sets validation errors from <see cref="ValidationProblemDetails"/> so they can be forwarded via TempData on redirect.
    /// </summary>
    public static void SetInertiaValidationErrors(
        this HttpContext context,
        ValidationProblemDetails problemDetails,
        string? bag = null)
        => SetInertiaValidationErrors(context, problemDetails.Errors, bag);

    /// <summary>
    /// Sets validation errors from <see cref="ModelStateDictionary"/> so they can be forwarded via TempData on redirect.
    /// </summary>
    public static void SetInertiaValidationErrors(
        this HttpContext context,
        ModelStateDictionary modelState,
        string? bag = null)
    {
        var errors = modelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!.Errors
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "The input was not valid." : error.ErrorMessage)
                    .ToArray());

        SetInertiaValidationErrors(context, errors, bag);
    }

    /// <summary>
    /// Sets the error bag name for Inertia validation error forwarding.
    /// </summary>
    public static void SetInertiaErrorBag(this HttpContext context, string bag)
    {
        context.Items[InertiaValidationEndpointFilter.ErrorBagKey] = bag;
    }
}
