using System.Text;
using System.Text.Json;
using InertiaNet.Pathfinder.Analysis;

namespace InertiaNet.Pathfinder.Generation;

internal static class PageMapFileWriter
{
    public static void Write(string root, List<PagePropsInfo> pages, List<PropField> shared, List<ModelInfo> models)
    {
        var directory = GeneratedOutput.PathUnder(root, "types");
        Directory.CreateDirectory(directory);
        var sb = new StringBuilder("import type {} from '@inertiajs/core'\n");
        var references = shared.SelectMany(field => TypeReferenceCollector.Collect(field.TypeScriptType)).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal);
        foreach (var reference in references)
        {
            if (models.Any(model => model.ShortName == reference)) sb.AppendLine($"import type {{ {reference} }} from '../models/{reference}'");
            else if (TypeMapper.IsEnum(reference)) sb.AppendLine($"import type {{ {reference} }} from '../enums/{reference}'");
        }
        sb.AppendLine("\nexport interface SharedProps {");
        foreach (var field in shared) sb.AppendLine($"    {NameUtils.PropertyKey(field.Name)}{(field.IsOptional ? "?" : "")}: {field.TypeScriptType}");
        sb.AppendLine("}\n\ndeclare module '@inertiajs/core' {\n    interface PageProps extends SharedProps {}\n}");
        File.WriteAllText(GeneratedOutput.PathUnder(directory, "shared.ts"), sb.ToString());
        sb.Clear();
        sb.AppendLine("import type { SharedProps } from './shared'");
        foreach (var page in pages)
            sb.AppendLine($"import type {{ {PropsFileWriter.InterfaceName(page)} }} from {JsonSerializer.Serialize("./" + PropsFileWriter.FileName(page))}");
        sb.AppendLine("\nexport interface PagePropsMap {");
        foreach (var page in pages) sb.AppendLine($"    {JsonSerializer.Serialize(page.ComponentName)}: {PropsFileWriter.InterfaceName(page)} & SharedProps");
        sb.AppendLine("}\n\nexport type ComponentName = keyof PagePropsMap\nexport type PropsFor<T extends ComponentName> = PagePropsMap[T]");
        File.WriteAllText(GeneratedOutput.PathUnder(directory, "page-map.ts"), sb.ToString());
    }
}
