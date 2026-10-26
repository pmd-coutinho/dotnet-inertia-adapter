using System.ComponentModel.DataAnnotations;
using System.Reflection;
using InertiaNet.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InertiaNet.Middleware;

/// <summary>Validates bound Minimal API arguments without executing the endpoint.</summary>
public sealed class InertiaPrecognitionEndpointFilter(MethodInfo handler) : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!PrecognitionValidation.IsPrecognitive(context.HttpContext.Request)
            || context.HttpContext.RequestServices.GetService<IOptions<InertiaOptions>>()?.Value.RequestPredicate?.Invoke(context.HttpContext) == false)
            return await next(context);

        var errors = new Dictionary<string, List<string>>();
        var parameters = handler.GetParameters();
        var services = context.HttpContext.RequestServices;
        var registered = services.GetService<IServiceProviderIsService>();
        for (var i = 0; i < parameters.Length && i < context.Arguments.Count; i++)
        {
            var parameter = parameters[i];
            if (parameter.ParameterType == typeof(HttpContext) || parameter.ParameterType == typeof(CancellationToken)
                || parameter.ParameterType == typeof(System.Security.Claims.ClaimsPrincipal)
                || parameter.IsDefined(typeof(FromServicesAttribute))
                || (!parameter.IsDefined(typeof(FromBodyAttribute)) && !parameter.IsDefined(typeof(FromFormAttribute))
                    && registered?.IsService(parameter.ParameterType) == true)) continue;

            var value = context.Arguments[i];
            var results = new List<ValidationResult>();
            Validator.TryValidateValue(value, new ValidationContext(value ?? new object(), services, null)
                { MemberName = parameter.Name }, results, parameter.GetCustomAttributes<ValidationAttribute>());
            PrecognitionValidation.AddResults(results, "", errors);
            PrecognitionValidation.ValidateObject(value, "", errors, services, new(ReferenceEqualityComparer.Instance));
        }
        var selected = PrecognitionValidation.SelectErrors(context.HttpContext.Request,
            errors.Select(pair => new KeyValuePair<string, string[]>(pair.Key, pair.Value.ToArray())));
        PrecognitionValidation.SetHeaders(context.HttpContext.Response, selected.Count == 0);
        return selected.Count == 0 ? Results.NoContent()
            : Results.Json(PrecognitionValidation.ErrorPayload(selected), statusCode: StatusCodes.Status422UnprocessableEntity);
    }
}
