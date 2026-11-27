using System.Text.Json;

namespace RetroIde.Application.LanguageServers;

internal sealed partial class LanguageServerSession
{
    // Protected by _updates. Reopening a URI on the same transport must not reuse an old version.
    private readonly Dictionary<string, int> _documentVersions = new(StringComparer.Ordinal);

    private int NextDocumentVersion(string uri)
    {
        var version = checked(_documentVersions.GetValueOrDefault(uri) + 1);
        _documentVersions[uri] = version;
        return version;
    }

    private async Task<OpenDocument> SynchronizeCoreAsync(string uri, LanguageDocument source, string language,
        CancellationToken token)
    {
        if (!IsRunning)
            throw new IOException("The language server session is closed.");

        _documents.TryGetValue(uri, out var previous);
        if (previous is not null && previous.Context.DocumentId != source.Context.DocumentId)
        {
            await CloseCoreAsync(uri, token).ConfigureAwait(false);
            previous = null;
        }
        if (previous is null)
            return await OpenCoreAsync(uri, source, language, token).ConfigureAwait(false);
        if (previous.Context.Revision > source.Context.Revision)
            throw new LanguageDocumentChangedException();

        var updated = previous;
        if (previous.Context.Revision != source.Context.Revision)
        {
            if (_change == 0 && _openClose)
            {
                await CloseCoreAsync(uri, token).ConfigureAwait(false);
                return await OpenCoreAsync(uri, source, language, token).ConfigureAwait(false);
            }
            if (_change == 0 && source.Context.Revision != source.SavedRevision)
                throw new NotSupportedException("The language server cannot synchronize this unsaved document.");

            updated = new OpenDocument(source.Context, NextDocumentVersion(uri), source.Text, previous.SavedRevision);
            _diagnostics.TryRemove(uri, out _);
            _documents[uri] = updated;
            if (_change != 0)
            {
                var change = _change == 2
                    ? LspPosition.IncrementalChange(previous.Text, updated.Text, Encoding)
                    : new { text = updated.Text };
                try
                {
                    await _rpc.NotifyAsync("textDocument/didChange", new
                    {
                        textDocument = new { uri, version = updated.Version }, contentChanges = new[] { change },
                    }, token).ConfigureAwait(false);
                }
                catch
                {
                    _documents[uri] = previous;
                    _diagnostics.TryRemove(uri, out _);
                    updated.CancelRequests();
                    throw;
                }
            }
            previous.CancelRequests();
        }

        if (updated.SavedRevision < source.SavedRevision)
        {
            if (_save)
            {
                object parameters = _includeSaveText
                    ? new { textDocument = new { uri }, text = source.SavedText }
                    : new { textDocument = new { uri } };
                await _rpc.NotifyAsync("textDocument/didSave", parameters, token).ConfigureAwait(false);
            }
            updated = updated with { SavedRevision = source.SavedRevision };
            _documents[uri] = updated;
        }
        return updated;
    }

    private async Task<OpenDocument> OpenCoreAsync(string uri, LanguageDocument source, string language, CancellationToken token)
    {
        if (!_openClose && _change == 0 && source.Context.Revision != source.SavedRevision)
            throw new NotSupportedException("The language server cannot synchronize this unsaved document.");
        var tracked = new OpenDocument(source.Context, NextDocumentVersion(uri), source.Text, source.SavedRevision);
        _documents[uri] = tracked;
        if (_openClose)
        {
            try
            {
                await _rpc.NotifyAsync("textDocument/didOpen", new
                {
                    textDocument = new { uri, languageId = language, version = tracked.Version, text = tracked.Text },
                }, token).ConfigureAwait(false);
            }
            catch
            {
                _documents.TryRemove(uri, out _);
                _diagnostics.TryRemove(uri, out _);
                tracked.CancelRequests();
                throw;
            }
        }
        return tracked;
    }

    private async Task CloseCoreAsync(string uri, CancellationToken token)
    {
        if (_documents.TryRemove(uri, out var closed))
            closed.CancelRequests();
        _diagnostics.TryRemove(uri, out _);
        if (_openClose)
            await _rpc.NotifyAsync("textDocument/didClose", new { textDocument = new { uri } }, token).ConfigureAwait(false);
    }

    private void OnNotification(string method, JsonElement parameters)
    {
        if (!string.Equals(method, "textDocument/publishDiagnostics", StringComparison.Ordinal) ||
            parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty("uri", out var uriMember) ||
            uriMember.ValueKind != JsonValueKind.String || uriMember.GetString() is not { } uri ||
            !parameters.TryGetProperty("diagnostics", out var items) || items.ValueKind != JsonValueKind.Array ||
            !_documents.TryGetValue(uri, out var current))
            return;

        if (parameters.TryGetProperty("version", out var version) && version.ValueKind != JsonValueKind.Null &&
            (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var expected) || expected != current.Version))
            return;

        _diagnostics[uri] = new DiagnosticSet(current.Context, items.EnumerateArray().Select(item => item.Clone()).ToArray());
        _stateChanged();
    }

    private (bool Handled, object? Result) OnRequest(string method, JsonElement parameters) => method switch
    {
        "workspace/workspaceFolders" => (true, Folders()),
        "workspace/configuration" when parameters.ValueKind == JsonValueKind.Object &&
            parameters.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array =>
            (true, items.EnumerateArray().Select(ReadSetting).ToArray()),
        "client/registerCapability" => (ChangeRegistration(parameters, remove: false), null),
        "client/unregisterCapability" => (ChangeRegistration(parameters, remove: true), null),
        _ => (false, null),
    };

    private JsonElement? ReadSetting(JsonElement item)
    {
        if (_configuration.Settings is not { } settings)
            return null;
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("section", out var section) ||
            section.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(section.GetString()))
            return settings;
        foreach (var name in section.GetString()!.Split('.'))
        {
            if (settings.ValueKind != JsonValueKind.Object || !settings.TryGetProperty(name, out settings))
                return null;
        }
        return settings;
    }
}
