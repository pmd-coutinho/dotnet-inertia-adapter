using System.Text;
using InertiaNet.Pathfinder.Analysis;

namespace InertiaNet.Pathfinder.Generation;

static class EnumFileWriter
{
    public static void Write(string outputDir, List<EnumInfo> enums)
    {
        if (enums.Count == 0)
            return;

        var enumsDir = GeneratedOutput.PathUnder(outputDir, "enums");
        Directory.CreateDirectory(enumsDir);

        foreach (var enumInfo in enums
                     .OrderBy(enumInfo => enumInfo.ShortName, StringComparer.Ordinal)
                     .ThenBy(enumInfo => enumInfo.FullName, StringComparer.Ordinal))
        {
            var sb = new StringBuilder();
            var name = enumInfo.ShortName;

            // Generate const object with all members
            sb.AppendLine($"export const {name} = {{");
            foreach (var member in enumInfo.Members)
            {
                var value = member.Value ?? member.Name;
                var key = member.Name == "__proto__" ? "[\"__proto__\"]" : NameUtils.PropertyKey(member.Name);
                // If the value is numeric, don't quote it; otherwise quote it
                if (member.BigInt)
                    sb.AppendLine($"    {key}: {value}n,");
                else if (!enumInfo.StringValues && System.Numerics.BigInteger.TryParse(value, out _))
                    sb.AppendLine($"    {key}: {value},");
                else
                    sb.AppendLine($"    {key}: {System.Text.Json.JsonSerializer.Serialize(value)},");
            }
            sb.AppendLine("} as const");
            sb.AppendLine();

            // Generate union type from the const object values
            sb.AppendLine($"export type {name} = (typeof {name})[keyof typeof {name}]" + (enumInfo.WireType is null ? "" : " | " + enumInfo.WireType));

            var filePath = GeneratedOutput.PathUnder(enumsDir, $"{name}.ts");
            File.WriteAllText(filePath, sb.ToString());
        }
    }
}
