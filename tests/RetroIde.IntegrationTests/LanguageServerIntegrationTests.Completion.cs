using System.Text.Json;
using Avalonia.Headless.XUnit;
using RetroIde.Workbench;
using Xunit;
using Connection = RetroIde.IntegrationTests.GitWorkspaceStateTests.WorkspaceConnection;

namespace RetroIde.IntegrationTests;

public sealed partial class LanguageServerIntegrationTests
{
    /// <summary>Offers a plain-text completion and validates its captured buffer before applying one undoable change.</summary>
    /// <remarks>TEST-RETRO-014#ac-test-language-features; TEST-RETRO-014#ac-test-language-documents.</remarks>
    [AvaloniaFact]
    public async Task Completion_preview_is_bound_to_the_original_unsaved_revision()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = deadline.Token;
        var fixture = Directory.CreateTempSubdirectory("retro-ide-completion-review-");
        try
        {
            const string saved = "/* é😀 */ int value;\r\n";
            var path = Path.Combine(fixture.FullName, "fixture.c");
            await File.WriteAllTextAsync(path, saved, token);
            await using var session = new WorkbenchSession(Guid.NewGuid());
            await using var client = await Connection.ConnectAsync(session, token);
            var root = await client.AddRootAsync(fixture.FullName);
            var document = await OpenAsync(client, root);
            await ConfigureAsync(client, root);
            var offset = saved.IndexOf("value", StringComparison.Ordinal) + 3;
            var offer = await CompleteAsync(client, document, offset);
            Assert.Equal("language_completion_unavailable", Code(await PreviewAsync(client, document, offer, -1)));

            var changed = (await client.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
            {
                ["document"] = document, ["offset"] = saved.Length, ["length"] = 0, ["text"] = "// unsaved\r\n",
            })).GetProperty("data");
            Assert.Equal("document_changed", Code(await PreviewAsync(client, changed, offer, 0)));
            var fresh = await CompleteAsync(client, changed, offset);
            var result = await PreviewAsync(client, changed, fresh, 0);
            Assert.Equal("ok", Code(result));
            var preview = result.GetProperty("data");
            var edit = preview.GetProperty("targets")[0].GetProperty("edits")[0];
            Assert.Equal("val", edit.GetProperty("previousText").GetString());
            Assert.Equal("fixture completion", edit.GetProperty("newText").GetString());

            var applied = SingleChangedDocument(await client.ChangeAsync("language_apply_edit", ApplyArguments(changed, preview)));
            Assert.Equal("/* é😀 */ int fixture completionue;\r\n// unsaved\r\n", TextOf(session, applied));
            var undo = await client.ChangeAsync("editor_undo", new(StringComparer.Ordinal) { ["document"] = applied });
            Assert.Equal(saved + "// unsaved\r\n", TextOf(session, undo.GetProperty("data")));
            Assert.Equal(saved, await File.ReadAllTextAsync(path, token));
        }
        finally { fixture.Delete(recursive: true); }
    }

    private static Task<JsonElement> PreviewAsync(Connection client, JsonElement document, JsonElement offer, int index) =>
        client.CallAsync("language_preview_completion", new(StringComparer.Ordinal)
        {
            ["document"] = document, ["offerId"] = offer.GetProperty("completionOfferId"), ["index"] = index,
        });
}
