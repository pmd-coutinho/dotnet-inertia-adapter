using InertiaNet.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace InertiaNet.Extensions;

/// <summary>
/// Extension methods for adding Inertia validation to Minimal API endpoints.
/// </summary>
public static class EndpointExtensions
{
    /// <summary>
    /// Adds Inertia validation error forwarding to this endpoint or route group.
    /// Validation errors set via <c>httpContext.SetInertiaValidationErrors()</c>
    /// will be serialized to TempData when the endpoint returns a redirect.
    /// </summary>
    public static RouteHandlerBuilder WithInertiaValidation(this RouteHandlerBuilder builder)
    {
        builder.AddEndpointFilter<InertiaValidationEndpointFilter>();
        return builder;
    }

    /// <summary>
    /// Adds Inertia validation error forwarding to all endpoints in this route group.
    /// </summary>
    public static RouteGroupBuilder WithInertiaValidation(this RouteGroupBuilder builder)
    {
        builder.AddEndpointFilter<InertiaValidationEndpointFilter>();
        return builder;
    }
}
