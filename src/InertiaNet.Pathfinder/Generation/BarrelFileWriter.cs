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
        var sb = new StringBuilder();

        // Re-export .ts files in this directory (excluding index.ts)
        foreach (var file in Directory.GetFiles(directory, "*.ts")
                     .Where(f => Path.GetFileName(f) != "index.ts")
                     .OrderBy(f => f))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var exportName = NameUtils.SafeJsName(name);

            if (useNamedExports)
            {
                // For types/models/enums: re-export all named exports
                sb.AppendLine($"export * from './{name}'");
            }
            else
            {
                sb.AppendLine($"export {{ default as {exportName} }} from './{name}'");
            }
        }

        // Re-export subdirectories that have an index.ts
        foreach (var subDir in Directory.GetDirectories(directory).OrderBy(d => d))
        {
            var indexFile = Path.Combine(subDir, "index.ts");
            if (File.Exists(indexFile) || Directory.GetFiles(subDir, "*.ts").Length > 0)
            {
                var dirName = Path.GetFileName(subDir);
                var exportName = NameUtils.SafeJsName(dirName);
                sb.AppendLine($"export * as {exportName} from './{dirName}'");
            }
        }

        if (sb.Length > 0)
        {
            var indexPath = Path.Combine(directory, "index.ts");

            // If index.ts already exists (e.g. written by RouteFileWriter), only append
            // barrel exports that don't conflict with existing named exports
            if (File.Exists(indexPath))
            {
                var existing = File.ReadAllText(indexPath);

                // Filter out conflicting re-exports
                var filteredLines = new StringBuilder();
                foreach (var line in sb.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    // Extract the export name from patterns like:
                    //   export * as posts from './posts'
                    //   export { default as posts } from './posts'
                    var exportName = ExtractExportName(line);
                    if (exportName != null && existing.Contains($" {exportName}"))
                        continue; // Skip — would conflict with existing export

                    filteredLines.AppendLine(line);
                }

                if (filteredLines.Length > 0)
                    File.WriteAllText(indexPath, existing + filteredLines);
            }
            else
            {
                File.WriteAllText(indexPath, sb.ToString());
            }
        }
    }

    private static string? ExtractExportName(string line)
    {
        // "export * as posts from './posts'" → "posts"
        if (line.Contains("* as "))
        {
            var start = line.IndexOf("* as ") + 5;
            var end = line.IndexOf(' ', start);
            return end > start ? line[start..end] : null;
        }

        // "export { default as posts } from './posts'" → "posts"
        if (line.Contains("default as "))
        {
            var start = line.IndexOf("default as ") + 11;
            var end = line.IndexOf(' ', start);
            if (end < 0) end = line.IndexOf('}', start);
            return end > start ? line[start..end].Trim() : null;
        }

        return null;
    }
}
