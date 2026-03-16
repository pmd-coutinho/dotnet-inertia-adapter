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

        foreach (var model in models)
        {
            var sb = new StringBuilder();
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
