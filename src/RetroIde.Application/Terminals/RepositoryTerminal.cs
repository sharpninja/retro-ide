using System.Collections;
using Microsoft.Extensions.Logging;
using Porta.Pty;
using RetroIde.Application.Git;

namespace RetroIde.Application.Terminals;

internal sealed class RepositoryTerminal : IAsyncDisposable
{
    internal sealed record Exit(int Code, int Signal);
    internal sealed record Shell(string Path, string[] Arguments);

    private readonly IPtyConnection _connection;
    private readonly ILogger _logger;
    private readonly TerminalTranscript _transcript = new(256 * 1024);
    private readonly TaskCompletionSource<Exit> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _input = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _stopToken;
    private readonly Lock _disposeGate = new();
    private readonly Task _pump;
    private Task? _shutdown;

    private RepositoryTerminal(GitWorktree repository, IPtyConnection connection, ILogger logger)
    {
        Repository = repository;
        _connection = connection;
        _logger = logger;
        _stopToken = _lifetime.Token;
        _connection.ProcessExited += OnExited;
        if (_connection.HasExited)
            RecordExit();
        _pump = Task.Run(PumpAsync);
    }

    internal Guid Id { get; } = Guid.NewGuid();
    internal GitWorktree Repository { get; }
    internal int ProcessId => _connection.Pid;
    internal bool IsRunning => Volatile.Read(ref _shutdown) is null && !_exited.Task.IsCompleted;
    internal Exit? ExitResult => _exited.Task.IsCompletedSuccessfully ? _exited.Task.Result : null;
    internal event EventHandler<PtyExitedEventArgs>? ProcessExited
    {
        add => _connection.ProcessExited += value;
        remove => _connection.ProcessExited -= value;
    }

    internal static async Task<RepositoryTerminal> StartAsync(GitWorktree repository, int columns, int rows,
        ILogger logger, CancellationToken token, Shell? shell = null)
    {
        ValidateSize(columns, rows);
        var located = await NativeGit.DiscoverAsync(repository.WorktreePath, "", token).ConfigureAwait(false);
        if (located is null || !NativeGit.IsSameWorktree(located.Repository, repository))
            throw new InvalidOperationException("The selected Git worktree changed before opening its terminal.");
        shell ??= DefaultShell();
        var options = new PtyOptions
        {
            Name = "Retro IDE",
            Cols = columns,
            Rows = rows,
            Cwd = repository.WorktreePath,
            App = shell.Path,
            CommandLine = shell.Arguments,
            Environment = InheritEnvironment(),
            UseAsyncIo = true,
        };
        var connection = await PtyProvider.SpawnAsync(options, token).ConfigureAwait(false);
        var terminal = new RepositoryTerminal(repository, connection, logger);
        if (token.IsCancellationRequested)
        {
            await terminal.DisposeAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        return terminal;
    }

    internal TerminalTranscript.Slice Read(long offset, int maximumBytes) => _transcript.Read(offset, maximumBytes);

    internal ValueTask<TerminalTranscript.Slice> WaitForOutputAsync(long offset, int maximumBytes, CancellationToken token) =>
        _transcript.WaitForOutputAsync(offset, maximumBytes, token);

    internal void ReportFailure(Exception error, string operation) => RuntimeFailures.Report(_logger, error, operation);

    internal Task<Exit> WaitForExitAsync(CancellationToken token) => _exited.Task.WaitAsync(token);

    internal async Task WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stopToken);
        await _input.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            EnsureRunning();
            await _connection.WriterStream.WriteAsync(bytes, linked.Token).ConfigureAwait(false);
            await _connection.WriterStream.FlushAsync(linked.Token).ConfigureAwait(false);
        }
        finally { _input.Release(); }
    }

    internal async Task ResizeAsync(int columns, int rows, CancellationToken token)
    {
        ValidateSize(columns, rows);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stopToken);
        await _input.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            EnsureRunning();
            _connection.Resize(columns, rows);
        }
        finally { _input.Release(); }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
            return new ValueTask(_shutdown ??= Task.Run(ShutdownAsync));
    }

    private async Task PumpAsync()
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (true)
            {
                var count = await _connection.ReaderStream.ReadAsync(buffer, _stopToken).ConfigureAwait(false);
                if (count == 0)
                    return;
                _transcript.Append(buffer.AsSpan(0, count));
            }
        }
        catch (OperationCanceledException) when (_stopToken.IsCancellationRequested) { }
        catch (IOException error)
        {
            if (!_stopToken.IsCancellationRequested && !_connection.HasExited)
                RuntimeFailures.Report(_logger, error, "Repository terminal output", LogLevel.Warning);
        }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(_logger, error, "Repository terminal output");
        }
        finally { _transcript.Complete(); }
    }

    private async Task ShutdownAsync()
    {
        try
        {
            if (!_connection.HasExited)
                await _connection.KillAsync().ConfigureAwait(false);
        }
        finally
        {
            await _lifetime.CancelAsync().ConfigureAwait(false);
            await _input.WaitAsync().ConfigureAwait(false);
            var cancellableRead = _connection.SupportsCancellableRead;
            try
            {
                if (cancellableRead)
                    await _pump.ConfigureAwait(false);
                if (_connection.HasExited)
                    RecordExit();
            }
            finally
            {
                _connection.ProcessExited -= OnExited;
                _connection.Dispose();
                _lifetime.Dispose();
                _input.Release();
            }
            if (!cancellableRead)
                await _pump.ConfigureAwait(false);
        }
    }

    private void OnExited(object? sender, PtyExitedEventArgs args) =>
        _exited.TrySetResult(new Exit(args.ExitCode, args.ExitSignal));

    private void RecordExit() => _exited.TrySetResult(new Exit(_connection.ExitCode, _connection.ExitSignal));

    private void EnsureRunning()
    {
        if (!IsRunning)
            throw new InvalidOperationException("The repository terminal is no longer running.");
    }

    private static Shell DefaultShell()
    {
        var configured = Environment.GetEnvironmentVariable(OperatingSystem.IsWindows() ? "COMSPEC" : "SHELL");
        if (configured is not null && Path.IsPathFullyQualified(configured) && File.Exists(configured))
            return new Shell(configured, []);
        return OperatingSystem.IsWindows()
            ? new Shell(Path.Combine(Environment.SystemDirectory, "cmd.exe"), [])
            : new Shell("/bin/sh", []);
    }

    private static Dictionary<string, string> InheritEnvironment()
    {
        var names = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var environment = new Dictionary<string, string>(names);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            if (entry.Key is string name && entry.Value is string value && !NativeGit.IsRepositoryOverride(name))
                environment[name] = value;
        environment["TERM"] = "xterm-256color";
        return environment;
    }

    private static void ValidateSize(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columns, 1000);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rows, 1000);
    }
}
