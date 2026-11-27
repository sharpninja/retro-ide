using System.Globalization;
using System.Text.Json;

namespace RetroIde.LspFixture;

internal sealed partial class Program
{
    private string? _workspaceEditPeer;
    private string? _workspaceEditMode;
    private bool _workspaceEditSent;

    private void ReadWorkspaceEditFixture(JsonElement options, JsonElement parameters)
    {
        if (options.ValueKind != JsonValueKind.Object || !options.TryGetProperty("serverEdit", out var mode) || mode.ValueKind != JsonValueKind.String)
            return;
        _workspaceEditMode = mode.GetString();
        if (options.TryGetProperty("serverEditPeer", out var peer) && peer.ValueKind == JsonValueKind.String)
            _workspaceEditPeer = peer.GetString();

        var advertised = parameters.GetProperty("capabilities").TryGetProperty("workspace", out var workspace) &&
            workspace.TryGetProperty("applyEdit", out var applyEdit) && applyEdit.ValueKind == JsonValueKind.True;
        Trace("apply-edit-client-capability", advertised.ToString(CultureInfo.InvariantCulture));
    }

    private void RequestWorkspaceEdit(JsonElement parameters)
    {
        if (_workspaceEditSent || _workspaceEditMode is null)
            return;
        _workspaceEditSent = true;
        var uri = DocumentUri(parameters);
        var document = _documents[uri];
        var index = document.Text.IndexOf("value", StringComparison.Ordinal);
        var range = new { start = EncodedPosition(document.Text, index), end = EncodedPosition(document.Text, index + "value".Length) };
        var changes = new List<object>
        {
            new
            {
                textDocument = new { uri, version = _workspaceEditMode is "invalid-version" ? document.Version + 1 : document.Version },
                edits = new[] { new { range, newText = "server_value" } },
            },
        };
        if (_workspaceEditPeer is { } peer)
        {
            var point = new { line = 0, character = 0 };
            changes.Add(new
            {
                textDocument = new { uri = peer, version = (int?)null },
                edits = new[] { new { range = new { start = point, end = point }, newText = "// server edit\n" } },
            });
        }

        if (_workspaceEditMode is "resource" && _workspaceEditPeer is { } created)
            changes.Add(new { kind = "create", uri = created });

        const string requestId = "fixture-workspace-edit";
        connection.Send(new { jsonrpc = "2.0", id = requestId, method = "workspace/applyEdit",
            @params = new { label = "Fixture proposed edits", edit = new { documentChanges = changes } } });
        if (_workspaceEditMode is "cancelled")
            connection.Send(new { jsonrpc = "2.0", method = "$/cancelRequest", @params = new { id = requestId } });
        Trace("apply-edit-requested", uri);
    }

    private void ObserveWorkspaceEditResponse(JsonElement packet)
    {
        if (packet.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String &&
            string.Equals(id.GetString(), "fixture-workspace-edit", StringComparison.Ordinal))
            Trace("apply-edit-response", packet.GetRawText());
    }
}
