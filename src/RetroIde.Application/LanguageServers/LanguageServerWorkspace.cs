using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RetroIde.Application.Workspaces;

namespace RetroIde.Application.LanguageServers;

internal sealed partial class LanguageServerWorkspace : IAsyncDisposable
{
    private readonly WorkspaceExplorer _explorer;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<LanguageProfileKey, Slot> _servers = new();
    private readonly ConcurrentDictionary<Guid, LanguageDocument> _documents = new();
    private readonly SemaphoreSlim _configuration = new(1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Lock _disposeGate = new();
    private readonly Slot[] _restore;
    private Task? _dispose;

    internal LanguageReviewStore<LanguageEditPlan> Edits { get; } = new();
    internal LanguageReviewStore<LanguageCodeActionList> Actions { get; } = new();
    internal LanguageReviewStore<LanguageCompletionOffer> Completions { get; } = new();

    internal LanguageServerWorkspace(WorkspaceExplorer explorer, ILogger logger)
    {
        _explorer = explorer;
        _logger = logger;
        var state = explorer.Snapshot();
        var pending = new List<Slot>();
        foreach (var saved in state.LanguageProfiles ?? [])
        {
            var root = state.Roots.Single(item => item.Id == saved.RootId);
            var slot = new Slot(saved.Bind(root), null, saved.Enabled ? "starting" : "stopped");
            _servers[saved.Key] = slot;
            if (saved.Enabled)
                pending.Add(slot);
        }
        _restore = pending.ToArray();
    }

    internal LanguageServerStatus[] List()
    {
        var roots = _explorer.Roots.Select(root => root.Id).ToHashSet();
        return _servers.Values.Where(slot => roots.Contains(slot.Configuration.RootId)).Select(slot => slot.Status)
            .OrderBy(item => item.RootId).ThenBy(item => item.ProfileId, StringComparer.Ordinal).ToArray();
    }

    internal LanguageProfileKey? Resolve(Guid rootId, string? profileId, out bool ambiguous)
    {
        ambiguous = false;
        var key = new LanguageProfileKey(rootId, profileId ?? LanguageProfileKey.DefaultProfile);
        if (_servers.ContainsKey(key))
            return key;
        if (profileId is not null)
            return null;
        var matches = _servers.Keys.Where(item => item.RootId == rootId).Take(2).ToArray();
        ambiguous = matches.Length > 1;
        return matches.Length == 1 ? matches[0] : null;
    }

    internal LanguageServerOutput? ReadOutput(LanguageProfileKey key) =>
        _servers.TryGetValue(key, out var slot) ? new(slot.Status, slot.Session?.Output ?? "") : null;

    internal async Task RestoreAsync()
    {
        try
        {
            await _configuration.WaitAsync(_shutdown.Token).ConfigureAwait(false);
            try
            {
                foreach (var slot in _restore)
                {
                    if (!_servers.TryGetValue(slot.Configuration.Key, out var current) || !ReferenceEquals(slot, current))
                        continue;
                    try { await AttachAsync(slot.Configuration, _shutdown.Token).ConfigureAwait(false); }
                    catch (Exception error) when (RuntimeFailures.CanRecover(error) && !_shutdown.IsCancellationRequested)
                    {
                        RuntimeFailures.Report(_logger, error, "Restore language server profile");
                    }
                }
            }
            finally { _configuration.Release(); }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    internal async Task<LanguageServerStatus?> ConfigureAsync(WorkspaceExplorer.Root root, SavedLanguageProfile profile, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        await _configuration.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            if (!_explorer.RememberLanguageProfile(profile))
                return null;
            if (_servers.TryGetValue(profile.Key, out var installed))
                await DetachAsync(installed, _shutdown.Token).ConfigureAwait(false);
            return await AttachAsync(profile.Bind(root), _shutdown.Token).ConfigureAwait(false);
        }
        finally { _configuration.Release(); }
    }

    internal async Task<LanguageServerStatus?> ChangeProfileAsync(LanguageProfileKey key, ProfileAction action, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        await _configuration.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            if (!_servers.TryGetValue(key, out var slot) || !_explorer.SetLanguageProfileEnabled(key, action == ProfileAction.Restart))
                return null;

            if (action == ProfileAction.Restart)
            {
                await RestartAsync(slot, _shutdown.Token).ConfigureAwait(false);
            }
            else
            {
                await DetachAsync(slot, _shutdown.Token).ConfigureAwait(false);
                if (action == ProfileAction.Remove)
                {
                    _explorer.RemoveLanguageProfile(key);
                    _servers.TryRemove(key, out _);
                    return slot.Status with { State = "removed", SessionId = null, ProcessId = null, WorkspaceRootIds = null };
                }
            }
            return _servers[key].Status;
        }
        finally { _configuration.Release(); }
    }

    internal bool TryQuery(LanguageDocument document, LanguageRequest query, CancellationToken token,
        [NotNullWhen(true)] out Task<LanguageQueryResult>? pending)
    {
        pending = null;
        if (FindDocumentSlot(document) is not { Session: { IsRunning: true } session } slot ||
            slot.Configuration.GetLanguage(document.Path) is not { } language)
            return false;
        pending = Task.Run(() => session.QueryAsync(document, slot.Configuration, language, query,
            () => _documents.TryGetValue(document.Context.ViewKey, out var latest) && latest.Context == document.Context &&
                IsCurrent(slot.Configuration.Key, session.Id), token), token);
        return true;
    }

    internal LanguageDiagnosticResult Diagnostics(LanguageDocument document) =>
        FindDocumentSlot(document)?.Session is { IsRunning: true } session
            ? session.Diagnostics(document) : new(document.Context, null, []);

    internal bool IsCurrent(LanguageProfileKey key, Guid? sessionId) =>
        _servers.TryGetValue(key, out var slot) && slot.Session is { IsRunning: true } session && session.Id == sessionId &&
        _explorer.Roots.Any(root => root.Id == key.RootId);

    internal LanguageServerStatus? ReadSession(LanguageDocument document, Guid sessionId) =>
        FindDocumentSlot(document) is { Session: { IsRunning: true } session } slot && session.Id == sessionId
            ? slot.Status : null;

    internal LanguageDocument? ReadObserved(WorkspaceExplorer.Address address) =>
        _documents.Values.FirstOrDefault(document => WorkspaceExplorer.AddressEquality.Equals(
            new(document.Context.RootId, document.Context.RelativePath), address));

    internal LanguageDocument[] CaptureBuffers() => _documents.Values.ToArray();

    internal int? ReadLspVersion(LanguageDocument document, Guid serverId) =>
        FindDocumentSlot(document) is { Session: { IsRunning: true } session } && session.Id == serverId
            ? session.ReadDocumentVersion(document) : null;

    internal void Observe(LanguageDocument document)
    {
        if (_shutdown.IsCancellationRequested)
            return;
        _documents[document.Context.ViewKey] = document;
        if (FindDocumentSlot(document) is { } slot)
            _ = Task.Run(() => SynchronizeAsync(slot, document, _shutdown.Token));
    }

    internal void Closed(LanguageDocument document)
    {
        _documents.TryRemove(document.Context.ViewKey, out _);
        if (FindDocumentSlot(document)?.Session is { IsRunning: true } session)
            _ = CloseDocumentAsync(session, document);
    }

    internal void ReconcileRoots()
    {
        var roots = _explorer.Roots.Select(root => root.Id).ToHashSet();
        if (!_shutdown.IsCancellationRequested && _servers.Keys.Any(key => !roots.Contains(key.RootId)))
            _ = Task.Run(PruneAsync);
    }

    private async Task PruneAsync()
    {
        try
        {
            await _configuration.WaitAsync(_shutdown.Token).ConfigureAwait(false);
            try
            {
                var roots = _explorer.Roots.Select(root => root.Id).ToHashSet();
                foreach (var slot in _servers.Values.Where(slot => !roots.Contains(slot.Configuration.RootId)))
                {
                    await DetachAsync(slot, _shutdown.Token).ConfigureAwait(false);
                    _servers.TryRemove(slot.Configuration.Key, out _);
                }
                foreach (var document in _documents.Values.Where(document => !roots.Contains(document.Context.RootId)))
                    _documents.TryRemove(document.Context.ViewKey, out _);
            }
            finally { _configuration.Release(); }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(_logger, error, "Remove language server profile for closed workspace root");
        }
    }

    private Slot? FindDocumentSlot(LanguageDocument document)
    {
        var rootId = document.Context.RootId;
        if (!_explorer.Roots.Any(root => root.Id == rootId))
            return null;
        return _servers.Values.FirstOrDefault(slot => slot.Configuration.RootId == rootId &&
            slot.Configuration.GetLanguage(document.Path) is not null);
    }

    private async Task SynchronizeAsync(Slot slot, LanguageDocument document, CancellationToken token)
    {
        if (slot.Session is not { IsRunning: true } session || !_documents.TryGetValue(document.Context.ViewKey, out var latest) ||
            !ReferenceEquals(document, latest) || slot.Configuration.GetLanguage(document.Path) is not { } language)
            return;
        try
        {
            await session.SynchronizeAsync(document, language, () =>
                _documents.TryGetValue(document.Context.ViewKey, out var current) && ReferenceEquals(document, current) &&
                IsCurrent(slot.Configuration.Key, session.Id), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (LanguageDocumentChangedException) { }
        catch (NotSupportedException) { }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            if (session.IsRunning)
                RuntimeFailures.Report(_logger, error, "Synchronize language server document");
        }
    }

    private async Task CloseDocumentAsync(LanguageServerSession session, LanguageDocument document)
    {
        try { await session.CloseDocumentAsync(document, _shutdown.Token).ConfigureAwait(false); }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            if (session.IsRunning && !_shutdown.IsCancellationRequested)
                RuntimeFailures.Report(_logger, error, "Close language server document");
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
            RuntimeFailures.Report(_logger, error, "Dispose language server session");
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
            foreach (var session in _servers.Values.Select(slot => slot.Session).OfType<LanguageServerSession>().Distinct())
                await DisposeSessionAsync(session).ConfigureAwait(false);
            _servers.Clear();
            _documents.Clear();
        }
        finally { _configuration.Release(); }
    }

    internal enum ProfileAction { Stop, Restart, Remove }

    private sealed record Slot(LanguageServerConfiguration Configuration, LanguageServerSession? Session, string State)
    {
        internal LanguageServerStatus Status => Session?.StatusFor(Configuration) ??
            new(Configuration.RootId, Configuration.Executable, State, ProfileId: Configuration.ProfileId);
    }
}
