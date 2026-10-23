using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace InertiaNet.Pathfinder.Analysis;

internal static class ContractNames
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
        { "index", "Array", "Record", "File", "Blob", "Promise", "PagePropsMap", "SharedProps", "PropsFor", "ComponentName", "page-map", "shared",
          "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else", "enum", "export",
          "extends", "false", "finally", "for", "function", "if", "import", "in", "instanceof", "new", "null", "return", "super", "switch",
          "this", "throw", "true", "try", "typeof", "var", "void", "while", "with", "interface", "type", "namespace", "string", "number", "bigint", "boolean", "unknown", "any", "never" };

    public static string Identity(ITypeSymbol type)
        => type.ContainingAssembly?.Identity.Name + ":" + FullName(type);

    public static string FullName(ITypeSymbol type)
        => type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));

    public static string Identifier(string value)
    {
        var result = new StringBuilder();
        foreach (var character in value)
            result.Append(char.IsLetterOrDigit(character) || character is '_' or '$' ? character : '_');
        if (result.Length == 0 || char.IsDigit(result[0])) result.Insert(0, '_');
        return result.ToString();
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8].ToLowerInvariant();

    public static Dictionary<string, string> Assign(IEnumerable<(string Identity, string Preferred)> entries)
    {
        var items = entries.DistinctBy(entry => entry.Identity).OrderBy(entry => entry.Identity, StringComparer.Ordinal).ToArray();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in items.GroupBy(item => Identifier(item.Preferred), StringComparer.OrdinalIgnoreCase))
            foreach (var item in group)
            {
                var preferred = Identifier(item.Preferred);
                var candidate = group.Count() > 1 || Reserved.Contains(preferred) ? preferred + "_" + Hash(item.Identity) : preferred;
                var suffix = 0;
                // Request DTOs export a companion validation constant in the same barrel.
                while (used.Contains(candidate) || used.Contains(candidate + "Validation"))
                    candidate = preferred + "_" + Hash(item.Identity) + "_" + ++suffix;
                used.Add(candidate);
                used.Add(candidate + "Validation");
                result[item.Identity] = candidate;
            }
        return result;
    }
}
