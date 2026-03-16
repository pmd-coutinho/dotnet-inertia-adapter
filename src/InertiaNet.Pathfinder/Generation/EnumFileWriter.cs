using System.Text;
using InertiaNet.Pathfinder.Analysis;

namespace InertiaNet.Pathfinder.Generation;

static class EnumFileWriter
{
    public static void Write(string outputDir, List<EnumInfo> enums)
    {
        if (enums.Count == 0)
            return;

        var enumsDir = Path.Combine(outputDir, "enums");
        Directory.CreateDirectory(enumsDir);

        foreach (var enumInfo in enums)
        {
            var sb = new StringBuilder();
            var name = enumInfo.ShortName;

            // Generate const object with all members
            sb.AppendLine($"export const {name} = {{");
            foreach (var member in enumInfo.Members)
            {
                var value = member.Value ?? member.Name;
                // If the value is numeric, don't quote it; otherwise quote it
                if (int.TryParse(value, out _))
                    sb.AppendLine($"    {member.Name}: {value},");
                else
                    sb.AppendLine($"    {member.Name}: \"{value}\",");
            }
            sb.AppendLine("} as const");
            sb.AppendLine();

            // Generate union type from the const object values
            sb.AppendLine($"export type {name} = (typeof {name})[keyof typeof {name}]");

            var filePath = Path.Combine(enumsDir, $"{name}.ts");
            File.WriteAllText(filePath, sb.ToString());
        }
    }
}
