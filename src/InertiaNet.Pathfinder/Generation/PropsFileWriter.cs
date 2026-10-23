using System.Text;
using InertiaNet.Pathfinder.Analysis;

namespace InertiaNet.Pathfinder.Generation;

static class PropsFileWriter
{
    public static void Write(string outputDir, List<PagePropsInfo> pageProps, List<ModelInfo> models)
    {
        if (pageProps.Count == 0)
            return;

        var typesDir = GeneratedOutput.PathUnder(outputDir, "types");
        Directory.CreateDirectory(typesDir);

        // Build a set of known model names for import resolution
        var modelNames = models.Select(m => m.ShortName).ToHashSet();

        foreach (var page in pageProps.OrderBy(page => page.ComponentName, StringComparer.Ordinal))
        {
            var sb = new StringBuilder();
            var interfaceName = InterfaceName(page);

            // Collect model types referenced by this page's props
            var referencedModels = new HashSet<string>();
            foreach (var prop in page.Props)
                CollectModelReferences(prop.TypeScriptType, modelNames, referencedModels);

            // Write imports for referenced models
            foreach (var model in referencedModels.OrderBy(m => m))
            {
                sb.AppendLine($"import type {{ {model} }} from '../models/{model}'");
            }
            var enumReferences = page.Props.SelectMany(property => TypeReferenceCollector.Collect(property.TypeScriptType))
                .Where(TypeMapper.IsEnum).Distinct().OrderBy(name => name, StringComparer.Ordinal).ToArray();
            foreach (var reference in enumReferences)
                sb.AppendLine($"import type {{ {reference} }} from '../enums/{reference}'");

            if (referencedModels.Count > 0 || enumReferences.Length > 0)
                sb.AppendLine();

            sb.AppendLine($"export interface {interfaceName} {{");

            foreach (var prop in page.Props)
            {
                var optional = prop.IsOptional ? "?" : "";
                sb.AppendLine($"    {NameUtils.PropertyKey(prop.Name)}{optional}: {prop.TypeScriptType}");
            }
            // Page roots include request-wide shared fields and must satisfy
            // Inertia's PageProps constraint. A plain index signature also stays
            // consumable by Vue's runtime props compiler.
            sb.AppendLine("    [key: string]: unknown");

            sb.AppendLine("}");

            // Write to types/{ComponentName}.ts — flatten path separators
            var fileName = FileName(page) + ".ts";
            var filePath = GeneratedOutput.PathUnder(typesDir, fileName);
            File.WriteAllText(filePath, sb.ToString());
        }
    }

    internal static string InterfaceName(PagePropsInfo page) => page.TypeName ?? ContractNames.Identifier(SanitizeInterfaceName(page.ComponentName) + "Props");
    internal static string FileName(PagePropsInfo page) => page.FileName ?? page.ComponentName.Replace('/', '.').Replace('\\', '.');

    private static void CollectModelReferences(string tsType, HashSet<string> modelNames, HashSet<string> references)
    {
        foreach (var reference in TypeReferenceCollector.Collect(tsType))
        {
            if (modelNames.Contains(reference))
                references.Add(reference);
        }
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
