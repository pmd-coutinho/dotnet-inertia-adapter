using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace InertiaNet.Pathfinder.Analysis;

internal sealed record ProjectCompilation(CSharpCompilation Compilation, SyntaxTree[] RootTrees, string[] Inputs);

/// <summary>Builds an analysis-only compilation graph. No application assemblies are loaded or executed.</summary>
internal static class ProjectCompilationLoader
{
    private static readonly Lazy<string[]> PlatformReferences = new(() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? typeof(object).Assembly.Location)
        .Split(Path.PathSeparator).Distinct(StringComparer.Ordinal).ToArray());

    public static async Task<ProjectCompilation> LoadAsync(ProjectSources root, SyntaxTree[] trees, PathfinderConfig config, CancellationToken ct = default)
    {
        var inputs = new HashSet<string>(StringComparer.Ordinal);
        var completed = new Dictionary<string, CSharpCompilation>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        async Task<CSharpCompilation> Compile(ProjectSources source, SyntaxTree[]? suppliedTrees = null)
        {
            ct.ThrowIfCancellationRequested();
            var key = source.ProjectFile ?? source.Root;
            if (completed.TryGetValue(key, out var existing)) return existing;
            if (!visiting.Add(key)) throw new InvalidOperationException($"Circular project references cannot be analyzed: {key}");
            foreach (var file in source.Files.Concat(source.ConfigurationFiles).Concat(source.ProjectReferences).Concat(source.MetadataFiles)) inputs.Add(file);
            if (source.AssetsFile is not null) inputs.Add(source.AssetsFile);
            var compilationTrees = suppliedTrees;
            if (compilationTrees is null)
            {
                var parsed = new List<SyntaxTree>();
                foreach (var file in source.Files)
                {
                    if (!File.Exists(file))
                    {
                        PathfinderDiagnostics.Report(file, 1, "Referenced Compile item is missing. Generate it before running Pathfinder.", "PATHFINDER110");
                        continue;
                    }
                    parsed.Add(CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(file, ct), source.ParseOptions,
                        path: Path.GetRelativePath(root.Root, file).Replace('\\', '/'), cancellationToken: ct));
                }
                if (source.GlobalUsings.Length > 0)
                    parsed.Add(CSharpSyntaxTree.ParseText(string.Join('\n', source.GlobalUsings), source.ParseOptions, path: key + ".GlobalUsings.g.cs"));
                compilationTrees = parsed.ToArray();
            }
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var missing in source.MissingFrameworkPacks)
                PathfinderDiagnostics.Report(source.ProjectFile ?? source.Root, 1, $"Targeting pack '{missing}' is unavailable. Restore the project with a matching SDK for complete metadata.", "PATHFINDER213");
            var platform = source.FrameworkFiles.Length > 0 ? source.FrameworkFiles : PlatformReferences.Value;
            foreach (var file in platform.Concat(source.MetadataFiles)) files[Path.GetFileName(file)] = file;
            var references = files.Values.Select(file => (MetadataReference)MetadataReference.CreateFromFile(file)).ToList();
            foreach (var project in source.ProjectReferences)
            {
                var dependency = await ProjectSourceLoader.LoadAsync(new()
                {
                    ProjectPath = project, Configuration = config.Configuration,
                    // Each referenced project evaluates its own declared framework and symbols.
                    // Multi-target references can be pinned below when they share the root TFM.
                }, ct);
                if (source.Framework is not null && dependency.Framework != source.Framework)
                {
                    try { dependency = await ProjectSourceLoader.LoadAsync(new() { ProjectPath = project, Configuration = config.Configuration, Framework = source.Framework }, ct); }
                    catch (ArgumentException) { /* A netstandard/single-target dependency has its own framework. */ }
                }
                references.RemoveAll(reference => reference.Display is { } display && Path.GetFileNameWithoutExtension(display) == dependency.AssemblyName);
                references.Add((await Compile(dependency)).ToMetadataReference());
            }
            var compilation = CSharpCompilation.Create(source.AssemblyName, compilationTrees, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: source.Nullable));
            completed[key] = compilation;
            visiting.Remove(key);
            return compilation;
        }
        return new(await Compile(root, trees), trees, inputs.OrderBy(file => file, StringComparer.Ordinal).ToArray());
    }

    public static CSharpCompilation CreateForSources(IEnumerable<SyntaxTree> trees)
        => CSharpCompilation.Create("Pathfinder.Contracts", trees,
            PlatformReferences.Value.Select(file => MetadataReference.CreateFromFile(file)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
}
