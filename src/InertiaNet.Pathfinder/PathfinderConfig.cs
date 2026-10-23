namespace InertiaNet.Pathfinder;

internal sealed class PathfinderConfig
{
    public string ProjectPath { get; set; } = ".";
    public string OutputPath { get; set; } = "./pathfinder";
    public string? Framework { get; set; }
    public string Configuration { get; set; } = "Debug";
    public string? ContractFile { get; set; }
    public string? EndpointManifest { get; set; }
    public bool GenerateActions { get; set; } = true;
    public bool GenerateRoutes { get; set; } = true;
    public bool GenerateForms { get; set; } = true;
    public bool GenerateModels { get; set; } = true;
    public bool GenerateEnums { get; set; } = true;
    public bool GenerateProps { get; set; } = true;
    public bool DiscoverWolverine { get; set; } = true;
    public string[] SkipPatterns { get; set; } = [];
    public bool Clean { get; set; } = true;
    public bool Quiet { get; set; }
    public bool Watch { get; set; }
    public bool Check { get; set; }
    public bool Strict { get; set; }
    public bool Adopt { get; set; }
    public bool JsonDiagnostics { get; set; }
    public bool Help { get; set; }
    public bool Version { get; set; }
    public int WatchDebounceMs { get; set; } = 300;
    public string[] InputFiles { get; set; } = [];

    public static PathfinderConfig FromArgs(string[] args)
    {
        var config = args.Contains("--msbuild", StringComparer.Ordinal) ? FromBuildEnvironment() : new PathfinderConfig();
        string Value(ref int index)
        {
            if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for '{args[index - 1]}'.");
            return args[index];
        }
        bool Boolean(ref int index)
        {
            var value = Value(ref index);
            return bool.TryParse(value, out var parsed) ? parsed
                : throw new ArgumentException($"Expected true or false for '{args[index - 1]}', got '{value}'.");
        }
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--project" or "-p": config.ProjectPath = Value(ref i); break;
                case "--output" or "-o": config.OutputPath = Value(ref i); break;
                case "--framework": config.Framework = Value(ref i); break;
                case "--configuration" or "-c": config.Configuration = Value(ref i); break;
                case "--contracts": config.ContractFile = Value(ref i); break;
                case "--endpoint-manifest": config.EndpointManifest = Value(ref i); break;
                case "--actions": config.GenerateActions = Boolean(ref i); break;
                case "--routes": config.GenerateRoutes = Boolean(ref i); break;
                case "--forms": config.GenerateForms = Boolean(ref i); break;
                case "--models": config.GenerateModels = Boolean(ref i); break;
                case "--enums": config.GenerateEnums = Boolean(ref i); break;
                case "--props": config.GenerateProps = Boolean(ref i); break;
                case "--wolverine": config.DiscoverWolverine = Boolean(ref i); break;
                case "--skip": config.SkipPatterns = Value(ref i).Split(';', StringSplitOptions.RemoveEmptyEntries); break;
                case "--clean": config.Clean = Boolean(ref i); break;
                case "--no-clean": config.Clean = false; break;
                case "--quiet" or "-q": config.Quiet = true; break;
                case "--watch" or "-w": config.Watch = true; break;
                case "--check": config.Check = true; break;
                case "--strict" or "--fail-on-warning": config.Strict = true; break;
                case "--adopt": config.Adopt = true; break;
                case "--diagnostics-json": config.JsonDiagnostics = true; break;
                case "--help" or "-h": config.Help = true; break;
                case "--version": config.Version = true; break;
                case "--msbuild": break;
                case "--debounce":
                    if (!int.TryParse(Value(ref i), out var debounce) || debounce < 1 || debounce > 60000)
                        throw new ArgumentException("Debounce must be between 1 and 60000 milliseconds.");
                    config.WatchDebounceMs = debounce;
                    break;
                default: throw new ArgumentException($"Unknown option '{args[i]}'. Use --help to list supported options.");
            }
        }
        if (config.Check && config.Watch) throw new ArgumentException("--check cannot be combined with --watch.");
        return config;
    }

    private static PathfinderConfig FromBuildEnvironment()
    {
        string? Get(string name) => Environment.GetEnvironmentVariable("PATHFINDER_BUILD_" + name);
        bool Flag(string name, bool fallback)
            => Get(name) is not { Length: > 0 } value ? fallback : bool.TryParse(value, out var result)
                ? result : throw new ArgumentException($"Invalid build boolean '{name}': '{value}'.");
        return new()
        {
            ProjectPath = Get("PROJECT") ?? throw new ArgumentException("Missing build project context."),
            OutputPath = Get("OUTPUT") ?? throw new ArgumentException("Missing build output context."),
            Framework = Get("FRAMEWORK") is { Length: > 0 } framework ? framework : null,
            Configuration = Get("CONFIGURATION") ?? "Debug",
            ContractFile = Get("CONTRACTS") is { Length: > 0 } contracts ? contracts : null,
            EndpointManifest = Get("ENDPOINT_MANIFEST") is { Length: > 0 } manifest ? manifest : null,
            GenerateActions = Flag("ACTIONS", true), GenerateRoutes = Flag("ROUTES", true), GenerateForms = Flag("FORMS", true),
            GenerateModels = Flag("MODELS", true), GenerateEnums = Flag("ENUMS", true), GenerateProps = Flag("PROPS", true),
            DiscoverWolverine = Flag("WOLVERINE", true), Clean = Flag("CLEAN", true), Quiet = Flag("QUIET", true),
            Strict = Flag("STRICT", false), SkipPatterns = (Get("SKIP") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries),
        };
    }
}
