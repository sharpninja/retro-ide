using Microsoft.Extensions.Logging;

namespace RetroIde.Application.LanguageServers;

internal sealed partial class LanguageServerWorkspace
{
    private async Task<LanguageServerStatus> AttachAsync(LanguageServerConfiguration configuration, CancellationToken token)
    {
        _servers[configuration.Key] = new(configuration, null, "starting");
        NotifyChanged();
        try
        {
            foreach (var session in _servers.Values.Select(slot => slot.Session)
                .OfType<LanguageServerSession>().Where(session => session.IsRunning && session.SupportsWorkspaceFolders).Distinct())
            {
                var members = Members(session);
                if (members.Length > 0 && members.All(configuration.CanShareWith) &&
                    await TryJoinAsync(session, members, configuration, token).ConfigureAwait(false))
                    return _servers[configuration.Key].Status;
            }

            var own = await LanguageServerSession.StartAsync([configuration], _logger, NotifyChanged, ReviewServerEditAsync, token).ConfigureAwait(false);
            await PublishAsync(own, [configuration], token).ConfigureAwait(false);
            return _servers[configuration.Key].Status;
        }
        catch
        {
            _servers[configuration.Key] = new(configuration, null, token.IsCancellationRequested ? "stopped" : "faulted");
            throw;
        }
    }

    private async Task<bool> TryJoinAsync(LanguageServerSession session, LanguageServerConfiguration[] members,
        LanguageServerConfiguration configuration, CancellationToken token)
    {
        try
        {
            if (session.CanNotifyFolderChanges)
            {
                await session.ChangeFolderAsync(configuration, add: true, token).ConfigureAwait(false);
                await InstallAsync(session, [configuration], token).ConfigureAwait(false);
                return true;
            }

            if (!await TryStartGroupAsync([.. members, configuration], token).ConfigureAwait(false))
                return false;
            await DisposeSessionAsync(session).ConfigureAwait(false);
            return true;
        }
        catch (Exception error) when (RuntimeFailures.CanRecover(error) && !token.IsCancellationRequested)
        {
            RuntimeFailures.Report(_logger, error, "Join language server workspace folders");
            return false;
        }
    }

    private async Task RestartAsync(Slot slot, CancellationToken token)
    {
        if (slot.Session is { } session)
        {
            var members = Members(session);
            if (members.Length > 1)
            {
                await RebuildAsync(session, members, token).ConfigureAwait(false);
                return;
            }
        }

        await DetachAsync(slot, token).ConfigureAwait(false);
        await AttachAsync(slot.Configuration, token).ConfigureAwait(false);
    }

    private async Task DetachAsync(Slot slot, CancellationToken token)
    {
        var configuration = slot.Configuration;
        _servers[configuration.Key] = new(configuration, null, "stopped");
        RejectUnservedEdits();
        if (slot.Session is not { } session)
            return;

        var members = Members(session);
        if (members.Length == 0)
        {
            await DisposeSessionAsync(session).ConfigureAwait(false);
            return;
        }

        if (!session.IsRunning)
            return;

        if (session.CanNotifyFolderChanges && Directory.Exists(configuration.Directory))
        {
            try
            {
                await session.ChangeFolderAsync(configuration, add: false, token).ConfigureAwait(false);
                return;
            }
            catch (Exception error) when (RuntimeFailures.CanRecover(error) && !token.IsCancellationRequested)
            {
                RuntimeFailures.Report(_logger, error, "Detach language server workspace folder");
            }
        }

        await RebuildAsync(session, members, token, reportFailure: false).ConfigureAwait(false);
    }

    private async Task RebuildAsync(LanguageServerSession old, LanguageServerConfiguration[] members, CancellationToken token,
        bool reportFailure = true)
    {
        try
        {
            if (members.Length > 1 && await TryStartGroupAsync(members, token).ConfigureAwait(false))
                return;

            foreach (var configuration in members)
            {
                if (_servers[configuration.Key].Session != old)
                    continue;
                _servers[configuration.Key] = new(configuration, null, "starting");
                try
                {
                    var process = await LanguageServerSession.StartAsync([configuration], _logger, NotifyChanged, ReviewServerEditAsync, token).ConfigureAwait(false);
                    await PublishAsync(process, [configuration], token).ConfigureAwait(false);
                }
                catch (Exception error) when (RuntimeFailures.CanRecover(error) && !token.IsCancellationRequested)
                {
                    _servers[configuration.Key] = new(configuration, null, "faulted");
                    RuntimeFailures.Report(_logger, error, "Restart isolated language server after workspace folder change");
                }
            }

            if (reportFailure && members.Any(configuration => _servers[configuration.Key].Session is not { IsRunning: true }))
                throw new IOException("One or more language server profiles did not restart.");
        }
        catch (Exception error) when (RuntimeFailures.CanRecover(error) && !token.IsCancellationRequested && !reportFailure)
        {
            RuntimeFailures.Report(_logger, error, "Rebuild language server after removing workspace folder");
        }
        finally
        {
            foreach (var configuration in members)
                if (_servers[configuration.Key].Session == old)
                    _servers[configuration.Key] = new(configuration, null, "faulted");
            await DisposeSessionAsync(old).ConfigureAwait(false);
        }
    }

    private async Task<bool> TryStartGroupAsync(LanguageServerConfiguration[] members, CancellationToken token)
    {
        LanguageServerSession session;
        try { session = await LanguageServerSession.StartAsync(members, _logger, NotifyChanged, ReviewServerEditAsync, token).ConfigureAwait(false); }
        catch (Exception error) when (RuntimeFailures.CanRecover(error) && !token.IsCancellationRequested)
        {
            RuntimeFailures.Report(_logger, error, "Start shared language server; trying separate processes");
            return false;
        }
        if (!session.SupportsWorkspaceFolders)
        {
            await DisposeSessionAsync(session).ConfigureAwait(false);
            return false;
        }
        await PublishAsync(session, members, token).ConfigureAwait(false);
        return true;
    }

    private Task PublishAsync(LanguageServerSession session, LanguageServerConfiguration[] members, CancellationToken token)
    {
        _ = ReapAsync(session);
        return InstallAsync(session, members, token);
    }

    private async Task InstallAsync(LanguageServerSession session, LanguageServerConfiguration[] members, CancellationToken token)
    {
        foreach (var configuration in members)
            _servers[configuration.Key] = new(configuration, session, "running");
        RejectUnservedEdits();
        foreach (var configuration in members)
            foreach (var document in _documents.Values.Where(item => item.Context.RootId == configuration.RootId))
                await SynchronizeAsync(_servers[configuration.Key], document, token).ConfigureAwait(false);
    }

    private LanguageServerConfiguration[] Members(LanguageServerSession session) => _servers.Values
        .Where(slot => ReferenceEquals(slot.Session, session))
        .Select(slot => slot.Configuration).OrderBy(configuration => configuration.RootId).ToArray();
}
