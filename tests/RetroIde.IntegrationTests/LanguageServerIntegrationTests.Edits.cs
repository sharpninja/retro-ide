using System.Text.Json;
using Avalonia.Headless.XUnit;
using RetroIde.Application;
using RetroIde.Application.Workspaces;
using RetroIde.Workbench;
using Xunit;
using Connection = RetroIde.IntegrationTests.GitWorkspaceStateTests.WorkspaceConnection;

namespace RetroIde.IntegrationTests;

public sealed partial class LanguageServerIntegrationTests
{
    /// <summary>Applies versioned rename to multiple dirty buffers and rejects the entire plan after a target changes.</summary>
    /// <remarks>TEST-RETRO-014#ac-test-language-features; TEST-RETRO-014#ac-test-language-documents.</remarks>
    [AvaloniaFact]
    public async Task Workspace_rename_previews_all_targets_and_rejects_a_stale_peer()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = deadline.Token;
        var fixture = Directory.CreateTempSubdirectory("retro-ide-lsp-rename-");
        try
        {
            const string text = "/* é😀 */ int value;\r\n";
            var primary = Path.Combine(fixture.FullName, "fixture.c");
            var peer = Path.Combine(fixture.FullName, "peer.c");
            await File.WriteAllTextAsync(primary, text, token);
            await File.WriteAllTextAsync(peer, text, token);
            await using var session = new WorkbenchSession(Guid.NewGuid());
            await using var client = await Connection.ConnectAsync(session, token);
            var root = await client.AddRootAsync(fixture.FullName);
            var first = await OpenAsync(client, root);
            var second = (await client.ChangeAsync("workspace_open_file", new(StringComparer.Ordinal)
                { ["rootId"] = root, ["relativePath"] = "peer.c" })).GetProperty("data");
            await ConfigureAsync(client, root, new { navigation = true }, encoding: "utf-8");
            var offset = text.IndexOf("value", StringComparison.Ordinal);

            var preview = await RenameAsync(client, first, offset, "renamed");
            Assert.Equal(2, preview.GetProperty("targets").GetArrayLength());
            Assert.Equal(text, TextOf(session, first));

            var peerEdited = (await client.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
            {
                ["document"] = second, ["offset"] = text.Length, ["length"] = 0, ["text"] = "// keep this change\r\n",
            })).GetProperty("data");
            var stale = ApplyArguments(first, preview);
            stale["context"] = (await client.CallAsync("workbench_get_context")).GetProperty("context");
            Assert.Equal("document_changed", Code(await client.CallAsync("language_apply_edit", stale)));
            Assert.Equal(text, TextOf(session, first));
            Assert.EndsWith("// keep this change\r\n", TextOf(session, peerEdited), StringComparison.Ordinal);

