using InertiaNet.Pathfinder;
using InertiaNet.Pathfinder.Analysis;
using InertiaNet.Pathfinder.Generation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

PathfinderDiagnostics.Clear();
try
{
    var config = PathfinderConfig.FromArgs(args);
    if (config.Help)
    {
        Console.WriteLine("""
            pathfinder [options]
              -p, --project <directory|csproj>   Project to evaluate, or a source directory
              -o, --output <directory>          Generated output (default ./pathfinder)
              --framework <tfm>                 Target framework (defaults to first declared)
              -c, --configuration <name>        Project configuration (default Debug)
              --contracts <json>               Explicit JSON output/input contract profiles
              --endpoint-manifest <json>       Compare source routes with an app-exported snapshot
              --actions|--routes|--forms <bool> Route output switches (default true)
              --models|--enums|--props <bool>    Experimental type output switches (default true)
              --wolverine <bool>                Experimental endpoint discovery (default true)
              --skip <patterns>                 Semicolon-separated controller wildcard patterns
              --strict, --fail-on-warning       Fail without writing when discovery has warnings
              --check                          Report stale generated files without modifying them
              --adopt                          Adopt matching paths in a legacy generated directory
              --no-clean, --clean false         Retain previously owned files no longer generated
              -q, --quiet                       Suppress progress; diagnostics still go to stderr
              --diagnostics-json                Write structured discovery diagnostics to stderr
              -w, --watch                       Regenerate after source/project changes
              --debounce <1..60000>              Watch debounce in milliseconds (default 300)
              --version                         Print tool version
              -h, --help                        Show this help
            Exit codes: 0 success/current; 1 stale output or strict diagnostics; 2 invalid input/failure.
            """);
        return 0;
    }
    if (config.Version)
    {
        Console.WriteLine(typeof(PathfinderConfig).Assembly.GetName().Version);
        return 0;
    }
    using var cancellation = new CancellationTokenSource();
    ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    Console.CancelKeyPress += cancel;
    try
    {
        var (exitCode, sources) = await RunGenerationAsync(config, cancellation.Token);
        if (!config.Watch) return exitCode;
        await PathfinderWatchRunner.RunAsync(sources.Root, config,
            () => { sources = RunGenerationAsync(config, cancellation.Token).GetAwaiter().GetResult().Sources; }, cancellation.Token,
            () => sources.WatchDirectories);
        return 0;
    }
    finally { Console.CancelKeyPress -= cancel; }
}
catch (OperationCanceledException) { return 0; }
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.Xml.XmlException)
{
    PathfinderDiagnostics.Report("pathfinder", 1, ex.Message, "PATHFINDER900", "error");
    PathfinderDiagnostics.Write(Console.Error, args.Contains("--diagnostics-json", StringComparer.Ordinal));
    return 2;
}

static async Task<(int ExitCode, ProjectSources Sources)> RunGenerationAsync(PathfinderConfig config, CancellationToken ct)
{
    PathfinderDiagnostics.Clear();
    TypeMapper.ClearEnums();
    var sources = await ProjectSourceLoader.LoadAsync(config, ct);
    var contractPath = config.ContractFile is null ? null : Path.GetFullPath(config.ContractFile);
    var manifestPath = config.EndpointManifest is null ? null : Path.GetFullPath(config.EndpointManifest);
    var profiles = contractPath is null ? new ContractConfiguration() : ContractConfiguration.Load(contractPath);
    var trees = new List<SyntaxTree>();
    var routes = new List<RouteInfo>();
    if (!config.Quiet) Console.WriteLine($"Pathfinder: Reading {sources.Files.Length} sources from {sources.ProjectFile ?? sources.Root}.");
    foreach (var file in sources.Files)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(file))
        {
            PathfinderDiagnostics.Report(file, 1, "Evaluated Compile item does not exist. Generate it before running Pathfinder.", "PATHFINDER110");
            continue;
        }
        var tree = CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(file, ct), sources.ParseOptions,
            path: Path.GetRelativePath(sources.Root, file).Replace('\\', '/'), cancellationToken: ct);
        trees.Add(tree);
        foreach (var error in tree.GetDiagnostics(ct).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            var span = error.Location.GetLineSpan();
            PathfinderDiagnostics.Report(span.Path, span.StartLinePosition.Line + 1, error.GetMessage(), "PATHFINDER200", "error");
        }
    }
    if (sources.GlobalUsings.Length > 0)
        trees.Add(CSharpSyntaxTree.ParseText(string.Join('\n', sources.GlobalUsings), sources.ParseOptions, path: "Pathfinder.GlobalUsings.g.cs"));
    var project = await ProjectCompilationLoader.LoadAsync(sources, trees.ToArray(), config, ct);
    var inputs = project.Inputs.Concat(new[] { contractPath, manifestPath }.OfType<string>()).Distinct(StringComparer.Ordinal).ToArray();
    sources = sources with { AdditionalInputs = inputs };
    config.InputFiles = inputs;
    foreach (var tree in trees)
    {
        routes.AddRange(ControllerRouteDiscoverer.Discover(tree));
        routes.AddRange(MinimalApiRouteDiscoverer.Discover(tree, project.Compilation.GetSemanticModel(tree)));
        if (config.DiscoverWolverine) routes.AddRange(WolverineRouteDiscoverer.Discover(tree));
    }
    routes = routes.Where(route => !config.SkipPatterns.Any(pattern => MatchesPattern(route.ControllerFullName, pattern))).ToList();
    var contracts = new SemanticContractDiscoverer(project.Compilation, config, profiles.Output, profiles.Input).Discover(trees, routes);
    routes = contracts.Routes;
    if (manifestPath is not null) EndpointManifestValidator.Validate(routes, manifestPath, config);
    var enums = contracts.Enums;
    var pages = contracts.Pages;
    var models = contracts.Models;
    if (PathfinderDiagnostics.Entries.Any(entry => entry.Severity == "error") || (config.Strict && PathfinderDiagnostics.Entries.Count > 0))
    {
        PathfinderDiagnostics.Write(Console.Error, config.JsonDiagnostics);
        return (1, sources);
    }
    var changed = TypeScriptGenerator.Generate(routes, enums, pages, models, config, contracts.SharedProps);
    if (config.Check && changed)
        PathfinderDiagnostics.Report(config.OutputPath, 1, "Generated output is stale. Run Pathfinder without --check to update it.", "PATHFINDER300", "error");
    PathfinderDiagnostics.Write(Console.Error, config.JsonDiagnostics);
    if (!config.Quiet) Console.WriteLine(config.Check
        ? changed ? "Pathfinder: Generated output is stale." : "Pathfinder: Generated output is current."
        : $"Pathfinder: {routes.Count} routes, {pages.Count} pages, {models.Count} models; output {Path.GetFullPath(config.OutputPath)}.");
    return (config.Check && changed ? 1 : 0, sources);
}

static bool MatchesPattern(string value, string pattern)
    => System.Text.RegularExpressions.Regex.IsMatch(value,
        "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
