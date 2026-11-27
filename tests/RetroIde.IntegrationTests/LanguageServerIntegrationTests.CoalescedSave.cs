using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RetroIde.Application.LanguageServers;
using RetroIde.Application.Workspaces;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class LanguageServerIntegrationTests
{
    /// <remarks>TEST-RETRO-014#ac-test-language-documents. The LSP session sees a saved snapshot before the intermediate edit.</remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reopen_synchronization_reports_the_coalesced_save_once(bool dirtyAfterSave)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var fixture = Directory.CreateTempSubdirectory("retro-coalesced-save-");
        try
        {
            var root = Guid.NewGuid();
            var options = new { syncKind = 0, traceDirectory = fixture.FullName };
            var arguments = ConfigurationArguments(root, options);
            var configuration = new LanguageServerConfiguration(root, fixture.FullName, (string)arguments["executable"]!,
                (string[])arguments["arguments"]!, [new("c", [".c"])], JsonSerializer.SerializeToElement(options), null);
            var started = await LanguageServerSession.StartAsync([configuration], NullLogger.Instance, static () => { },
                static (_, _, _) => Task.FromResult(new LanguageServerEditReply(false)), token).ConfigureAwait(true);
            await using var session = started.ConfigureAwait(true);
            var context = new DocumentContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), root, "fixture.c", 1);
            var initial = new LanguageDocument(context, Path.Combine(fixture.FullName, context.RelativePath), "int first;", 1);
            await started.SynchronizeAsync(initial, "c", static () => true, token).ConfigureAwait(true);

            const string Saved = "int saved;";
            var latest = initial with
            {
                Context = context with { Revision = dirtyAfterSave ? 3 : 2 },
                Text = dirtyAfterSave ? "int dirty;" : Saved,
                SavedRevision = 2,
            };
            await started.SaveDocumentAsync(latest, "c", Saved, token).ConfigureAwait(true);
            var completion = new LanguageRequest("textDocument/completion", Offset: 0);
            var result = await started.QueryAsync(latest, configuration, "c", completion, static () => true, token).ConfigureAwait(true);
            Assert.Equal(latest.Text, result.Result[0].GetProperty("detail").GetString(), StringComparer.Ordinal);
            Assert.Equal(2, result.Result[0].GetProperty("data").GetProperty("version").GetInt32());

            var trace = Path.Combine(fixture.FullName, "did-save");
            Assert.True(File.Exists(trace), "The server did not receive didSave before replying to completion.");
            using var save = JsonDocument.Parse(await File.ReadAllTextAsync(trace, token).ConfigureAwait(true));
            Assert.Equal(Saved, save.RootElement.GetProperty("text").GetString(), StringComparer.Ordinal);
            Assert.Equal(new Uri(initial.Path).AbsoluteUri,
                save.RootElement.GetProperty("textDocument").GetProperty("uri").GetString(), StringComparer.Ordinal);

            File.Delete(trace);
            _ = await started.QueryAsync(latest, configuration, "c", completion, static () => true, token).ConfigureAwait(true);
            Assert.False(File.Exists(trace), "The same saved revision produced a duplicate didSave.");
        }
        finally { fixture.Delete(recursive: true); }
    }
}
