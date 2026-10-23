using System.Text;
using InertiaNet.Pathfinder.Analysis;

namespace InertiaNet.Pathfinder.Generation;

static class ModelFileWriter
{
    public static void Write(string outputDir, List<ModelInfo> models)
    {
        if (models.Count == 0)
            return;

        var modelsDir = GeneratedOutput.PathUnder(outputDir, "models");
        Directory.CreateDirectory(modelsDir);

        foreach (var model in models
                     .OrderBy(model => model.ShortName, StringComparer.Ordinal)
                     .ThenBy(model => model.FullName, StringComparer.Ordinal))
        {
            var sb = new StringBuilder();
            var referencedModels = model.Properties
                .SelectMany(property => TypeReferenceCollector.Collect(property.TypeScriptType))
                .Where(reference => reference != model.ShortName && models.Any(candidate => candidate.ShortName == reference))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(reference => reference, StringComparer.Ordinal)
                .ToList();

            foreach (var reference in referencedModels)
                sb.AppendLine($"import type {{ {reference} }} from './{reference}'");

            var enumReferences = model.Properties.SelectMany(property => TypeReferenceCollector.Collect(property.TypeScriptType))
                .Where(TypeMapper.IsEnum).Distinct().OrderBy(name => name, StringComparer.Ordinal).ToArray();
            foreach (var reference in enumReferences)
                sb.AppendLine($"import type {{ {reference} }} from '../enums/{reference}'");

            if (referencedModels.Count > 0 || enumReferences.Length > 0)
                sb.AppendLine();

            sb.AppendLine(model.Usage == "output" ? $"export interface {model.ShortName} {{" : $"export type {model.ShortName} = {{");

            foreach (var prop in model.Properties)
            {
                var optional = (prop.IsOptional ?? prop.IsNullable) ? "?" : "";
                sb.AppendLine($"    {NameUtils.PropertyKey(prop.Name)}{optional}: {prop.TypeScriptType}");
            }
            if (model.HasExtensionData) sb.AppendLine("    [key: string]: unknown");

            sb.AppendLine("}");
            var validation = model.Properties.Where(property => property.Validation is not null)
                .ToDictionary(property => property.Name, property => property.Validation);
            if (validation.Count > 0)
                sb.AppendLine($"\nexport const {model.ShortName}Validation = {System.Text.Json.JsonSerializer.Serialize(validation).Replace("\"__proto__\":", "[\"__proto__\"]:", StringComparison.Ordinal)} as const");

            var filePath = GeneratedOutput.PathUnder(modelsDir, $"{model.ShortName}.ts");
            File.WriteAllText(filePath, sb.ToString());
        }
    }
}
