using System.Text.Json;
using Avalonia.Headless.XUnit;
using RetroIde.Application;
using RetroIde.Workbench;
using Xunit;
using Connection = RetroIde.IntegrationTests.GitWorkspaceStateTests.WorkspaceConnection;

namespace RetroIde.IntegrationTests;

public sealed partial class LanguageServerIntegrationTests
{
    /// <summary>Exercises server proposals over stdio and validates approval, scope, stale content and decline through MCP.</summary>
    /// <remarks>TEST-RETRO-014#ac-test-language-documents; TEST-RETRO-014#ac-test-language-features.</remarks>
    [AvaloniaTheory]
    [InlineData("apply")]
    [InlineData("decline")]
    [InlineData("stale")]
    [InlineData("stale-buffer")]
    [InlineData("foreign-root")]
    [InlineData("invalid-version")]
    [InlineData("server-stopped")]
    [InlineData("shared-root-stopped")]
    [InlineData("cancelled")]
    [InlineData("resource")]
    public async Task Server_workspace_edits_require_current_scoped_review(string scenario)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = deadline.Token;
        var fixture = Directory.CreateTempSubdirectory("retro-ide-server-edits-");
        try
        {
            const string text = "/* é😀 */ int value;\r\n";
            const string peerText = "int peer;\r\n";
            var workspace = Directory.CreateDirectory(Path.Combine(fixture.FullName, "workspace"));
            var other = Directory.CreateDirectory(Path.Combine(fixture.FullName, "other"));
            var source = Path.Combine(workspace.FullName, "fixture.c");
            var peer = Path.Combine(scenario is "foreign-root" or "shared-root-stopped" ? other.FullName : workspace.FullName, "peer.c");
            await File.WriteAllTextAsync(source, text, token);
            await File.WriteAllTextAsync(peer, peerText, token);
            await using var session = new WorkbenchSession(Guid.NewGuid());
            await using var client = await Connection.ConnectAsync(session, token);
            var root = await client.AddRootAsync(workspace.FullName);
            var additionalRoot = await client.AddRootAsync(other.FullName);
            var origin = await OpenAsync(client, root);
            var options = new
            {
                serverEdit = scenario, serverEditPeer = new Uri(peer).AbsoluteUri, traceDirectory = fixture.FullName,
                workspaceFolderMode = scenario is "shared-root-stopped" ? "dynamic" : null,
            };
            await ConfigureAsync(client, root, options);
            if (scenario is "shared-root-stopped")
                await ConfigureAsync(client, additionalRoot, options);
            Assert.Equal("True", await File.ReadAllTextAsync(Path.Combine(fixture.FullName, "apply-edit-client-capability"), token));

            // The server sends the edit request before replying to completion. The reader must keep processing messages during review.
            await CompleteAsync(client, origin, text.IndexOf("value", StringComparison.Ordinal));
            var responseFile = Path.Combine(fixture.FullName, "apply-edit-response");
            await VerifyServerReviewAsync(client, session, origin, scenario, fixture.FullName, peerText, token);
            if (scenario is not "server-stopped")
            {
                await WaitForFileAsync(responseFile, token);
                using var response = JsonDocument.Parse(await File.ReadAllTextAsync(responseFile, token));
                var reply = response.RootElement.GetProperty("result");
                Assert.Equal(scenario is "apply", reply.GetProperty("applied").GetBoolean());
                if (scenario is not "apply")
                    Assert.False(string.IsNullOrWhiteSpace(reply.GetProperty("failureReason").GetString()));
            }
            Assert.Empty(await ReadServerReviewsAsync(client));
            Assert.Equal(text, await File.ReadAllTextAsync(source, token));
            if (scenario is not "stale")
                Assert.Equal(peerText, await File.ReadAllTextAsync(peer, token));
        }
        finally { fixture.Delete(recursive: true); }
    }

    private static async Task VerifyServerReviewAsync(Connection client, WorkbenchSession session, JsonElement origin,
        string scenario, string fixture, string peerText, CancellationToken token)
    {
        if (scenario is "foreign-root" or "invalid-version" or "resource" or "cancelled")
        {
            Assert.Empty(await ReadServerReviewsAsync(client));
            return;
        }

        var offer = await WaitForServerReviewAsync(client, token);
        Assert.Equal("Fixture proposed edits", offer.GetProperty("label").GetString());
        var targets = offer.GetProperty("targets").EnumerateArray().ToArray();
        Assert.Equal(2, targets.Length);
        Assert.Equal("value", targets[0].GetProperty("edits")[0].GetProperty("previousText").GetString());
        var input = new Dictionary<string, object?>(StringComparer.Ordinal) { ["reviewId"] = offer.GetProperty("reviewId") };
        if (scenario is "server-stopped")
        {
            await client.ChangeAsync("language_stop_server", new(StringComparer.Ordinal) { ["rootId"] = origin.GetProperty("rootId") });
            return;
        }
        if (scenario is "shared-root-stopped")
        {
            await client.ChangeAsync("language_stop_server", new(StringComparer.Ordinal) { ["rootId"] = targets[1].GetProperty("rootId") });
            Assert.Contains(session.Controller.LanguageServers.List(), profile =>
                profile.RootId == origin.GetProperty("rootId").GetGuid() &&
                profile.SessionId == offer.GetProperty("server").GetProperty("sessionId").GetGuid() && profile.State is "running");
            return;
        }
        if (scenario is "decline")
        {
            await client.ChangeAsync("language_decline_server_edit", input);
            Assert.Contains("int value;", TextOf(session, origin), StringComparison.Ordinal);
            return;
        }
        if (scenario is "stale" or "stale-buffer")
        {
            var current = origin;
            if (scenario is "stale")
                await File.WriteAllTextAsync(Path.Combine(fixture, "workspace", "peer.c"), "// externally changed\n", token);
            else
                current = (await client.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
                    { ["document"] = origin, ["offset"] = 0, ["length"] = 0, ["text"] = "// unsaved\n" })).GetProperty("data");
            input["context"] = (await client.CallAsync("workbench_get_context")).GetProperty("context");
            var stale = await client.CallAsync("language_apply_server_edit", input);
            Assert.Equal("document_changed", Code(stale));
            Assert.Contains("int value;", TextOf(session, current), StringComparison.Ordinal);
            return;
        }
        await VerifyObserverCannotApplyAsync(session, input, token);
        await using var stdio = await client.ConnectStdioAsync(Path.Combine(fixture, "server-edit-stdio.json"));
        var applied = (await client.ChangeAsync("language_apply_server_edit", input, stdio)).GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(2, applied.Length);
        Assert.Contains("int server_value;", TextOf(session, applied[0]), StringComparison.Ordinal);
        Assert.Equal("// server edit\n" + peerText, TextOf(session, applied[1]));
        var undo = (await client.ChangeAsync("editor_undo", new(StringComparer.Ordinal) { ["document"] = applied[0] })).GetProperty("data");
        Assert.Contains("int value;", TextOf(session, undo), StringComparison.Ordinal);
    }

    private static async Task VerifyObserverCannotApplyAsync(WorkbenchSession session, Dictionary<string, object?> arguments, CancellationToken token)
    {
        var viewer = new ActorGrant("language-edit-observer", session.View.WorkspaceId, session.View.ViewKey, canMutate: false);
        var result = await session.Controller.DispatchAsync(session.Registry.Language.ApplyServerEdit,
            new(((JsonElement)arguments["reviewId"]!).GetGuid()), session.View.Address, viewer, Guid.NewGuid(), token);
        Assert.Equal("forbidden", result.Code);
    }

    private static async Task<JsonElement> WaitForServerReviewAsync(Connection client, CancellationToken token)
    {
        while (true)
        {
            var reviews = await ReadServerReviewsAsync(client);
            if (reviews.Length != 0)
                return Assert.Single(reviews);
            await Task.Delay(25, token);
        }
    }

    private static async Task<JsonElement[]> ReadServerReviewsAsync(Connection client)
    {
        var result = await client.CallAsync("language_list_server_edits");
        Assert.Equal("ok", Code(result));
        return result.GetProperty("data").EnumerateArray().ToArray();
    }
}
