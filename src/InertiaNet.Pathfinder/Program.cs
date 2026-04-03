using Microsoft.CodeAnalysis.CSharp;
using InertiaNet.Pathfinder;
using InertiaNet.Pathfinder.Analysis;
using InertiaNet.Pathfinder.Generation;

var config = PathfinderConfig.FromArgs(args);
var projectPath = Path.GetFullPath(config.ProjectPath);

if (!Directory.Exists(projectPath))
{
    Console.Error.WriteLine($"Error: Project directory not found: {projectPath}");
    return 1;
}

if (!config.Quiet)
    Console.WriteLine($"Pathfinder: Scanning {projectPath}...");

// Find all .cs files
var csFiles = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories)
    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
    .OrderBy(f => f, StringComparer.Ordinal)
    .ToArray();

if (!config.Quiet)
    Console.WriteLine($"Pathfinder: Found {csFiles.Length} source files.");

// Parse all files with Roslyn
var allRoutes = new List<RouteInfo>();
var allEnums = new List<EnumInfo>();
var trees = new List<Microsoft.CodeAnalysis.SyntaxTree>();

PathfinderDiagnostics.Clear();

foreach (var file in csFiles)
{
    var source = File.ReadAllText(file);
    var tree = CSharpSyntaxTree.ParseText(source, path: file);
    trees.Add(tree);

    var controllerRoutes = ControllerRouteDiscoverer.Discover(tree);
    var minimalApiRoutes = MinimalApiRouteDiscoverer.Discover(tree);
    var wolverineRoutes = WolverineRouteDiscoverer.Discover(tree);
    var enums = EnumDiscoverer.Discover(tree);

    allRoutes.AddRange(controllerRoutes);
    allRoutes.AddRange(minimalApiRoutes);
    allRoutes.AddRange(wolverineRoutes);
    allEnums.AddRange(enums);
}

// Apply skip patterns
if (config.SkipPatterns.Length > 0)
{
    allRoutes = allRoutes
        .Where(r => !config.SkipPatterns.Any(pattern => MatchesPattern(r.ControllerFullName, pattern)))
        .ToList();
}

// Register discovered enums in the type mapper
foreach (var enumInfo in allEnums)
    TypeMapper.RegisterEnum(enumInfo.ShortName, enumInfo.ShortName);

// Discover page props from Inertia Render() calls
var treesArray = trees.ToArray();
var allPageProps = PropsDiscoverer.Discover(treesArray);

// Discover models referenced by page props
var allModels = ModelDiscoverer.Discover(treesArray, allPageProps, allRoutes);

if (!config.Quiet)
{
    Console.WriteLine($"Pathfinder: Discovered {allRoutes.Count} routes, {allEnums.Count} enums, {allPageProps.Count} page types, {allModels.Count} models.");

    foreach (var diagnostic in PathfinderDiagnostics.Current.OrderBy(message => message, StringComparer.Ordinal))
        Console.WriteLine($"Pathfinder: Warning: {diagnostic}");
}

// Generate TypeScript
TypeScriptGenerator.Generate(allRoutes, allEnums, allPageProps, allModels, config);

if (!config.Quiet)
    Console.WriteLine($"Pathfinder: Output written to {Path.GetFullPath(config.OutputPath)}");

// Watch mode
if (config.Watch)
{
    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    await PathfinderWatchRunner.RunAsync(projectPath, config, () => RunGeneration(projectPath, config), cts.Token);
}

return 0;

static void RunGeneration(string projectPath, PathfinderConfig config)
{
    var csFiles = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                    !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
        .OrderBy(f => f, StringComparer.Ordinal)
        .ToArray();

    var allRoutes = new List<RouteInfo>();
    var allEnums = new List<EnumInfo>();
    var trees = new List<Microsoft.CodeAnalysis.SyntaxTree>();

    TypeMapper.ClearEnums();
    PathfinderDiagnostics.Clear();

    foreach (var file in csFiles)
    {
        var source = File.ReadAllText(file);
        var tree = CSharpSyntaxTree.ParseText(source, path: file);
        trees.Add(tree);

        allRoutes.AddRange(ControllerRouteDiscoverer.Discover(tree));
        allRoutes.AddRange(MinimalApiRouteDiscoverer.Discover(tree));
        allRoutes.AddRange(WolverineRouteDiscoverer.Discover(tree));
        allEnums.AddRange(EnumDiscoverer.Discover(tree));
    }

    if (config.SkipPatterns.Length > 0)
    {
        allRoutes = allRoutes
            .Where(r => !config.SkipPatterns.Any(pattern => MatchesPattern(r.ControllerFullName, pattern)))
            .ToList();
    }

    foreach (var enumInfo in allEnums)
        TypeMapper.RegisterEnum(enumInfo.ShortName, enumInfo.ShortName);

    var treesArray = trees.ToArray();
    var allPageProps = PropsDiscoverer.Discover(treesArray);
    var allModels = ModelDiscoverer.Discover(treesArray, allPageProps, allRoutes);

    TypeScriptGenerator.Generate(allRoutes, allEnums, allPageProps, allModels, config);

    if (!config.Quiet)
    {
        foreach (var diagnostic in PathfinderDiagnostics.Current.OrderBy(message => message, StringComparer.Ordinal))
            Console.WriteLine($"Pathfinder: Warning: {diagnostic}");
    }
}

static bool MatchesPattern(string value, string pattern)
{
    // Simple wildcard matching: *Health* matches "HealthController", "MyHealthCheck", etc.
    if (pattern.StartsWith('*') && pattern.EndsWith('*'))
        return value.Contains(pattern[1..^1], StringComparison.OrdinalIgnoreCase);
    if (pattern.StartsWith('*'))
        return value.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
    if (pattern.EndsWith('*'))
        return value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
    return value.Equals(pattern, StringComparison.OrdinalIgnoreCase);
}