            await CompleteAsync(client, peerEdited, offset);
            var fresh = await RenameAsync(client, first, offset, "renamed");
            var applied = await client.ChangeAsync("language_apply_edit", ApplyArguments(first, fresh));
            var documents = applied.GetProperty("data").EnumerateArray().ToDictionary(
                document => document.GetProperty("relativePath").GetString()!, StringComparer.Ordinal);
            Assert.Equal("/* é😀 */ int renamed;\r\n", TextOf(session, documents["fixture.c"]));
            Assert.Equal("/* é😀 */ int renamed;\r\n// keep this change\r\n", TextOf(session, documents["peer.c"]));
            Assert.Equal(text, await File.ReadAllTextAsync(primary, token));
            Assert.Equal(text, await File.ReadAllTextAsync(peer, token));
        }
        finally { fixture.Delete(recursive: true); }
    }

    /// <summary>Previews range formatting and resolves a server code action before applying it for one undoable edit.</summary>
    /// <remarks>TEST-RETRO-014#ac-test-language-features.</remarks>
    [AvaloniaFact]
    public async Task Formatting_and_code_actions_apply_reviewed_text_and_preserve_undo()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = deadline.Token;
        var fixture = Directory.CreateTempSubdirectory("retro-ide-lsp-actions-");
        try
        {
            const string saved = "\r\n/* é😀 */ int value;\r\n";
            var source = Path.Combine(fixture.FullName, "fixture.c");
            await File.WriteAllTextAsync(source, saved, token);
            await using var session = new WorkbenchSession(Guid.NewGuid());
            await using var client = await Connection.ConnectAsync(session, token);
            var root = await client.AddRootAsync(fixture.FullName);
            var document = await OpenAsync(client, root);
            await ConfigureAsync(client, root, new { navigation = true }, encoding: "utf-32");
            var offset = saved.IndexOf("value", StringComparison.Ordinal);

            var span = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["document"] = document, ["offset"] = offset, ["endOffset"] = offset + 5,
            };
            var formatting = await client.CallAsync("language_format", span);
            Assert.Equal("ok", Code(formatting));
            var formatted = SingleChangedDocument(await client.ChangeAsync("language_apply_edit", ApplyArguments(document, formatting.GetProperty("data"))));
            Assert.Equal(saved.Replace("value", "formatted", StringComparison.Ordinal), TextOf(session, formatted));
            var undo = await client.ChangeAsync("editor_undo", new(StringComparer.Ordinal) { ["document"] = formatted });
            var restored = undo.GetProperty("data");
            Assert.Equal(saved, TextOf(session, restored));

            span["document"] = restored;
            var actions = await client.CallAsync("language_code_actions", span);
            Assert.Equal("ok", Code(actions));
            var offer = actions.GetProperty("data");
            Assert.Single(offer.GetProperty("actions").EnumerateArray());
            var preview = await client.CallAsync("language_preview_code_action", new(StringComparer.Ordinal)
            {
                ["document"] = restored, ["offerId"] = offer.GetProperty("offerId"), ["index"] = 0,
            });
            Assert.Equal("ok", Code(preview));
            var fixedDocument = SingleChangedDocument(await client.ChangeAsync("language_apply_edit", ApplyArguments(restored, preview.GetProperty("data"))));
            Assert.Equal(saved.Replace("value", "fixed", StringComparison.Ordinal), TextOf(session, fixedDocument));
            Assert.Equal(saved, await File.ReadAllTextAsync(source, token));
        }
        finally { fixture.Delete(recursive: true); }
    }

    private static async Task<JsonElement> RenameAsync(Connection client, JsonElement document, int offset, string name)
    {
        var result = await client.CallAsync("language_rename", new(StringComparer.Ordinal)
        {
            ["document"] = document, ["offset"] = offset, ["newName"] = name,
        });
        Assert.Equal("ok", Code(result));
        return result.GetProperty("data");
    }

    /// <summary>Checks workspace confinement and detects on-disk changes before opening and editing a closed target.</summary>
    /// <remarks>TEST-RETRO-014#ac-test-language-features; TEST-RETRO-014#ac-test-language-documents.</remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rename_validates_closed_targets_and_rejects_files_outside_the_workspace(bool inside)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        var token = deadline.Token;
        var fixture = Directory.CreateTempSubdirectory("retro-ide-lsp-closed-edit-");
        try
        {
            const string text = "int value;\n";
            const string Peer = "closed.c";
            var workspace = Directory.CreateDirectory(Path.Combine(fixture.FullName, "workspace")).FullName;
            var target = Path.Combine(inside ? workspace : fixture.FullName, Peer);
            await File.WriteAllTextAsync(Path.Combine(workspace, "fixture.c"), text, token);
            await File.WriteAllTextAsync(target, text, token);
            await using var session = new WorkbenchSession(Guid.NewGuid());
            await using var client = await Connection.ConnectAsync(session, token);
            var root = await client.AddRootAsync(workspace);
            var document = await OpenAsync(client, root);
            await ConfigureAsync(client, root, new { navigation = true, renameExtraUri = new Uri(target).AbsoluteUri });

            var operation = await client.CallAsync("language_rename", new(StringComparer.Ordinal)
            {
                ["document"] = document, ["offset"] = 4, ["newName"] = "renamed",
            });
            Assert.Equal(inside ? "ok" : "invalid_workspace_edit", Code(operation));
            Assert.Equal(text, TextOf(session, document));
            if (!inside)
                return;

            const string ChangedText = "/* disk change */ int value;\n";
            await File.WriteAllTextAsync(target, ChangedText, token);
            var stale = ApplyArguments(document, operation.GetProperty("data"));
            stale["context"] = (await client.CallAsync("workbench_get_context")).GetProperty("context");
            Assert.Equal("document_changed", Code(await client.CallAsync("language_apply_edit", stale)));
            Assert.False(session.Documents.HasDocument(new(root, Peer)));
            Assert.Equal(text, TextOf(session, document));

            var fresh = await RenameAsync(client, document, 4, "renamed");
            var applied = await client.ChangeAsync("language_apply_edit", ApplyArguments(document, fresh));
            var peer = applied.GetProperty("data").EnumerateArray().Single(
                item => string.Equals(item.GetProperty("relativePath").GetString(), Peer, StringComparison.Ordinal));
            Assert.Equal("/* disk change */ int renamed;\n", TextOf(session, peer));
            Assert.Equal(ChangedText, await File.ReadAllTextAsync(target, token));
        }
        finally { fixture.Delete(recursive: true); }
    }

    private static Dictionary<string, object?> ApplyArguments(JsonElement document, JsonElement preview) =>
        new(StringComparer.Ordinal) { ["document"] = document, ["previewId"] = preview.GetProperty("previewId") };

    private static JsonElement SingleChangedDocument(JsonElement applied) => Assert.Single(applied.GetProperty("data").EnumerateArray());

    private static string TextOf(WorkbenchSession session, JsonElement context) =>
        session.Documents.ReadLanguageDocument(context.Deserialize<DocumentContext>(WireJson.Options)!)!.Text;
}
