using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;
using Connection = RetroIde.IntegrationTests.GitWorkspaceStateTests.WorkspaceConnection;

namespace RetroIde.IntegrationTests;

/// <summary>Verifies that detached workbench operations keep their identity and scope across the MCP transports.</summary>
public sealed class OperationHandleTests
{
    /// <summary>Retrieves a native Git fetch's final result through a bridge after starting it over HTTP.</summary>
    /// <remarks>TEST-RETRO-015#ac-test-mcp-operation.</remarks>
    [AvaloniaFact]
    public async Task Fetch_result_is_correlated_across_transports_with_the_same_grant()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var token = deadline.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var http = await AttachAsync(session, sample.Primary, token).ConfigureAwait(true);
        await using var bridge = await http.ConnectStdioAsync(Path.Combine(sample.Peer, ".git", "operation-stdio.json"), shareGrant: true).ConfigureAwait(true);
        var tools = await bridge.ListToolsAsync(cancellationToken: token).ConfigureAwait(true);
        Assert.Contains(tools, tool => string.Equals(tool.Name, "workbench_operation_status", StringComparison.Ordinal));
        var fetch = Assert.Single(tools, tool => string.Equals(tool.Name, "git_fetch", StringComparison.Ordinal));
        Assert.Equal("boolean", fetch.JsonSchema.GetProperty("properties").GetProperty("background").GetProperty("type").GetString());

        await sample.GitAsync(sample.Peer, token, "commit", "--allow-empty", "-m", "Update the reviewed origin").ConfigureAwait(true);
        await sample.GitAsync(sample.Peer, token, "push", "origin", "main").ConfigureAwait(true);
        var started = await StartFetchAsync(http, sample.Primary, "origin").ConfigureAwait(true);
        var handle = started.GetProperty("data");
        var id = handle.GetProperty("operationId").GetGuid();
        Assert.Equal(started.GetProperty("operationId").GetGuid(), id);
        Assert.Equal("git_fetch", handle.GetProperty("tool").GetString());

        var complete = await WaitForStateAsync(http, id, "succeeded", token, bridge).ConfigureAwait(true);
        Assert.Equal(id, complete.GetProperty("operationId").GetGuid());
        Assert.False(complete.GetProperty("canCancel").GetBoolean());
        var result = await http.CallAsync("workbench_operation_result", Handle(id), bridge).ConfigureAwait(true);
        Assert.Equal("ok", Code(result));
        var original = result.GetProperty("data");
        Assert.Equal("ok", Code(original));
        Assert.Equal(id, original.GetProperty("operationId").GetGuid());
        Assert.Equal("origin", original.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal(session.View.ViewKey, original.GetProperty("context").GetProperty("viewKey").GetGuid());
        Assert.Equal(
            await sample.GitAsync(sample.Peer, token, "rev-parse", "HEAD").ConfigureAwait(true),
            await sample.GitAsync(sample.Primary, token, "rev-parse", "refs/remotes/origin/main").ConfigureAwait(true));
    }

    /// <summary>Cancels a running native process and rejects observation by a different attachment.</summary>
    /// <remarks>TEST-RETRO-015#ac-test-mcp-operation.</remarks>
    [AvaloniaFact]
    public async Task Cancellation_stops_the_owned_native_fetch_and_preserves_its_cancelled_result()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var token = deadline.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var http = await AttachAsync(session, sample.Primary, token).ConfigureAwait(true);
        await using var bridge = await http.ConnectStdioAsync(Path.Combine(sample.Peer, ".git", "operation-cancel.json"), shareGrant: true).ConfigureAwait(true);
        var tools = await bridge.ListToolsAsync(cancellationToken: token).ConfigureAwait(true);
        Assert.Contains(tools, tool => string.Equals(tool.Name, "workbench_operation_cancel", StringComparison.Ordinal));
        using var stalled = new TcpListener(IPAddress.Loopback, 0);
        stalled.Start();
        var endpoint = (IPEndPoint)stalled.LocalEndpoint;
        await sample.GitAsync(sample.Primary, token, "remote", "add", "stalled", $"http://127.0.0.1:{endpoint.Port}/fixture").ConfigureAwait(true);
        await sample.GitAsync(sample.Primary, token, "config", "http.proxy", "").ConfigureAwait(true);

