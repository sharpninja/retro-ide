using System.Text.Json;
using Avalonia.Headless.XUnit;
using RetroIde.Workbench;
using Xunit;
using Connection = RetroIde.IntegrationTests.GitWorkspaceStateTests.WorkspaceConnection;

namespace RetroIde.IntegrationTests;

public sealed partial class LanguageServerIntegrationTests
{
    /// <summary>Applies the reference timeline assertions to the real workbench and its native LSP fixture.</summary>
    /// <remarks>2026-11-26. TEST-RETRO-014#ac-test-language-documents. The negotiated synchronization mode is checked by the real server fixture, which reports the text and version observed on stdio.</remarks>
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Live_versions_match_the_independent_document_timeline(int syncKind)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = deadline.Token;
        var fixture = Directory.CreateTempSubdirectory("retro-language-timeline-");
        try
        {
            var path = Path.Combine(fixture.FullName, "fixture.c");
            await File.WriteAllTextAsync(path, Phase03LanguageVersionContractTests.OriginalText, token).ConfigureAwait(true);
            await using var session = new WorkbenchSession(Guid.NewGuid());
            await using var connection = await Connection.ConnectAsync(session, token).ConfigureAwait(true);
            var root = await connection.AddRootAsync(fixture.FullName).ConfigureAwait(true);
            var document = await OpenAsync(connection, root).ConfigureAwait(true);
            await ConfigureAsync(connection, root, new { syncKind, traceDirectory = fixture.FullName }).ConfigureAwait(true);
            var probe = new LiveVersionProbe(connection, path, root, document);
            await Phase03LanguageVersionContractTests.VerifyTimelineAsync(probe, token).ConfigureAwait(true);
        }
        finally { fixture.Delete(recursive: true); }
    }

    private sealed class LiveVersionProbe(Connection client, string file, Guid root, JsonElement document)
        : Phase03LanguageVersionContractTests.IVersionProbe
    {
        private JsonElement _document = document;
        private string _currentText = Phase03LanguageVersionContractTests.OriginalText;

        public async Task<Phase03LanguageVersionContractTests.ServerDocument> ReadServerAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            const int Offset = 4;
            var reply = await CompleteAsync(client, _document, Offset).ConfigureAwait(false);
            var completion = reply.GetProperty("result")[0];
            var fields = completion.GetProperty("data");
            var text = completion.GetProperty("detail").GetString();
            Assert.NotNull(text);
            Assert.Equal(Offset, fields.GetProperty("offset").GetInt32());
            return new(fields.GetProperty("version").GetInt32(), text);
        }

        public Task<string> ReadDiskAsync(CancellationToken token) => File.ReadAllTextAsync(file, token);

        public async Task EditAsync(string text, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var changed = await client.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
            {
                ["document"] = _document, ["offset"] = 0, ["length"] = _currentText.Length, ["text"] = text,
            }).ConfigureAwait(false);
            _document = changed.GetProperty("data");
            _currentText = text;
        }

        public async Task DiscardAndReopenAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await client.ChangeAsync("editor_close_document", new(StringComparer.Ordinal)
            {
                ["document"] = _document, ["discardChanges"] = true,
            }).ConfigureAwait(false);
            _document = await OpenAsync(client, root).ConfigureAwait(false);
            _currentText = await ReadDiskAsync(token).ConfigureAwait(false);
        }

        public async Task SaveAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await client.ChangeAsync("editor_save_document", new(StringComparer.Ordinal)
            {
                ["document"] = _document,
            }).ConfigureAwait(false);
        }

        public async Task<string> ReadServerSaveAsync(CancellationToken token)
        {
            var trace = Path.Combine(Path.GetDirectoryName(file)!, "did-save");
            await WaitForFileAsync(trace, token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(trace, token).ConfigureAwait(false));
            var text = document.RootElement.GetProperty("text").GetString();
            Assert.NotNull(text);
            return text;
        }
    }
}
