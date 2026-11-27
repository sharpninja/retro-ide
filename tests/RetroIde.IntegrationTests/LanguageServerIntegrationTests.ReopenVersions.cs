using System.Text.Json;
using Avalonia.Headless.XUnit;
using RetroIde.Workbench;
using Xunit;
using Connection = RetroIde.IntegrationTests.GitWorkspaceStateTests.WorkspaceConnection;

namespace RetroIde.IntegrationTests;

public sealed partial class LanguageServerIntegrationTests
{
    /// <summary>Verifies close/open, full and incremental synchronization, URI versions and save payloads.</summary>
    /// <remarks>TEST-RETRO-014#ac-test-language-documents.</remarks>
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Synchronization_and_reopening_preserve_monotonic_uri_versions(int syncKind)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = deadline.Token;
        var fixture = Directory.CreateTempSubdirectory("retro-ide-language-versions-");
        try
        {
            const string disk = "int source;\n";
            const string dirty = "int changed;\n";
            var path = Path.Combine(fixture.FullName, "fixture.c");
            await File.WriteAllTextAsync(path, disk, token);
            await using var session = new WorkbenchSession(Guid.NewGuid());
            await using var client = await Connection.ConnectAsync(session, token);
            var root = await client.AddRootAsync(fixture.FullName);
            var first = await OpenAsync(client, root);
            await ConfigureAsync(client, root, new { syncKind, traceDirectory = fixture.FullName });
            AssertFixtureReply(await CompleteAsync(client, first, 4), disk, 4, expectedVersion: 1);

            var edit = (await client.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
            {
                ["document"] = first, ["offset"] = 0, ["length"] = disk.Length, ["text"] = dirty,
            })).GetProperty("data");
            AssertFixtureReply(await CompleteAsync(client, edit, 4), dirty, 4, expectedVersion: 2);

            await client.ChangeAsync("editor_close_document", new(StringComparer.Ordinal)
            {
                ["document"] = edit, ["discardChanges"] = true,
            });
            var reopened = await OpenAsync(client, root);
            AssertFixtureReply(await CompleteAsync(client, reopened, 4), disk, 4, expectedVersion: 3);
            Assert.Equal(disk, await File.ReadAllTextAsync(path, token));
            var editedAgain = (await client.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
            {
                ["document"] = reopened, ["offset"] = 0, ["length"] = disk.Length, ["text"] = dirty,
            })).GetProperty("data");
            AssertFixtureReply(await CompleteAsync(client, editedAgain, 4), dirty, 4, expectedVersion: 4);
            await client.ChangeAsync("editor_save_document", new(StringComparer.Ordinal) { ["document"] = editedAgain });
            var saveTrace = Path.Combine(fixture.FullName, "did-save");
            await WaitForFileAsync(saveTrace, token);
            using var save = JsonDocument.Parse(await File.ReadAllTextAsync(saveTrace, token));
            Assert.Equal(dirty, save.RootElement.GetProperty("text").GetString());
            Assert.Equal(dirty, await File.ReadAllTextAsync(path, token));
        }
        finally { fixture.Delete(recursive: true); }
    }
}
