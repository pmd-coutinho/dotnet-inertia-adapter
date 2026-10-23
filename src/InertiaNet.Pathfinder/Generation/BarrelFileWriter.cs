using System.Text;

namespace InertiaNet.Pathfinder.Generation;

static class BarrelFileWriter
{
    // Directories that export named types (interfaces/types) rather than default exports
    private static readonly HashSet<string> NamedExportDirectories = new() { "types", "models", "enums" };

    public static void WriteAll(string outputDir)
    {
        // Write barrel files for actions/, routes/, enums/, models/, types/ subdirectories
        WriteBarrelForDirectory(Path.Combine(outputDir, "actions"), useNamedExports: false);
        WriteBarrelForDirectory(Path.Combine(outputDir, "routes"), useNamedExports: false);
        WriteBarrelForDirectory(Path.Combine(outputDir, "enums"), useNamedExports: true);
        WriteBarrelForDirectory(Path.Combine(outputDir, "models"), useNamedExports: true);
        WriteBarrelForDirectory(Path.Combine(outputDir, "types"), useNamedExports: true);
    }

    private static void WriteBarrelForDirectory(string directory, bool useNamedExports)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var subDir in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            WriteBarrelFile(subDir, useNamedExports);
        }

        WriteBarrelFile(directory, useNamedExports);
    }

    private static void WriteBarrelFile(string directory, bool useNamedExports)
    {
        var indexPath = Path.Combine(directory, "index.ts");
        var existing = File.Exists(indexPath) ? File.ReadAllText(indexPath) : "";
        var usedNames = System.Text.RegularExpressions.Regex.Matches(existing,
                @"\bexport\s+(?:const|let|function|type|interface|class|enum)\s+([\w$]+)")
            .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var exports = new StringBuilder();
        foreach (var file in Directory.GetFiles(directory, "*.ts")
                     .Where(file => Path.GetFileName(file) != "index.ts").OrderBy(file => file, StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (useNamedExports) exports.AppendLine($"export * from './{name}'");
            else
            {
                var alias = NameUtils.Unique(NameUtils.SafeJsName(name), usedNames);
                exports.AppendLine($"export {{ default as {alias} }} from './{name}'");
            }
        }
        foreach (var subDir in Directory.GetDirectories(directory).OrderBy(path => path, StringComparer.Ordinal))
        {
            if (!File.Exists(Path.Combine(subDir, "index.ts"))) continue;
            var name = Path.GetFileName(subDir);
            var alias = NameUtils.Unique(NameUtils.SafeJsName(name), usedNames);
            // An adjacent foo.ts must not shadow the foo/index.ts namespace.
            exports.AppendLine($"export * as {alias} from './{name}/index'");
        }
        if (exports.Length > 0) File.WriteAllText(indexPath, existing + exports);
    }
}