        var starting = StartFetchAsync(http, sample.Primary, "stalled", bridge);
        using var socket = await stalled.AcceptTcpClientAsync(token).ConfigureAwait(true);
        var started = await starting.ConfigureAwait(true);
        var id = started.GetProperty("data").GetProperty("operationId").GetGuid();
        var running = await http.CallAsync("workbench_operation_status", Handle(id)).ConfigureAwait(true);
        Assert.Equal("running", running.GetProperty("data").GetProperty("state").GetString());
        Assert.Equal("operation_pending", Code(await http.CallAsync("workbench_operation_result", Handle(id)).ConfigureAwait(true)));

        await using var other = await http.ConnectStdioAsync(Path.Combine(sample.Peer, ".git", "operation-other.json")).ConfigureAwait(true);
        Assert.Equal("operation_unavailable", Code(await http.CallAsync("workbench_operation_status", Handle(id), other).ConfigureAwait(true)));
        var context = (await http.CallAsync("workbench_get_context").ConfigureAwait(true)).GetProperty("context");
        var cancel = Handle(id);
        cancel["context"] = context;
        Assert.Equal("operation_unavailable", Code(await http.CallAsync("workbench_operation_cancel", cancel, other).ConfigureAwait(true)));
        await http.ChangeAsync("workbench_operation_cancel", Handle(id)).ConfigureAwait(true);
        var done = await WaitForStateAsync(http, id, "cancelled", token, bridge).ConfigureAwait(true);
        Assert.False(done.GetProperty("canCancel").GetBoolean());
        var result = await http.CallAsync("workbench_operation_result", Handle(id)).ConfigureAwait(true);
        Assert.Equal("cancelled", Code(result.GetProperty("data")));
        await WaitForDisconnectAsync(socket, token).ConfigureAwait(true);

        var restarting = StartFetchAsync(http, sample.Primary, "stalled");
        using var revoked = await stalled.AcceptTcpClientAsync(token).ConfigureAwait(true);
        _ = await restarting.ConfigureAwait(true);
        http.Revoke();
        await WaitForDisconnectAsync(revoked, token).ConfigureAwait(true);
    }

    private static async Task<Connection> AttachAsync(WorkbenchSession session, string worktree, CancellationToken token)
    {
        var http = await Connection.ConnectAsync(session, token).ConfigureAwait(true);
        try
        {
            var root = await http.AddRootAsync(worktree).ConfigureAwait(true);
            await http.ChangeAsync("git_select_repository", new(StringComparer.Ordinal)
                { ["rootId"] = root, ["relativePath"] = "" }).ConfigureAwait(true);
            return http;
        }
        catch
        {
            await http.DisposeAsync().ConfigureAwait(true);
            throw;
        }
    }

    private static async Task<JsonElement> StartFetchAsync(Connection http, string worktree, string name, McpClient? transport = null)
    {
        var remotes = await http.CallAsync("git_list_remotes", new(StringComparer.Ordinal) { ["worktreePath"] = worktree }).ConfigureAwait(true);
        return await http.ChangeAsync("git_fetch", new(StringComparer.Ordinal)
        {
            ["worktreePath"] = worktree,
            ["name"] = name,
            ["revision"] = remotes.GetProperty("data").GetProperty("revision").GetString(),
            ["background"] = true,
        }, transport).ConfigureAwait(true);
    }

    private static async Task<JsonElement> WaitForStateAsync(Connection http, Guid id, string expected, CancellationToken token, McpClient bridge)
    {
        while (true)
        {
            var status = await http.CallAsync("workbench_operation_status", Handle(id), bridge).ConfigureAwait(true);
            Assert.Equal("ok", Code(status));
            var data = status.GetProperty("data");
            var state = data.GetProperty("state").GetString();
            if (state is "succeeded" or "failed" or "cancelled")
            {
                Assert.Equal(expected, state);
                return data;
            }
            await Task.Delay(25, token).ConfigureAwait(true);
        }
    }

    private static async Task WaitForDisconnectAsync(TcpClient socket, CancellationToken token)
    {
        var buffer = new byte[1024];
        try
        {
            while (await socket.GetStream().ReadAsync(buffer, token).ConfigureAwait(false) != 0) { }
        }
        catch (IOException error) when (error.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset }) { }
    }

    private static Dictionary<string, object?> Handle(Guid id) => new(StringComparer.Ordinal) { ["operationId"] = id };
    private static string? Code(JsonElement result) => result.GetProperty("code").GetString();
}
