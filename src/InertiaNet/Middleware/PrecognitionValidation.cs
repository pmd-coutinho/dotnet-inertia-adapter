using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using InertiaNet.Core;
using InertiaNet.Support;
using Microsoft.AspNetCore.Http;

namespace InertiaNet.Middleware;

internal static class PrecognitionValidation
{
    public static bool IsPrecognitive(HttpRequest request)
        => request.Headers[HeaderNames.Precognition].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Normalizes error keys to dot paths (the form sent in <c>Precognition-Validate-Only</c>) and keeps
    /// the errors for the requested fields, their parents and their children.
    /// </summary>
    public static Dictionary<string, string[]> SelectErrors(HttpRequest request, IEnumerable<KeyValuePair<string, string[]>> errors)
    {
        var keys = ValidationErrorKeys.From(request.HttpContext, ValidationErrorKeyFormat.Dots);
        var only = request.Headers[HeaderNames.PrecognitionValidateOnly].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(keys.Normalize).ToArray();
        return keys.Normalize(errors).Where(pair => only.Length == 0
            || only.Any(field => pair.Key == field || pair.Key.StartsWith(field + ".", StringComparison.Ordinal)
                || field.StartsWith(pair.Key + ".", StringComparison.Ordinal)))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    public static void SetHeaders(HttpResponse response, bool success)
    {
        InertiaResponseHeaders.AppendVary(response, HeaderNames.Precognition);
        response.Headers[HeaderNames.Precognition] = "true";
        if (success) response.Headers[HeaderNames.PrecognitionSuccess] = "true";
        else response.Headers.Remove(HeaderNames.PrecognitionSuccess);
    }

    public static object ErrorPayload(Dictionary<string, string[]> errors)
        => new { message = "The given data was invalid.", errors };

    public static void ValidateObject(object? value, string prefix, Dictionary<string, List<string>> errors,
        IServiceProvider services, HashSet<object> ancestors, int depth = 0)
    {
        if (value is null || value is string || value.GetType().IsValueType || depth > 32) return;
        if (!ancestors.Add(value)) return;
        try
        {
            if (value is IEnumerable sequence)
            {
                var index = 0;
                foreach (var item in sequence)
                    ValidateObject(item, Join(prefix, (index++).ToString(System.Globalization.CultureInfo.InvariantCulture)), errors, services, ancestors, depth + 1);
                return;
            }
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(value, new ValidationContext(value, services, null), results, validateAllProperties: true);
            AddResults(results, prefix, errors);
            foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (property.CanRead && property.GetIndexParameters().Length == 0)
                    ValidateObject(property.GetValue(value), Join(prefix, property.Name), errors, services, ancestors, depth + 1);
        }
        finally { ancestors.Remove(value); }
    }

    public static void AddResults(IEnumerable<ValidationResult> results, string prefix, Dictionary<string, List<string>> errors)
    {
        foreach (var result in results)
            foreach (var member in result.MemberNames.DefaultIfEmpty(string.Empty))
            {
                // Keys stay in CLR form here; SelectErrors normalizes them with the configured naming.
                var key = Join(prefix, member);
                if (!errors.TryGetValue(key, out var messages)) errors[key] = messages = [];
                messages.Add(result.ErrorMessage ?? "The value is invalid.");
            }
    }

    private static string Join(string prefix, string member)
        => string.IsNullOrEmpty(prefix) ? member : string.IsNullOrEmpty(member) ? prefix : $"{prefix}.{member}";
}
