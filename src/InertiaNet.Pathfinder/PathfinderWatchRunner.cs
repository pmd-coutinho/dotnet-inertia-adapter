using System.Threading.Channels;

using InertiaNet.Pathfinder.Analysis;

namespace InertiaNet.Pathfinder;

internal static class PathfinderWatchRunner
{
    private const string WatcherErrorMarker = "__watcher_error__";
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "node_modules", ".git" };

    public static async Task RunAsync(string projectPath, PathfinderConfig config, Action regenerate,
        CancellationToken cancellationToken, Func<IEnumerable<string>>? additionalDirectories = null)
    {
        projectPath = Path.GetFullPath(projectPath);
        var changes = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var watchers = new Dictionary<string, FileSystemWatcher>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var output = Path.GetFullPath(config.OutputPath);
        void Queue(string? path, bool directoryEvent = false)
        {
            if (path is not null && (Under(output, path) || path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(ExcludedDirectories.Contains))) return;
            if (directoryEvent || path == WatcherErrorMarker || ShouldProcessPath(path)
                || path is not null && config.InputFiles.Contains(path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
                changes.Writer.TryWrite(path ?? WatcherErrorMarker);
        }
        void RefreshWatchers()
        {
            var roots = (additionalDirectories?.Invoke() ?? []).Append(projectPath).Select(Path.GetFullPath)
                .Where(Directory.Exists).Where(path => path == projectPath || !Under(projectPath, path)).Distinct(watchers.Comparer).ToHashSet(watchers.Comparer);
            foreach (var removed in watchers.Keys.Except(roots, watchers.Comparer).ToArray())
            { watchers[removed].Dispose(); watchers.Remove(removed); }
            foreach (var root in roots.Where(root => !watchers.ContainsKey(root)))
            {
                var watcher = new FileSystemWatcher(root)
                {
                    Filter = "*", IncludeSubdirectories = root == projectPath,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.DirectoryName,
                };
                watcher.Changed += (_, e) => Queue(e.FullPath);
                watcher.Created += (_, e) => Queue(e.FullPath, Directory.Exists(e.FullPath));
                watcher.Deleted += (_, e) => Queue(e.FullPath, !Path.HasExtension(e.FullPath));
                watcher.Renamed += (_, e) => { Queue(e.OldFullPath, !Path.HasExtension(e.OldFullPath)); Queue(e.FullPath, Directory.Exists(e.FullPath)); };
                watcher.Error += (_, _) => Queue(WatcherErrorMarker);
                watchers[root] = watcher;
                watcher.EnableRaisingEvents = true;
            }
        }
        try
        {
            RefreshWatchers();
            if (!config.Quiet) Console.WriteLine($"Pathfinder: Watching {projectPath} for changes... (Ctrl+C to stop)");
            await ProcessChangesAsync(changes.Reader, config, () => { regenerate(); RefreshWatchers(); }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            foreach (var watcher in watchers.Values) watcher.Dispose();
            changes.Writer.TryComplete();
            if (!config.Quiet) Console.WriteLine("Pathfinder: Stopped watching.");
        }
    }

    internal static bool ShouldProcessPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(ExcludedDirectories.Contains)) return false;
        return Path.GetExtension(path).ToLowerInvariant() is ".cs" or ".csproj" or ".props" or ".targets"
            || Path.GetFileName(path) == "global.json";
    }

    private static bool Under(string root, string path)
        => path.Equals(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static async Task ProcessChangesAsync(ChannelReader<string> reader, PathfinderConfig config, Action regenerate, CancellationToken ct)
    {
        while (await reader.WaitToReadAsync(ct))
        {
            var pending = new HashSet<string>(StringComparer.Ordinal);
            void Drain() { while (reader.TryRead(out var path)) pending.Add(path); }
            Drain();
            while (true)
            {
                using var debounce = CancellationTokenSource.CreateLinkedTokenSource(ct);
                debounce.CancelAfter(config.WatchDebounceMs);
                try
                {
                    if (!await reader.WaitToReadAsync(debounce.Token)) return;
                    Drain();
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { break; }
            }
            ct.ThrowIfCancellationRequested();
            if (!config.Quiet) Console.WriteLine(pending.Contains(WatcherErrorMarker)
                ? "Pathfinder: Watcher overflow; running full generation."
                : $"Pathfinder: {pending.Count} changed paths; regenerating.");
            try { regenerate(); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                PathfinderDiagnostics.Clear();
                PathfinderDiagnostics.Report("pathfinder", 1, ex.Message, "PATHFINDER900", "error");
                PathfinderDiagnostics.Write(Console.Error, config.JsonDiagnostics);
            }
        }
    }
}
