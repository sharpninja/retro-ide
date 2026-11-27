using System.Text.Json;
using Avalonia.Headless.XUnit;
using RetroIde.Workbench;
using Xunit;
using Connection = RetroIde.IntegrationTests.GitWorkspaceStateTests.WorkspaceConnection;

namespace RetroIde.IntegrationTests;

public sealed partial class LanguageServerIntegrationTests
{
    /// <summary>Reads diagnostics for separate workspace roots and invalidates a selected location when its document changes.</summary>
    /// <remarks>TEST-RETRO-014#ac-test-language-documents; TEST-RETRO-014#ac-test-language-features.</remarks>
    [AvaloniaFact]
    public async Task Problems_follow_live_documents_across_roots_and_reject_stale_navigation()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = deadline.Token;
        var fixture = Directory.CreateTempSubdirectory("retro-ide-problems-");
        try
        {
            const string text = "ERROR\r\nint value;\r\n";
            var one = Directory.CreateDirectory(Path.Combine(fixture.FullName, "one"));
            var two = Directory.CreateDirectory(Path.Combine(fixture.FullName, "two"));
            await File.WriteAllTextAsync(Path.Combine(one.FullName, "fixture.c"), text, token);
            await File.WriteAllTextAsync(Path.Combine(two.FullName, "fixture.c"), text, token);
            await using var session = new WorkbenchSession(Guid.NewGuid());
            await using var client = await Connection.ConnectAsync(session, token);
            var firstRoot = await client.AddRootAsync(one.FullName);
            var secondRoot = await client.AddRootAsync(two.FullName);
            var first = await OpenAsync(client, firstRoot);
            var second = await OpenAsync(client, secondRoot);
            await ConfigureAsync(client, firstRoot);
            await ConfigureAsync(client, secondRoot);

            var items = await WaitForProblemsAsync(client, 2, token);
            await using var stdio = await client.ConnectStdioAsync(Path.Combine(fixture.FullName, "lsp-problems-stdio.json"));
            var same = await client.CallAsync("language_list_problems", transport: stdio);
            Assert.Equal("ok", Code(same));
            Assert.Equal(items.Select(item => item.GetRawText()),
                same.GetProperty("data").GetProperty("items").EnumerateArray().Select(item => item.GetRawText()), StringComparer.Ordinal);
            Assert.All(items, item => Assert.Equal("Fixture error marker.", item.GetProperty("message").GetString()));
            Assert.Equal(2, items.Select(item => item.GetProperty("sessionId").GetGuid()).Distinct().Count());
            var problem = Assert.Single(items.Where(item => item.GetProperty("document").GetProperty("rootId").GetGuid() == firstRoot));
            var navigation = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["document"] = first, ["sessionId"] = problem.GetProperty("sessionId"), ["target"] = problem.GetProperty("location"),
            };
            var opened = await client.ChangeAsync("language_open_location", navigation);
            Assert.Equal(0, opened.GetProperty("data").GetProperty("offset").GetInt32());

            await client.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
                { ["document"] = first, ["offset"] = 0, ["length"] = 5, ["text"] = "CLEAN" });
            navigation["context"] = (await client.CallAsync("workbench_get_context")).GetProperty("context");
            Assert.Equal("document_changed", Code(await client.CallAsync("language_open_location", navigation)));
            var surviving = Assert.Single(await WaitForProblemsAsync(client, 1, token));
            Assert.Equal(second.GetProperty("viewKey").GetGuid(), surviving.GetProperty("document").GetProperty("viewKey").GetGuid());

            await client.ChangeAsync("editor_close_document", new(StringComparer.Ordinal) { ["document"] = second });
            Assert.Empty(await WaitForProblemsAsync(client, 0, token));
        }
        finally { fixture.Delete(recursive: true); }
    }

    private static async Task<JsonElement[]> WaitForProblemsAsync(Connection client, int count, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var response = await client.CallAsync("language_list_problems");
            Assert.Equal("ok", Code(response));
            var data = response.GetProperty("data");
            Assert.False(data.GetProperty("truncated").GetBoolean());
            var items = data.GetProperty("items").EnumerateArray().ToArray();
            if (items.Length == count)
                return items;
            await Task.Delay(25, token);
        }
    }
}
