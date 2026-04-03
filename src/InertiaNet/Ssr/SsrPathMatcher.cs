using System.Text.RegularExpressions;

namespace InertiaNet.Ssr;

internal static partial class SsrPathMatcher
{
    public static bool IsExcluded(string path, IEnumerable<string> patterns)
    {
        var normalizedPath = NormalizePath(path);

        foreach (var pattern in patterns)
        {
            if (IsMatch(normalizedPath, pattern))
                return true;
        }

        return false;
    }

    public static bool IsMatch(string path, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        var normalizedPath = NormalizePath(path);
        var normalizedPattern = NormalizePath(pattern);

        if (normalizedPattern is "/*" or "/**")
            return true;

        if (!normalizedPattern.Contains('*'))
            return string.Equals(normalizedPath, normalizedPattern, StringComparison.OrdinalIgnoreCase);

        if (normalizedPattern.EndsWith("/*", StringComparison.Ordinal))
        {
            var prefix = normalizedPattern[..^2];
            return string.Equals(normalizedPath, prefix, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
        }

        var regexPattern = "^" + WildcardRegex().Replace(Regex.Escape(normalizedPattern), ".*") + "$";
        return Regex.IsMatch(normalizedPath, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        var normalized = path.Trim();

        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;

        if (normalized.Length > 1 && normalized.EndsWith('/'))
            normalized = normalized.TrimEnd('/');

        return normalized;
    }

    [GeneratedRegex("\\\\\\*", RegexOptions.CultureInvariant)]
    private static partial Regex WildcardRegex();
}
