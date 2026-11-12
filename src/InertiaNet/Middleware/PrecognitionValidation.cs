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
    /// the errors for the requested fields, their parents and their children. Fields are compared with
    /// <see cref="ValidationErrorKeys.MatchSegments"/> on both sides, so a field sent back exactly as this
    /// method returned it (or in its ASP.NET bracket form) always selects its errors.
    /// </summary>
    public static Dictionary<string, string[]> SelectErrors(HttpRequest request, IEnumerable<KeyValuePair<string, string[]>> errors)
    {
        var keys = ValidationErrorKeys.From(request.HttpContext, ValidationErrorKeyFormat.Dots);
        var only = request.Headers[HeaderNames.PrecognitionValidateOnly].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(keys.MatchSegments).ToArray();
        return keys.Normalize(errors).Where(pair =>
            {
                if (only.Length == 0) return true;
                var key = keys.MatchSegments(pair.Key);
                return only.Any(field => StartsWith(key, field) || StartsWith(field, key));
            })
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    // True when the path equals the prefix or is nested under it, segment by segment.
    private static bool StartsWith(string[] path, string[] prefix)
    {
        if (prefix.Length > path.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
            if (!string.Equals(path[i], prefix[i], StringComparison.OrdinalIgnoreCase)) return false;
        return true;
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
