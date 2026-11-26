using Microsoft.Extensions.Logging;

namespace RetroIde.Application.Git;

/// <summary>Coalesces native worktree and metadata changes into a serialized status refresh for one selection.</summary>
/// <remarks>2026-11-26 · TR-RETRO-GIT-013#ac-git-state-consistency; TEST-RETRO-013#ac-test-git-extensions. Observe shared refs and the selected worktree's own index.</remarks>
internal sealed class GitStatusMonitor : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(1);
    private static readonly StringComparer NativePaths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly CancellationTokenSource _lifetime = new();
    private int _pending = 1;
    private int _disposed;

    internal GitStatusMonitor(GitSelection selection, Func<CancellationToken, Task> refresh, ILogger logger)
    {
        var token = _lifetime.Token;
        Completion = Task.Run(() => MonitorAsync(selection, refresh, logger, token));
    }

    /// <summary>Completes after cancellation and release of the watcher and native read resources.</summary>
    internal Task Completion { get; }

    private async Task MonitorAsync(GitSelection selection, Func<CancellationToken, Task> refresh, ILogger logger, CancellationToken token)
    {
        var targets = GetTargets(selection).ToArray();
        using var timer = new PeriodicTimer(RefreshInterval);
        var nextProbe = long.MinValue;
        var failedRefresh = false;
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                var now = Environment.TickCount64;
                if (now >= nextProbe)
                {
                    foreach (var target in targets)
                        target.Probe(MarkPending, logger);
                    nextProbe = now + (long)ProbeInterval.TotalMilliseconds;
                }
                if (Interlocked.Exchange(ref _pending, 0) != 0)
                {
                    try
                    {
                        await refresh(token).ConfigureAwait(false);
                        failedRefresh = false;
                    }
                    catch (Exception error) when ((error is not OperationCanceledException || !token.IsCancellationRequested) &&
                        RuntimeFailures.CanRecover(error))
                    {
                        if (!failedRefresh)
                            RuntimeFailures.Report(logger, error, "Git status refresh; retrying", LogLevel.Warning);
                        failedRefresh = true;
                        MarkPending();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(logger, error, "Git status watcher loop");
        }
        finally
        {
            foreach (var target in targets)
                target.Dispose();
        }
    }

    private static IEnumerable<WatchTarget> GetTargets(GitSelection selection)
    {
        var repository = selection.Repository;
        yield return new WatchTarget(selection.ScopeRoot, recursive: true, name =>
            !name.Split(Path.DirectorySeparatorChar).Contains(".git", NativePaths) || NativePaths.Equals(name, ".git"));
        if (Path.GetDirectoryName(selection.ScopeRoot) is { } parent)
            yield return new WatchTarget(parent, recursive: false, name => NativePaths.Equals(name, Path.GetFileName(selection.ScopeRoot)));
        yield return new WatchTarget(repository.GitDirectory, recursive: true, IsGitMetadata);
        if (!NativePaths.Equals(repository.CommonDirectory, repository.GitDirectory))
            yield return new WatchTarget(repository.CommonDirectory, recursive: true, IsGitMetadata);
    }

    private static bool IsGitMetadata(string name)
    {
        var path = name.Replace(Path.DirectorySeparatorChar, '/');
        if (path.EndsWith(".lock", StringComparison.Ordinal))
            return false;
        return path is "index" or "HEAD" or "packed-refs" or "config" or "config.worktree" or "info/exclude" or
            "MERGE_HEAD" or "CHERRY_PICK_HEAD" or "REVERT_HEAD" or "rebase-apply" or "rebase-merge" or "sequencer" or "refs" ||
            path.StartsWith("refs/", StringComparison.Ordinal) || path.StartsWith("sequencer/", StringComparison.Ordinal);
    }

    private void MarkPending() => Volatile.Write(ref _pending, 1);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private sealed class WatchTarget(string directory, bool recursive, Predicate<string> matches) : IDisposable
    {
        private FileSystemWatcher? _watcher;
        private bool? _available;
        private int _restart;
        private bool _startFailed;

        internal void Probe(Action changed, ILogger logger)
        {
            var available = Directory.Exists(directory);
            if (_available != available)
            {
                _available = available;
                changed();
            }

            if (!available || Interlocked.Exchange(ref _restart, 0) != 0)
                Dispose();
            if (!available || _watcher is not null)
                return;

            FileSystemWatcher? watcher = null;
            try
            {
                watcher = new FileSystemWatcher(directory)
                {
                    IncludeSubdirectories = recursive,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                watcher.Created += OnChange;
                watcher.Changed += OnChange;
                watcher.Deleted += OnChange;
                watcher.Renamed += (_, args) =>
                {
                    if (matches(args.Name ?? "") || matches(args.OldName ?? ""))
                        changed();
                };
                watcher.Error += (_, args) =>
                {
                    if (args.GetException() is { } error)
                        RuntimeFailures.Report(logger, error, "Git status watcher; requesting a restart", LogLevel.Warning);
                    Interlocked.Exchange(ref _restart, 1);
                    changed();
                };
                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
                _startFailed = false;
                changed();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                watcher?.Dispose();
                if (!_startFailed)
                    RuntimeFailures.Report(logger, error, "start Git status watcher; rechecking availability", LogLevel.Warning);
                _startFailed = true;
            }

            void OnChange(object sender, FileSystemEventArgs args)
            {
                if (matches(args.Name ?? ""))
                    changed();
            }
        }

        public void Dispose()
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }
}
