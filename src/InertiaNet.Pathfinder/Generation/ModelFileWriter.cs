using System.Text;
using InertiaNet.Pathfinder.Analysis;

namespace InertiaNet.Pathfinder.Generation;

static class ModelFileWriter
{
    public static void Write(string outputDir, List<ModelInfo> models)
    {
        if (models.Count == 0)
            return;

        var modelsDir = Path.Combine(outputDir, "models");
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

            if (referencedModels.Count > 0)
                sb.AppendLine();

            sb.AppendLine($"export interface {model.ShortName} {{");

            foreach (var prop in model.Properties)
            {
                var optional = prop.IsNullable ? "?" : "";
                sb.AppendLine($"    {prop.Name}{optional}: {prop.TypeScriptType}");
            }

            sb.AppendLine("}");

            var filePath = Path.Combine(modelsDir, $"{model.ShortName}.ts");
            File.WriteAllText(filePath, sb.ToString());
        }
    }
}
