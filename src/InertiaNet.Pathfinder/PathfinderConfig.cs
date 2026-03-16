namespace InertiaNet.Pathfinder;

class PathfinderConfig
{
    public string ProjectPath { get; set; } = ".";
    public string OutputPath { get; set; } = "./pathfinder";
    public bool GenerateActions { get; set; } = true;
    public bool GenerateRoutes { get; set; } = true;
    public bool GenerateForms { get; set; } = true;
    public string[] SkipPatterns { get; set; } = [];
    public bool Clean { get; set; } = true;
    public bool Quiet { get; set; } = false;
    public bool Watch { get; set; } = false;
    public int WatchDebounceMs { get; set; } = 300;

    public static PathfinderConfig FromArgs(string[] args)
    {
        var config = new PathfinderConfig();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--project" or "-p":
                    config.ProjectPath = args[++i];
                    break;
                case "--output" or "-o":
                    config.OutputPath = args[++i];
                    break;
                case "--actions":
                    config.GenerateActions = bool.Parse(args[++i]);
                    break;
                case "--routes":
                    config.GenerateRoutes = bool.Parse(args[++i]);
                    break;
                case "--forms":
                    config.GenerateForms = bool.Parse(args[++i]);
                    break;
                case "--skip":
                    config.SkipPatterns = args[++i].Split(';', StringSplitOptions.RemoveEmptyEntries);
                    break;
                case "--clean":
                    config.Clean = bool.Parse(args[++i]);
                    break;
                case "--no-clean":
                    config.Clean = false;
                    break;
                case "--quiet" or "-q":
                    config.Quiet = true;
                    break;
                case "--watch" or "-w":
                    config.Watch = true;
                    break;
                case "--debounce":
                    config.WatchDebounceMs = int.Parse(args[++i]);
                    break;
            }
        }

        return config;
    }
}
