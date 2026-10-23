using Microsoft.AspNetCore.Http;

namespace InertiaNet.Support;

internal static class InertiaResponseHeaders
{
    public static void AppendVary(HttpResponse response, string value)
    {
        var existing = response.Headers.Vary.ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (existing.Contains("*")) return;
        response.Headers.Vary = string.Join(", ", existing.Append(value).Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
