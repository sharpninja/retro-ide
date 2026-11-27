using System.Text.Json;

namespace RetroIde.Application.LanguageServers;

internal sealed partial class LanguageServerWorkspace
{
    private const int MaximumPendingServerEdits = 16;
    private readonly Lock _serverEditGate = new();
    private readonly Dictionary<Guid, LanguageServerEditReview> _serverEdits = [];

    internal LanguageServerEditPreview[] ServerEditPreviews()
    {
        LanguageServerEditReview[] pending;
        lock (_serverEditGate)
            pending = _serverEdits.Values.Where(review => review.IsPending).ToArray();
        return pending.Select(review => review.Describe()).ToArray();
    }

    internal LanguageServerEditReview? FindServerEdit(Guid id)
    {
        lock (_serverEditGate)
            return _serverEdits.GetValueOrDefault(id) is { IsPending: true } review ? review : null;
    }

    internal bool IsServerEditCurrent(LanguageServerEditReview review)
    {
        if (review.Server.SessionId is not { } session)
            return false;

        var liveRoots = _explorer.Roots.Select(root => root.Id).ToHashSet();
        var served = _servers.Values.Where(slot => slot.Session is { IsRunning: true } running && running.Id == session &&
            liveRoots.Contains(slot.Configuration.RootId)).Select(slot => slot.Configuration.RootId).ToHashSet();
        return review.Documents.All(item => served.Contains(item.File.Address.Entry.RootId));
    }

    private void RejectUnservedEdits()
    {
        LanguageServerEditReview[] pending;
        lock (_serverEditGate)
            pending = _serverEdits.Values.Where(review => review.IsPending).ToArray();

        foreach (var review in pending)
            if (!IsServerEditCurrent(review))
                review.Reject("The language server no longer serves every workspace edit target.");
    }

    private async Task<LanguageServerEditReply> ReviewServerEditAsync(LanguageServerSession session,
        JsonElement parameters, CancellationToken token)
    {
        if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty("edit", out var edit) ||
            edit.ValueKind != JsonValueKind.Object)
            return new(false, "The server supplied an invalid workspace edit.");

        var members = Members(session).Where(configuration => IsCurrent(configuration.Key, session.Id)).ToArray();
        if (members.Length == 0)
            return new(false, "The requesting language server has no active workspace roots.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var cancellation = deadline.Token;
        try
        {
            var scope = members.Select(member => member.RootId).ToHashSet();
            var server = session.StatusFor(members[0]);
            var documents = await LanguageEditPlanner.PrepareFilesAsync(edit, _explorer, this, server, CaptureBuffers(),
                cancellation, allowedRoots: scope).ConfigureAwait(false);
            if (documents.Length == 0)
                return new(true);
            var label = parameters.TryGetProperty("label", out var name) && name.ValueKind == JsonValueKind.String &&
                name.GetString() is { Length: > 0 and <= 256 } saved ? saved : server.Name ?? server.ProfileId;
            var review = new LanguageServerEditReview(label, server, documents, cancellation);
            return await PublishServerEditAsync(review, cancellation).ConfigureAwait(false);
        }
        catch (LanguageDocumentChangedException)
        {
            return new(false, "A workspace edit target has changed.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return new(false, "The workspace edit review expired or the server closed.");
        }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(_logger, error, "Validate server-originated workspace edit");
            return new(false, "The workspace edit includes an unsupported target, range, or operation.");
        }
    }

    private async Task<LanguageServerEditReply> PublishServerEditAsync(LanguageServerEditReview review, CancellationToken token)
    {
        using var cancellation = token.Register(review.Cancel);
        lock (_serverEditGate)
        {
            if (!review.IsPending || !IsServerEditCurrent(review))
                return new(false, "The requesting language server has changed.");
            if (_serverEdits.Count >= MaximumPendingServerEdits)
                return new(false, "The workbench already has too many pending server edits.");
            _serverEdits.Add(review.Id, review);
        }
        try
        {
            NotifyChanged();
            return await review.Reply.ConfigureAwait(false);
        }
        finally
        {
            lock (_serverEditGate)
                _serverEdits.Remove(review.Id);
            NotifyChanged();
        }
    }
}
