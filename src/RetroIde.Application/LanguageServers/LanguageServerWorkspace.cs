using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace RetroIde.Application.LanguageServers;

internal sealed class LanguageServerWorkspace(ILogger logger) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, Slot> _servers = new();
    private readonly ConcurrentDictionary<Guid, LanguageDocument> _documents = new();
    private readonly SemaphoreSlim _configuration = new(1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Lock _disposeGate = new();
    private Task? _dispose;

    internal LanguageServerStatus[] List() => _servers.Values.Select(slot => slot.Status).OrderBy(item => item.RootId).ToArray();

    internal bool Contains(Guid rootId) => _servers.ContainsKey(rootId);

    internal LanguageServerOutput? ReadOutput(Guid rootId) =>
        _servers.TryGetValue(rootId, out var slot) ? new(slot.Status, slot.Session?.Output ?? "") : null;

    internal async Task<LanguageServerStatus> ConfigureAsync(LanguageServerConfiguration settings, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        await _configuration.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (_servers.TryGetValue(settings.RootId, out var installed))
                await StopAsync(installed).ConfigureAwait(false);
            return await StartAsync(settings, linked.Token).ConfigureAwait(false);
        }
        finally { _configuration.Release(); }
    }

    internal async Task<LanguageServerStatus?> SetRunningAsync(Guid rootId, bool running, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        await _configuration.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (!_servers.TryGetValue(rootId, out var slot))
                return null;
            await StopAsync(slot).ConfigureAwait(false);
            return running ? await StartAsync(slot.Configuration, linked.Token).ConfigureAwait(false) : _servers[rootId].Status;
        }
        finally { _configuration.Release(); }
    }

    internal bool TryQuery(LanguageDocument document, string method, string provider, int? offset, CancellationToken token,
        [NotNullWhen(true)] out Task<LanguageQueryResult>? pending)
    {
        pending = null;
        if (!_servers.TryGetValue(document.Context.RootId, out var slot) || slot.Session is not { IsRunning: true } session ||
            slot.Configuration.GetLanguage(document.Path) is not { } language)
            return false;
        pending = Task.Run(() => session.QueryAsync(document, language, method, provider, offset,
            () => _documents.TryGetValue(document.Context.ViewKey, out var latest) && latest.Context == document.Context,
            token), token);
        return true;
    }

    internal LanguageDiagnosticResult Diagnostics(LanguageDocument document)
    {
        if (_servers.TryGetValue(document.Context.RootId, out var slot) && slot.Session is { IsRunning: true } session)
            return session.Diagnostics(document);
        return new(document.Context, null, []);
    }

    internal bool IsCurrent(Guid rootId, Guid? sessionId) =>
        _servers.TryGetValue(rootId, out var slot) && slot.Session is { IsRunning: true } session && session.Id == sessionId;

    internal void Observe(LanguageDocument document)
    {
        if (_shutdown.IsCancellationRequested)
            return;
        _documents[document.Context.ViewKey] = document;
        if (_servers.TryGetValue(document.Context.RootId, out var slot))
            _ = Task.Run(() => SynchronizeAsync(slot, document, _shutdown.Token));
    }

    internal void Closed(LanguageDocument document)
    {
        _documents.TryRemove(document.Context.ViewKey, out _);
        if (_servers.TryGetValue(document.Context.RootId, out var slot) && slot.Session is { IsRunning: true } session)
            _ = CloseDocumentAsync(session, document);
    }

    private async Task<LanguageServerStatus> StartAsync(LanguageServerConfiguration settings, CancellationToken token)
    {
        _servers[settings.RootId] = new Slot(settings, null, "starting");
        LanguageServerSession? session = null;
        try
        {
            session = await LanguageServerSession.StartAsync(settings, logger, token).ConfigureAwait(false);
            var slot = new Slot(settings, session, "running");
            _servers[settings.RootId] = slot;
            foreach (var document in _documents.Values.Where(item => item.Context.RootId == settings.RootId))
                await SynchronizeAsync(slot, document, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            _ = ReapAsync(session);
            return slot.Status;
        }
        catch
        {
            _servers[settings.RootId] = new(settings, null, token.IsCancellationRequested ? "stopped" : "faulted");
            if (session is not null)
                await DisposeSessionAsync(session).ConfigureAwait(false);
            throw;
        }
    }

    private async Task StopAsync(Slot slot)
    {
        _servers[slot.Configuration.RootId] = new(slot.Configuration, null, "stopped");
        if (slot.Session is not null)
            await DisposeSessionAsync(slot.Session).ConfigureAwait(false);
    }

    private async Task SynchronizeAsync(Slot slot, LanguageDocument document, CancellationToken token)
    {
        if (slot.Session is not { IsRunning: true } session || !_documents.TryGetValue(document.Context.ViewKey, out var latest) ||
            !ReferenceEquals(document, latest) || slot.Configuration.GetLanguage(document.Path) is not { } language)
            return;
        try
        {
            await session.SynchronizeAsync(document, language, () =>
                _documents.TryGetValue(document.Context.ViewKey, out var current) && ReferenceEquals(document, current), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (LanguageDocumentChangedException) { }
        catch (NotSupportedException) { }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            if (session.IsRunning)
                RuntimeFailures.Report(logger, error, "Synchronize language server document");
        }
    }

    private async Task CloseDocumentAsync(LanguageServerSession session, LanguageDocument document)
    {
        try { await session.CloseDocumentAsync(document, _shutdown.Token).ConfigureAwait(false); }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            if (session.IsRunning && !_shutdown.IsCancellationRequested)
                RuntimeFailures.Report(logger, error, "Close language server document");
        }
    }

    private async Task ReapAsync(LanguageServerSession session)
    {
        await session.Completion.ConfigureAwait(false);
        await DisposeSessionAsync(session).ConfigureAwait(false);
    }

    private async Task DisposeSessionAsync(LanguageServerSession session)
    {
        try { await session.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(logger, error, "Dispose language server session");
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
            return new ValueTask(_dispose ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        await _configuration.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var slot in _servers.Values)
                await StopAsync(slot).ConfigureAwait(false);
            _servers.Clear();
            _documents.Clear();
        }
        finally { _configuration.Release(); }
    }

    private sealed record Slot(LanguageServerConfiguration Configuration, LanguageServerSession? Session, string State)
    {
        internal LanguageServerStatus Status => Session?.Status ?? new(Configuration.RootId, Configuration.Executable, State);
    }
}
