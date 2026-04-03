using System.Threading.Channels;

namespace InertiaNet.Pathfinder;

internal static class PathfinderWatchRunner
{
    private const string WatcherErrorMarker = "__watcher_error__";

    public static async Task RunAsync(
        string projectPath,
        PathfinderConfig config,
        Action regenerate,
        CancellationToken cancellationToken)
    {
        if (!config.Quiet)
            Console.WriteLine($"Pathfinder: Watching {projectPath} for changes... (Ctrl+C to stop)");

        var changes = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        using var watcher = new FileSystemWatcher(projectPath)
        {
            Filter = "*",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.DirectoryName,
            EnableRaisingEvents = true,
        };

        void QueuePath(string? path)
        {
            if (path != WatcherErrorMarker && !ShouldProcessPath(path))
                return;

            changes.Writer.TryWrite(path ?? string.Empty);
        }

        void OnChanged(object? _, FileSystemEventArgs e) => QueuePath(e.FullPath);

        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += (_, e) =>
        {
            QueuePath(e.OldFullPath);
            QueuePath(e.FullPath);
        };
        watcher.Error += (_, _) => QueuePath(WatcherErrorMarker);

        try
        {
            await ProcessChangesAsync(changes.Reader, config, regenerate, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            changes.Writer.TryComplete();

            if (!config.Quiet)
                Console.WriteLine("Pathfinder: Stopped watching.");
        }
    }

    internal static bool ShouldProcessPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return false;

        return !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ProcessChangesAsync(
        ChannelReader<string> reader,
        PathfinderConfig config,
        Action regenerate,
        CancellationToken cancellationToken)
    {
        var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var watcherErrored = false;

        while (await reader.WaitToReadAsync(cancellationToken))
        {
            while (reader.TryRead(out var path))
            {
                if (path == WatcherErrorMarker)
                {
                    watcherErrored = true;
                    continue;
                }

                pending.Add(path);
            }

            while (true)
            {
                using var debounceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var delayTask = Task.Delay(config.WatchDebounceMs, debounceCts.Token);
                var waitTask = reader.WaitToReadAsync(cancellationToken).AsTask();

                var completed = await Task.WhenAny(delayTask, waitTask);
                if (completed == waitTask && await waitTask)
                {
                    while (reader.TryRead(out var path))
                    {
                        if (path == WatcherErrorMarker)
                        {
                            watcherErrored = true;
                            continue;
                        }

                        pending.Add(path);
                    }

                    continue;
                }

                break;
            }

            if (!config.Quiet)
            {
                if (watcherErrored)
                {
                    Console.WriteLine("Pathfinder: File watcher overflowed; running a full regeneration.");
                }
                else
                {
                    Console.WriteLine($"Pathfinder: Changes detected in {pending.Count} file(s), regenerating...");
                }
            }

            try
            {
                regenerate();

                if (!config.Quiet)
                    Console.WriteLine("Pathfinder: Regeneration complete.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Pathfinder: Error during regeneration: {ex.Message}");
            }
            finally
            {
                pending.Clear();
                watcherErrored = false;
            }
        }
    }
}
