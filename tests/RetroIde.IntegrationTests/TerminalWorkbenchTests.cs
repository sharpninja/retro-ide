using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using Avalonia.Headless.XUnit;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

/// <summary>Verifies the native repository terminal against workbench and MCP contracts.</summary>
public sealed class TerminalWorkbenchTests
{
    /// <summary>Uses the same live shell across transports and verifies checkout and attachment scope.</summary>
    /// <remarks>TEST-RETRO-013#ac-test-git-extensions; TEST-RETRO-015#ac-test-mcp-same-instance.</remarks>
    [AvaloniaFact]
    public async Task Native_worktree_terminal_is_shared_by_the_document_and_mcp_transports()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(75));
        var token = deadline.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var http = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token).ConfigureAwait(true);
        var root = await http.AddRootAsync(sample.Linked).ConfigureAwait(true);
        await http.ChangeAsync("git_select_repository", new(StringComparer.Ordinal)
            { ["rootId"] = root, ["relativePath"] = "" }).ConfigureAwait(true);
        await using var relay = await http.ConnectStdioAsync(Path.Combine(sample.Peer, ".git", "terminal-stdio.json")).ConfigureAwait(true);
        var tools = await relay.ListToolsAsync(cancellationToken: token).ConfigureAwait(true);
        Assert.Contains(tools, tool => string.Equals(tool.Name, "git_terminal_open", StringComparison.Ordinal));

        var reply = await http.ChangeAsync("git_terminal_open", new(StringComparer.Ordinal)
            { ["worktreePath"] = sample.Linked }, relay).ConfigureAwait(true);
        var terminal = reply.GetProperty("data");
        var key = terminal.GetProperty("viewKey").GetGuid();
        Assert.Equal(session.View.WorkspaceId, terminal.GetProperty("workspaceId").GetGuid());
        Assert.Equal(sample.Linked, terminal.GetProperty("worktreePath").GetString());
        Assert.True(terminal.GetProperty("isRunning").GetBoolean());
        await VerifyScopeAsync(session, http, relay, sample.Primary, key, token).ConfigureAwait(true);

        var sessions = await http.CallAsync("git_terminal_list").ConfigureAwait(true);
        Assert.Equal(key, Assert.Single(sessions.GetProperty("data").EnumerateArray()).GetProperty("viewKey").GetGuid());
        await http.ChangeAsync("git_terminal_resize", new(StringComparer.Ordinal)
            { ["viewKey"] = key, ["columns"] = 102, ["rows"] = 34 }, relay).ConfigureAwait(true);
        await http.ChangeAsync("git_terminal_write", new(StringComparer.Ordinal)
            { ["viewKey"] = key, ["text"] = "git branch --show-current\r" }).ConfigureAwait(true);
        await http.ChangeAsync("git_terminal_write", new(StringComparer.Ordinal)
            { ["viewKey"] = key, ["text"] = "echo terminal-proof> terminal-proof.txt\r" }, relay).ConfigureAwait(true);

        using var output = new MemoryStream();
        long offset = 0;
        while (true)
        {
            var read = await http.CallAsync("git_terminal_read",
                new(StringComparer.Ordinal) { ["viewKey"] = key, ["offset"] = offset, ["maximumBytes"] = 128 }, relay).ConfigureAwait(true);
            Assert.Equal("ok", read.GetProperty("code").GetString());
            var slice = read.GetProperty("data");
            output.Write(slice.GetProperty("bytes").GetBytesFromBase64());
            offset = slice.GetProperty("nextOffset").GetInt64();
            if (Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length).Contains("fixture-linked", StringComparison.Ordinal))
                break;
            await Task.Delay(25, token).ConfigureAwait(true);
        }

        while (!session.SourceControl.Working.Any(entry => string.Equals(entry.Path, "terminal-proof.txt", StringComparison.Ordinal)))
            await Task.Delay(25, token).ConfigureAwait(true);
        Assert.True(File.Exists(Path.Combine(sample.Linked, "terminal-proof.txt")));
        Assert.False(File.Exists(Path.Combine(sample.Primary, "terminal-proof.txt")));

        await http.ChangeAsync("git_terminal_close", new(StringComparer.Ordinal) { ["viewKey"] = key }, relay).ConfigureAwait(true);
        var closed = await http.CallAsync("git_terminal_read", new(StringComparer.Ordinal) { ["viewKey"] = key }).ConfigureAwait(true);
        Assert.Equal("terminal_unavailable", closed.GetProperty("code").GetString());
    }

    private static async Task VerifyScopeAsync(WorkbenchSession session, GitWorkspaceStateTests.WorkspaceConnection http,
        McpClient relay, string differentWorktree, Guid terminalKey, CancellationToken token)
    {
        JsonElement context = (await http.CallAsync("workbench_get_context").ConfigureAwait(true)).GetProperty("context");
        await using var observer = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token, canMutate: false).ConfigureAwait(true);
        var forbidden = await observer.CallAsync("git_terminal_write", new(StringComparer.Ordinal)
            { ["viewKey"] = terminalKey, ["text"] = "echo forbidden", ["context"] = context }).ConfigureAwait(true);
        Assert.Equal("forbidden", forbidden.GetProperty("code").GetString());
        await using var foreignSession = new WorkbenchSession(Guid.NewGuid());
        await using var foreign = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(foreignSession, token).ConfigureAwait(true);
        var outside = await foreign.CallAsync("git_terminal_read", new(StringComparer.Ordinal) { ["viewKey"] = terminalKey }).ConfigureAwait(true);
        Assert.Equal("terminal_unavailable", outside.GetProperty("code").GetString());
        var mismatch = await http.CallAsync("git_terminal_open", new(StringComparer.Ordinal)
            { ["worktreePath"] = differentWorktree, ["context"] = context }, relay).ConfigureAwait(true);
        Assert.Equal("git_worktree_mismatch", mismatch.GetProperty("code").GetString());
    }
}
