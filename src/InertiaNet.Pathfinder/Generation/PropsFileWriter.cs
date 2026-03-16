using System.Text;
using InertiaNet.Pathfinder.Analysis;

namespace InertiaNet.Pathfinder.Generation;

static class PropsFileWriter
{
    private static readonly HashSet<string> BuiltInTsTypes = new()
    {
        "string", "number", "boolean", "unknown", "null", "undefined", "any", "void", "never", "object"
    };

    public static void Write(string outputDir, List<PagePropsInfo> pageProps, List<ModelInfo> models)
    {
        if (pageProps.Count == 0)
            return;

        var typesDir = Path.Combine(outputDir, "types");
        Directory.CreateDirectory(typesDir);

        // Build a set of known model names for import resolution
        var modelNames = models.Select(m => m.ShortName).ToHashSet();

        foreach (var page in pageProps)
        {
            var sb = new StringBuilder();
            var interfaceName = SanitizeInterfaceName(page.ComponentName) + "Props";

            // Collect model types referenced by this page's props
            var referencedModels = new HashSet<string>();
            foreach (var prop in page.Props)
                CollectModelReferences(prop.TypeScriptType, modelNames, referencedModels);

            // Write imports for referenced models
            foreach (var model in referencedModels.OrderBy(m => m))
            {
                sb.AppendLine($"import type {{ {model} }} from '../models/{model}'");
            }

            if (referencedModels.Count > 0)
                sb.AppendLine();

            sb.AppendLine($"export interface {interfaceName} {{");

            foreach (var prop in page.Props)
            {
                var optional = prop.IsOptional ? "?" : "";
                sb.AppendLine($"    {prop.Name}{optional}: {prop.TypeScriptType}");
            }

            sb.AppendLine("}");

            // Write to types/{ComponentName}.ts — flatten path separators
            var fileName = page.ComponentName.Replace('/', '.').Replace('\\', '.') + ".ts";
            var filePath = Path.Combine(typesDir, fileName);
            File.WriteAllText(filePath, sb.ToString());
        }
    }

    private static void CollectModelReferences(string tsType, HashSet<string> modelNames, HashSet<string> references)
    {
        // Strip array suffix, nullable, etc.
        var cleaned = tsType
            .Replace("[]", "")
            .Replace(" | null", "")
            .Replace(" | undefined", "");

        if (BuiltInTsTypes.Contains(cleaned)) return;

        // Handle Record<K, V> and other generics
        if (cleaned.Contains('<'))
        {
            var inner = cleaned[(cleaned.IndexOf('<') + 1)..cleaned.LastIndexOf('>')];
            foreach (var part in inner.Split(','))
                CollectModelReferences(part.Trim(), modelNames, references);
            return;
        }

        // Handle inline object types
        if (cleaned.StartsWith('{')) return;

        if (modelNames.Contains(cleaned))
            references.Add(cleaned);
    }

    private static string SanitizeInterfaceName(string componentName)
    {
        // "Pages/Dashboard" → "PagesDashboard"
        // "Posts/Index" → "PostsIndex"
        var parts = componentName.Split('/', '\\', '.');
        return string.Join("", parts.Select(p =>
            p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..] : p));
    }
}
