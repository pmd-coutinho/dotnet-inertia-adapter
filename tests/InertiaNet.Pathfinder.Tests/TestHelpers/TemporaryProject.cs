using System.Diagnostics;

namespace InertiaNet.Pathfinder.Tests.TestHelpers;

internal sealed class TemporaryProject : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("pathfinder-tests-").FullName;
    public string File(string relative, string content)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
        return path;
    }
    public string PathFor(string relative) => Path.Combine(Root, relative);
    public void Dispose() { Directory.Delete(Root, recursive: true); }

    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }

    public static string ToolAssembly => typeof(PathfinderConfig).Assembly.Location;
    public static string RepositoryRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (System.IO.File.Exists(Path.Combine(directory.FullName, "InertiaNet.slnx"))) return directory.FullName;
            throw new InvalidOperationException("Cannot find repository root.");
        }
    }
}
