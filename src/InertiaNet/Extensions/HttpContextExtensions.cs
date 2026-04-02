using InertiaNet.Middleware;
using Microsoft.AspNetCore.Http;

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
    {
        context.Items[InertiaValidationEndpointFilter.ValidationErrorsKey] = errors;
    }

    /// <summary>
    /// Sets the error bag name for Inertia validation error forwarding.
    /// </summary>
    public static void SetInertiaErrorBag(this HttpContext context, string bag)
    {
        context.Items[InertiaValidationEndpointFilter.ErrorBagKey] = bag;
    }
}
