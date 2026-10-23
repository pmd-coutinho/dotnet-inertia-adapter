using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;

namespace InertiaNet.Pathfinder;

internal sealed record ProjectSources(string Root, string? ProjectFile, string? Framework,
    string[] Files, string[] ProjectReferences, string[] ConfigurationFiles, string[] GlobalUsings, CSharpParseOptions ParseOptions)
{
    public string AssemblyName { get; init; } = "Pathfinder.Contracts";
    public string[] MetadataFiles { get; init; } = [];
    public string[] FrameworkFiles { get; init; } = [];
    public string[] MissingFrameworkPacks { get; init; } = [];
    public string? AssetsFile { get; init; }
    public string[] AdditionalInputs { get; init; } = [];
    public NullableContextOptions Nullable { get; init; } = NullableContextOptions.Disable;
    public string[] WatchDirectories => Files.Concat(ConfigurationFiles).Concat(ProjectReferences).Concat(AdditionalInputs).Select(Path.GetDirectoryName)
        .OfType<string>().Append(Root).Where(Directory.Exists).Distinct(StringComparer.Ordinal).ToArray();
}

/// <summary>Uses evaluated Compile items and framework symbols without compiling or starting the app.</summary>
internal static class ProjectSourceLoader
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase) { "obj", "bin", "node_modules", ".git" };

    public static async Task<ProjectSources> LoadAsync(PathfinderConfig config, CancellationToken ct = default)
    {
        var input = Path.GetFullPath(config.ProjectPath);
        var project = File.Exists(input) && input.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? input : null;
        var root = project is not null ? Path.GetDirectoryName(project)! : input;
        if (!Directory.Exists(root)) throw new ArgumentException($"Project directory does not exist: {root}");
        if (project is null)
        {
            var projects = Directory.GetFiles(root, "*.csproj").OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (projects.Length > 1) throw new ArgumentException("Several projects were found. Pass the intended .csproj with --project.");
            project = projects.SingleOrDefault();
        }
        if (project is null)
        {
            if (config.Framework is not null) throw new ArgumentException("--framework requires a .csproj project.");
            return new(root, null, null, DirectorySources(root).OrderBy(path => path, StringComparer.Ordinal).ToArray(), [], [], [], CSharpParseOptions.Default);
        }
        using var initial = await EvaluateAsync(project, config, framework: null, defines: false, ct);
        var initialProperties = initial.RootElement.GetProperty("Properties");
        var frameworks = Property(initialProperties, "TargetFrameworks").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var framework = config.Framework ?? (Property(initialProperties, "TargetFramework") is { Length: > 0 } single ? single : frameworks.FirstOrDefault());
        if (config.Framework is not null && !(frameworks.Length > 0 ? frameworks : new[] { Property(initialProperties, "TargetFramework") }).Contains(config.Framework, StringComparer.Ordinal))
            throw new ArgumentException($"Framework '{config.Framework}' is not declared by {project}.");
        if (string.IsNullOrEmpty(framework)) throw new ArgumentException($"No target framework was found in {project}.");
        using var evaluated = await EvaluateAsync(project, config, framework, defines: true, ct);
        var properties = evaluated.RootElement.GetProperty("Properties");
        var items = evaluated.RootElement.GetProperty("Items");
        var files = Paths(items, "Compile").Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var references = items.GetProperty("ProjectReference").EnumerateArray()
            .Where(item => !item.TryGetProperty("ReferenceOutputAssembly", out var output) || !string.Equals(output.GetString(), "false", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.GetProperty("FullPath").GetString()!).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var language = Property(properties, "LangVersion");
        if (!LanguageVersionFacts.TryParse(language, out var version))
            throw new ArgumentException($"Unsupported C# language version '{language}'. Update Pathfinder or use a supported LangVersion.");
        var symbols = Property(properties, "DefineConstants").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sdk = Property(properties, "MSBuildSDKsPath");
        var sdkRoot = string.IsNullOrEmpty(sdk) ? null : Path.GetDirectoryName(sdk);
        var imports = (await ImportedFilesAsync(project, config, framework, ct))
            .Concat(Property(properties, "MSBuildAllProjects").Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Concat(ConfigurationFiles(root)).Append(project).Distinct(StringComparer.Ordinal).Where(File.Exists)
            .Where(path => sdkRoot is null || !path.StartsWith(sdkRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var usings = items.GetProperty("Using").EnumerateArray().Select(item =>
        {
            var name = item.GetProperty("Identity").GetString();
            var alias = item.TryGetProperty("Alias", out var aliasValue) ? aliasValue.GetString() : null;
            var isStatic = item.TryGetProperty("Static", out var staticValue) && string.Equals(staticValue.GetString(), "true", StringComparison.OrdinalIgnoreCase);
            return $"global using {(isStatic ? "static " : "")}{(string.IsNullOrEmpty(alias) ? "" : alias + " = ")}{name};";
        }).Distinct(StringComparer.Ordinal).ToArray();
        var assets = Property(properties, "ProjectAssetsFile");
        var metadata = PackageReferences(assets, framework)
            .Concat(items.GetProperty("Reference").EnumerateArray().Select(item => item.TryGetProperty("HintPath", out var hint) ? hint.GetString() : null)
                .OfType<string>().Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => Path.GetFullPath(Path.Combine(root, path))))
            .Where(File.Exists).Distinct(StringComparer.Ordinal).ToArray();
        var nullable = Property(properties, "Nullable") switch
        {
            "enable" => NullableContextOptions.Enable, "annotations" => NullableContextOptions.Annotations,
            "warnings" => NullableContextOptions.Warnings, _ => NullableContextOptions.Disable,
        };
        var packagesRoot = Property(properties, "NuGetPackageRoot");
        var (frameworkFiles, missingPacks) = FrameworkReferences(items, properties, packagesRoot);
        if (missingPacks.Length > 0 && string.IsNullOrEmpty(packagesRoot))
        {
            // NuGetPackageRoot normally comes from generated restore props. A fresh
            // project can still use packs already present in its configured cache.
            packagesRoot = await GlobalPackagesRootAsync(root, ct);
            (frameworkFiles, missingPacks) = FrameworkReferences(items, properties, packagesRoot);
        }
        return new(root, project, framework, files, references, imports, usings, new CSharpParseOptions(version, preprocessorSymbols: symbols))
        {
            AssemblyName = Property(properties, "AssemblyName"), MetadataFiles = metadata, AssetsFile = File.Exists(assets) ? assets : null, Nullable = nullable,
            FrameworkFiles = frameworkFiles, MissingFrameworkPacks = missingPacks,
        };
    }

    private static IEnumerable<string> Paths(JsonElement items, string name)
        => items.GetProperty(name).EnumerateArray().Select(item => item.GetProperty("FullPath").GetString()!)
            .Distinct(StringComparer.Ordinal);

    private static string Property(JsonElement properties, string name)
        => properties.TryGetProperty(name, out var property) ? property.GetString() ?? "" : "";

    private static async Task<JsonDocument> EvaluateAsync(string project, PathfinderConfig config, string? framework, bool defines, CancellationToken ct)
    {
        var arguments = new List<string> { "-getProperty:TargetFramework,TargetFrameworks,DefineConstants,LangVersion,MSBuildAllProjects,MSBuildSDKsPath,AssemblyName,Nullable,ProjectAssetsFile,NetCoreTargetingPackRoot,NuGetPackageRoot,TargetFrameworkIdentifier,TargetFrameworkVersion", "-getItem:Compile,ProjectReference,Using,Reference,FrameworkReference,KnownFrameworkReference" };
        if (defines) arguments.Add("-target:AddImplicitDefineConstants");
        var output = await RunMsbuildAsync(project, config, framework, arguments, ct);
        var jsonStart = output.StartsWith('{') ? 0 : output.IndexOf("\n{", StringComparison.Ordinal) + 1;
        try { return JsonDocument.Parse(output[jsonStart..]); }
        catch (JsonException ex) { throw new InvalidOperationException($"MSBuild did not return project evaluation JSON for '{project}': {output}", ex); }
    }

    private static async Task<string[]> ImportedFilesAsync(string project, PathfinderConfig config, string framework, CancellationToken ct)
    {
        var temporary = Path.GetTempFileName();
        try
        {
            // MSBuildAllProjects is opt-in for imported files. Preprocessing records
            // the imports MSBuild actually resolved, including nested/property imports.
            await RunMsbuildAsync(project, config, framework, ["-preprocess:" + temporary], ct);
            return XDocument.Load(temporary).DescendantNodes().OfType<XComment>()
                .SelectMany(comment => comment.Value.Split('\n')).Select(line => line.Trim())
                .Where(line => Path.IsPathRooted(line) && File.Exists(line))
                .Distinct(StringComparer.Ordinal).ToArray();
        }
        finally { File.Delete(temporary); }
    }

    private static async Task<string> RunMsbuildAsync(string project, PathfinderConfig config, string? framework, IEnumerable<string> arguments, CancellationToken ct)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.GetDirectoryName(project)!, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "msbuild", project, "-nologo", "-property:PathfinderEnabled=false", "-property:Configuration=" + config.Configuration })
            start.ArgumentList.Add(argument);
        if (framework is not null) start.ArgumentList.Add("-property:TargetFramework=" + framework);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet msbuild.");
        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorsTask = process.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException($"Project evaluation timed out for '{project}'.");
        }
        var output = await outputTask;
        var errors = await errorsTask;
        if (process.ExitCode != 0) throw new InvalidOperationException($"MSBuild could not evaluate '{project}':\n{errors}{output}");
        return output;
    }

    private static IEnumerable<string> DirectorySources(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*.cs")) yield return file;
        foreach (var directory in Directory.EnumerateDirectories(root))
            if (!ExcludedDirectories.Contains(Path.GetFileName(directory)) && new DirectoryInfo(directory).LinkTarget is null)
                foreach (var file in DirectorySources(directory)) yield return file;
    }

    private static IEnumerable<string> ConfigurationFiles(string root)
    {
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json" })
            {
                var file = Path.Combine(directory.FullName, name);
                if (File.Exists(file)) yield return file;
            }
    }

    private static IEnumerable<string> PackageReferences(string assetsPath, string framework)
    {
        if (!File.Exists(assetsPath)) yield break;
        using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var assets = document.RootElement;
        if (!assets.TryGetProperty("targets", out var targets) || !assets.TryGetProperty("libraries", out var libraries)
            || !assets.TryGetProperty("packageFolders", out var folders)) yield break;
        // Prefer the portable compile graph, never a runtime-specific implementation graph.
        var graph = targets.EnumerateObject().FirstOrDefault(target => target.Name == framework);
        if (graph.Value.ValueKind != JsonValueKind.Object) yield break;
        foreach (var library in graph.Value.EnumerateObject())
        {
            if (!library.Value.TryGetProperty("compile", out var compile) || !libraries.TryGetProperty(library.Name, out var info)
                || !info.TryGetProperty("path", out var packagePath)) continue;
            foreach (var assembly in compile.EnumerateObject().Where(assembly => assembly.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                foreach (var folder in folders.EnumerateObject())
                {
                    var path = Path.Combine(folder.Name, packagePath.GetString()!, assembly.Name);
                    if (!File.Exists(path)) continue;
                    yield return path;
                    break;
                }
        }
    }

    private static (string[] Files, string[] Missing) FrameworkReferences(JsonElement items, JsonElement properties, string packages)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        var missing = new List<string>();
        var framework = Property(properties, "TargetFrameworkIdentifier") == ".NETCoreApp"
            ? "net" + Property(properties, "TargetFrameworkVersion").TrimStart('v') : Property(properties, "TargetFramework");
        var packRoot = Property(properties, "NetCoreTargetingPackRoot");
        foreach (var reference in items.GetProperty("FrameworkReference").EnumerateArray())
        {
            var identity = reference.GetProperty("Identity").GetString();
            var known = items.GetProperty("KnownFrameworkReference").EnumerateArray().FirstOrDefault(item =>
                Property(item, "Identity") == identity && Property(item, "TargetFramework") == framework);
            if (known.ValueKind == JsonValueKind.Undefined) { missing.Add(identity!); continue; }
            var name = Property(known, "TargetingPackName");
            var version = Property(known, "TargetingPackVersion");
            var directory = new[]
            {
                Path.Combine(packRoot, name, version, "ref", framework),
                Path.Combine(packages, name.ToLowerInvariant(), version, "ref", framework),
            }.FirstOrDefault(Directory.Exists);
            if (directory is null) { missing.Add(name + "/" + version); continue; }
            foreach (var file in Directory.EnumerateFiles(directory, "*.dll")) files.Add(file);
        }
        return (files.OrderBy(file => file, StringComparer.Ordinal).ToArray(), missing.ToArray());
    }

    private static async Task<string> GlobalPackagesRootAsync(string directory, CancellationToken ct)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "nuget", "locals", "global-packages", "--list", "--force-english-output" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot query the NuGet package cache.");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var errors = process.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Querying the NuGet package cache timed out.");
        }
        var text = await output;
        if (process.ExitCode != 0) throw new InvalidOperationException($"Cannot query the NuGet package cache: {await errors}{text}");
        return text.Split('\n').FirstOrDefault(line => line.StartsWith("global-packages: ", StringComparison.OrdinalIgnoreCase))?[17..].Trim() ?? "";
    }
}
