using Microsoft.Extensions.Logging;

namespace RetroIde.Application.Workspaces;

internal sealed class WorkspaceWatchSet(Action<IReadOnlyList<WorkspaceWatchSet.Change>> publish, ILogger logger) : IDisposable
{
    internal sealed record Change(WorkspaceExplorer.Address Directory, bool Subtree = false, bool? RootAvailable = null);

    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan AvailabilityInterval = TimeSpan.FromSeconds(1);
    private readonly Lock _gate = new();
    private readonly Dictionary<WorkspaceExplorer.Address, Change> _pending = new(WorkspaceExplorer.AddressEquality);
    private PeriodicTimer? _timer;
    private WorkspaceExplorer.Root[] _requested = [];
    private bool _disposed;

    public void UpdateRoots(WorkspaceExplorer.Root[] roots)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_requested.SequenceEqual(roots))
                _requested = roots;
            if (_timer is null && roots.Length > 0)
            {
                var timer = new PeriodicTimer(FlushInterval);
                _timer = timer;
                _ = Task.Run(() => RunAsync(timer));
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _pending.Clear();
            _timer?.Dispose();
        }
    }

    private async Task RunAsync(PeriodicTimer timer)
    {
        var watches = new Dictionary<Guid, RootWatch>();
        WorkspaceExplorer.Root[]? active = null;
        var nextProbe = 0L;
        var failedPublish = false;
        try
        {
            while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
            {
                WorkspaceExplorer.Root[] roots;
                lock (_gate)
                    roots = _requested;

                if (!ReferenceEquals(roots, active))
                {
                    Synchronize(roots, watches);
                    active = roots;
                    nextProbe = 0;
                }

                var now = Environment.TickCount64;
                if (now >= nextProbe)
                {
                    foreach (var watch in watches.Values)
                        watch.CheckAvailability();
                    nextProbe = now + (long)AvailabilityInterval.TotalMilliseconds;
                }

                Change[] changes;
                lock (_gate)
                {
                    changes = _pending.Values.ToArray();
                    _pending.Clear();
                }
                if (changes.Length > 0)
                {
                    try
                    {
                        publish(changes);
                        failedPublish = false;
                    }
                    catch (Exception error) when (RuntimeFailures.CanRecover(error))
                    {
                        if (!failedPublish)
                            RuntimeFailures.Report(logger, error, "Explorer watch delivery", LogLevel.Warning);
                        failedPublish = true;
                        Requeue(changes);
                    }
                }
            }
        }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(logger, error, "Explorer watcher loop");
            lock (_gate)
            {
                timer.Dispose();
                _timer = null;
            }
        }
        finally
        {
            foreach (var watch in watches.Values)
                watch.Dispose();
        }
    }

    private void Synchronize(WorkspaceExplorer.Root[] roots, Dictionary<Guid, RootWatch> watches)
    {
        var requested = roots.ToDictionary(root => root.Id);
        foreach (var (id, watch) in watches.ToArray())
        {
            if (requested.TryGetValue(id, out var root) && string.Equals(root.Path, watch.Root.Path, StringComparison.Ordinal))
                continue;

            watches.Remove(id);
            watch.Dispose();
        }
        foreach (var root in roots)
        {
            if (!watches.ContainsKey(root.Id))
                watches.Add(root.Id, new RootWatch(root, Enqueue, logger));
        }
    }

    private void Enqueue(Change next)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            if (_pending.TryGetValue(next.Directory, out var previous))
                next = Merge(previous, next);
            _pending[next.Directory] = next;
        }
    }

    private void Requeue(Change[] changes)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            foreach (var earlier in changes)
                _pending[earlier.Directory] = _pending.TryGetValue(earlier.Directory, out var newer) ? Merge(earlier, newer) : earlier;
        }
    }

    private static Change Merge(Change previous, Change next) => next with
    {
        Subtree = previous.Subtree || next.Subtree, RootAvailable = next.RootAvailable ?? previous.RootAvailable,
    };

    private sealed class RootWatch(WorkspaceExplorer.Root root, Action<Change> enqueue, ILogger logger) : IDisposable
    {
        private readonly WorkspaceExplorer.Address _address = new(root.Id, "");
        private readonly string? _parent = Path.GetDirectoryName(root.Path);
        private FileSystemWatcher? _watcher;
        private FileSystemWatcher? _parentWatcher;
        private bool? _available;
        private int _restartRequested;
        private int _restartParentRequested;
        private bool _watchStartFailed;
        private bool _parentStartFailed;

        public WorkspaceExplorer.Root Root => root;

        public void CheckAvailability()
        {
            var exists = Directory.Exists(root.Path);
            if (_available != exists)
            {
                enqueue(new Change(_address, Subtree: _available is not null || !exists, RootAvailable: exists));
                _available = exists;
            }

            if (!exists || Interlocked.Exchange(ref _restartRequested, 0) != 0)
            {
                _watcher?.Dispose();
                _watcher = null;
            }

            if (exists && _watcher is null)
                Start();

            if (Interlocked.Exchange(ref _restartParentRequested, 0) != 0)
            {
                _parentWatcher?.Dispose();
                _parentWatcher = null;
            }
            if (_parentWatcher is null && _parent is not null)
                StartParentWatch(_parent);
        }

        public void Dispose()
        {
            _watcher?.Dispose();
            _parentWatcher?.Dispose();
            _watcher = null;
            _parentWatcher = null;
        }

        private void Start()
        {
            FileSystemWatcher? watcher = null;
            try
            {
                watcher = new FileSystemWatcher(root.Path)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                };
                watcher.Created += OnChanged;
                watcher.Deleted += OnChanged;
                watcher.Renamed += OnRenamed;
                watcher.Error += OnError;
                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
                _watchStartFailed = false;
                enqueue(new Change(_address, Subtree: true));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                watcher?.Dispose();
                if (!_watchStartFailed)
                    RuntimeFailures.Report(logger, error, "start Explorer root watcher; rechecking availability", LogLevel.Warning);
                _watchStartFailed = true;
            }
        }

        private void StartParentWatch(string parent)
        {
            FileSystemWatcher? watcher = null;
            try
            {
                watcher = new FileSystemWatcher(parent) { NotifyFilter = NotifyFilters.DirectoryName };
                watcher.Created += (_, args) => OnRootEntryChanged(args.FullPath);
                watcher.Deleted += (_, args) => OnRootEntryChanged(args.FullPath);
                watcher.Renamed += (_, args) =>
                {
                    OnRootEntryChanged(args.OldFullPath);
                    OnRootEntryChanged(args.FullPath);
                };
                watcher.Error += (_, error) =>
                {
                    if (error.GetException() is { } exception)
                        RuntimeFailures.Report(logger, exception, "Explorer parent watcher; requesting a restart", LogLevel.Warning);
                    Interlocked.Exchange(ref _restartParentRequested, 1);
                };
                watcher.EnableRaisingEvents = true;
                _parentWatcher = watcher;
                _parentStartFailed = false;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                watcher?.Dispose();
                if (!_parentStartFailed)
                    RuntimeFailures.Report(logger, error, "start Explorer parent watcher; rechecking availability", LogLevel.Warning);
                _parentStartFailed = true;
            }
        }

        private void OnRootEntryChanged(string path)
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(path, root.Path, comparison))
            {
                Interlocked.Exchange(ref _restartRequested, 1);
                enqueue(new Change(_address, Subtree: true));
            }
        }

        private void OnChanged(object sender, FileSystemEventArgs args) => EnqueueParent(args.FullPath);

        private void OnRenamed(object sender, RenamedEventArgs args)
        {
            EnqueueParent(args.OldFullPath);
            EnqueueParent(args.FullPath);
        }

        private void OnError(object sender, ErrorEventArgs args)
        {
            if (args.GetException() is { } error)
                RuntimeFailures.Report(logger, error, "Explorer root watcher; requesting a rescan", LogLevel.Warning);
            Interlocked.Exchange(ref _restartRequested, 1);
            enqueue(new Change(_address, Subtree: true));
        }

        private void EnqueueParent(string changed)
        {
            var relative = Path.GetRelativePath(root.Path, changed);
            if (Path.IsPathRooted(relative) || string.Equals(relative, "..", StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return;

            enqueue(new Change(new WorkspaceExplorer.Address(root.Id, Path.GetDirectoryName(relative) ?? "")));
        }
    }
}
