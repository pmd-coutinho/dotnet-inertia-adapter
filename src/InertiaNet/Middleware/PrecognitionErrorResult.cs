using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace InertiaNet.Middleware;

/// <summary>
/// The 422 response for an invalid Precognition request: <c>{ "message": ..., "errors": { key: [messages] } }</c>,
/// the body Laravel's <c>Handler::invalidJson</c> sends and the Precognition client reads <c>errors</c> from.
/// </summary>
/// <remarks>
/// The JSON is written by hand rather than through the application's MVC or Minimal API serializer options,
/// so a <c>PropertyNamingPolicy</c> cannot rename the envelope (<c>Message</c>/<c>Errors</c>) and a
/// <c>DictionaryKeyPolicy</c> cannot rename the error keys, which <see cref="PrecognitionValidation.SelectErrors"/>
/// has already normalized with InertiaNet's own naming.
/// </remarks>
internal sealed class PrecognitionErrorResult(IReadOnlyDictionary<string, string[]> errors) : IActionResult, IResult
{
    public const string Message = "The given data was invalid.";

    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public Task ExecuteResultAsync(ActionContext context) => ExecuteAsync(context.HttpContext);

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        response.ContentType = "application/json; charset=utf-8";

        // Utf8JsonWriter buffers until FlushAsync, so nothing is written to the body synchronously.
        await using var writer = new Utf8JsonWriter(response.Body);
        writer.WriteStartObject();
        writer.WriteString("message", Message);
        writer.WriteStartObject("errors");
        foreach (var (key, messages) in Errors)
        {
            writer.WriteStartArray(key);
            foreach (var message in messages)
                writer.WriteStringValue(message);
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
        await writer.FlushAsync(httpContext.RequestAborted);
    }
}
