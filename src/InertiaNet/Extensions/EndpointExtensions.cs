using InertiaNet.Middleware;
using InertiaNet.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace InertiaNet.Extensions;

/// <summary>
/// Extension methods for adding Inertia validation to Minimal API endpoints.
/// </summary>
public static class EndpointExtensions
{
    /// <summary>Declares a component for optional endpoint-manifest validation.</summary>
    public static RouteHandlerBuilder WithInertiaComponent(this RouteHandlerBuilder builder, string component)
        => builder.WithMetadata(new InertiaComponentMetadata(component));

    /// <summary>Handles validation-only requests using DataAnnotations without running the handler.</summary>
    public static RouteHandlerBuilder WithInertiaPrecognition(this RouteHandlerBuilder builder)
    {
        builder.AddEndpointFilterFactory((context, next) =>
        {
            var filter = new InertiaPrecognitionEndpointFilter(context.MethodInfo);
            return invocation => filter.InvokeAsync(invocation, next);
        });
        return builder;
    }

    /// <summary>Handles validation-only requests for every endpoint in this route group.</summary>
    public static RouteGroupBuilder WithInertiaPrecognition(this RouteGroupBuilder builder)
    {
        builder.AddEndpointFilterFactory((context, next) =>
        {
            var filter = new InertiaPrecognitionEndpointFilter(context.MethodInfo);
            return invocation => filter.InvokeAsync(invocation, next);
        });
        return builder;
    }

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
