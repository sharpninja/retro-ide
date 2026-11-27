using System.Text.Json;
using Avalonia.Headless.XUnit;
using RetroIde.Workbench;
using Xunit;
using Connection = RetroIde.IntegrationTests.GitWorkspaceStateTests.WorkspaceConnection;

namespace RetroIde.IntegrationTests;

public sealed partial class LanguageServerIntegrationTests
{
    /// <remarks>TEST-RETRO-014#ac-test-language-documents.</remarks>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Committed_save_reaches_the_server_when_initial_sync_or_close_overtakes_it(bool close)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = deadline.Token;
        var fixture = Directory.CreateTempSubdirectory("retro-ide-queued-save-");
        var release = Path.Combine(fixture.FullName, "release-large-frame");
        var path = Path.Combine(fixture.FullName, "fixture.c");
        var uri = new Uri(path).AbsoluteUri;
        const string Initial = "int initial;\n";
        const string Saved = "int saved;\n";
        try
        {
            await File.WriteAllTextAsync(path, Initial, token);
            await File.WriteAllTextAsync(Path.Combine(fixture.FullName, "anchor.c"), new string(' ', 3 * 1024 * 1024), token);
            await using var session = new WorkbenchSession(Guid.NewGuid());
            await using var client = await Connection.ConnectAsync(session, token);
            var root = await client.AddRootAsync(fixture.FullName);
            await ConfigureAsync(client, root, new { syncKind = 1, traceDirectory = fixture.FullName, pauseLargeFrame = true });
            var document = close ? await OpenAsync(client, root) : default;
            if (close)
                AssertFixtureReply(await CompleteAsync(client, document, 0), Initial, 0, expectedVersion: 1);

            try
            {
                await client.ChangeAsync("workspace_open_file", new(StringComparer.Ordinal)
                    { ["rootId"] = root, ["relativePath"] = "anchor.c" });
                await WaitForFileAsync(Path.Combine(fixture.FullName, "large-frame-held"), token);
                if (!close)
                    document = await OpenAsync(client, root);
                var edited = (await client.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
                    { ["document"] = document, ["offset"] = 0, ["length"] = Initial.Length, ["text"] = Saved })).GetProperty("data");
                document = (await client.ChangeAsync("editor_save_document", new(StringComparer.Ordinal)
                    { ["document"] = edited })).GetProperty("data");
                Assert.Equal(Saved, await File.ReadAllTextAsync(path, token), StringComparer.Ordinal);
                if (close)
                    await client.ChangeAsync("editor_close_document", new(StringComparer.Ordinal)
                        { ["document"] = document, ["discardChanges"] = false });
            }
            finally { File.WriteAllText(release, ""); }

            if (close)
                await WaitForFileAsync(Path.Combine(fixture.FullName, "did-close"), token);
            else
                AssertFixtureReply(await CompleteAsync(client, document, 0), Saved, 0, expectedVersion: 1);

            await AssertQueuedSaveTraceAsync(fixture.FullName, uri, Saved, close, token);
        }
        finally { fixture.Delete(recursive: true); }
    }

    private static async Task AssertQueuedSaveTraceAsync(string fixture, string uri, string savedText, bool close, CancellationToken token)
    {
        var lines = await File.ReadAllLinesAsync(Path.Combine(fixture, "document-timeline"), token).ConfigureAwait(false);
        var actual = lines.Where(line => line.EndsWith(" " + uri, StringComparison.Ordinal));
        string[] expected = close ? [$"open {uri}", $"save {uri}", $"close {uri}"] : [$"open {uri}", $"save {uri}"];
        Assert.Equal(expected, actual);
        using var save = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture, "did-save"), token).ConfigureAwait(false));
        Assert.Equal(savedText, save.RootElement.GetProperty("text").GetString(), StringComparer.Ordinal);
    }
}
