using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using InertiaNet.Support;
using Microsoft.AspNetCore.Http;

namespace InertiaNet.Middleware;

internal static partial class PrecognitionValidation
{
    public static bool IsPrecognitive(HttpRequest request)
        => request.Headers[HeaderNames.Precognition].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

    public static Dictionary<string, string[]> SelectErrors(HttpRequest request, IEnumerable<KeyValuePair<string, string[]>> errors)
    {
        var only = request.Headers[HeaderNames.PrecognitionValidateOnly].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ClientKey).ToArray();
        return errors.GroupBy(pair => ClientKey(pair.Key)).Where(group => only.Length == 0
            || only.Any(field => group.Key == field || group.Key.StartsWith(field + ".", StringComparison.Ordinal)
                || field.StartsWith(group.Key + ".", StringComparison.Ordinal)))
            .ToDictionary(group => group.Key, group => group.SelectMany(pair => pair.Value).ToArray());
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

    public static string ClientKey(string key)
        => string.Join('.', ArrayIndex().Replace(key, ".$1").Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));

    [GeneratedRegex(@"\[(\d+)\]")]
    private static partial Regex ArrayIndex();

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
                var key = ClientKey(Join(prefix, member));
                if (!errors.TryGetValue(key, out var messages)) errors[key] = messages = [];
                messages.Add(result.ErrorMessage ?? "The value is invalid.");
            }
    }

    private static string Join(string prefix, string member)
        => string.IsNullOrEmpty(prefix) ? member : string.IsNullOrEmpty(member) ? prefix : $"{prefix}.{member}";
}
