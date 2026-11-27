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

/// <summary>Verifies overlapping native Git worktree requests against shared remote configuration.</summary>
public sealed partial class GitRemoteConcurrencyTests
{
    /// <remarks>TR-RETRO-GIT-013#ac-git-state-consistency; TEST-RETRO-013#ac-test-git-stage-and-push.</remarks>
    [AvaloniaFact]
    public async Task Linked_worktrees_cannot_both_write_the_same_reviewed_remote_revision()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(40));
        var token = deadline.Token;
        using var fixture = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        await using var primary = new WorkbenchSession(Guid.NewGuid());
        await using var linked = new WorkbenchSession(Guid.NewGuid());
        await using var direct = await Connection.ConnectAsync(primary, token).ConfigureAwait(true);
        await using var other = await Connection.ConnectAsync(linked, token).ConfigureAwait(true);
        await using var relay = await other.ConnectStdioAsync(Path.Combine(fixture.Peer, ".git", "remote-revision-stdio.json"))
            .ConfigureAwait(true);
        await SelectAsync(direct, fixture.Primary).ConfigureAwait(true);
        await SelectAsync(other, fixture.Linked).ConfigureAwait(true);

        var left = await ReviewAsync(direct, fixture.Primary).ConfigureAwait(true);
        var right = await ReviewAsync(other, fixture.Linked, relay).ConfigureAwait(true);
        Assert.Equal(left, right, StringComparer.Ordinal);

        var first = WriteAsync(direct, fixture.Primary, left, "https://first.invalid/review");
        var second = WriteAsync(other, fixture.Linked, right, "https://second.invalid/review", relay);
        var completed = await Task.WhenAll(first, second).WaitAsync(token).ConfigureAwait(true);
        var accepted = Assert.Single(completed, result => string.Equals(result.Code, "ok", StringComparison.Ordinal));
        Assert.Single(completed, result => string.Equals(result.Code, "git_remote_changed", StringComparison.Ordinal));
        var actual = await fixture.GitAsync(fixture.Primary, token, "config", "--get-all", "remote.origin.url").ConfigureAwait(true);
        Assert.Equal(accepted.Url, actual.Trim(), StringComparer.Ordinal);
        var linkedValue = await fixture.GitAsync(fixture.Linked, token, "config", "--get-all", "remote.origin.url").ConfigureAwait(true);
        Assert.Equal(accepted.Url, linkedValue.Trim(), StringComparer.Ordinal);
    }

    /// <summary>Observes a real native transfer and a conflicting remote edit from another linked worktree.</summary>
    /// <remarks>TR-RETRO-GIT-013#ac-git-state-consistency; TEST-RETRO-013#ac-test-git-stage-and-push.</remarks>
    [AvaloniaTheory]
    [InlineData("git_fetch", "git_fetch_failed")]
    [InlineData("git_push", "git_push_failed")]
    [InlineData("git_pull", "git_fetch_failed")]
    public async Task Linked_worktree_cannot_change_the_remote_during_a_reviewed_transfer(string tool, string expectedFailure)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var token = deadline.Token;
        using var fixture = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = new UriBuilder(Uri.UriSchemeHttp, IPAddress.Loopback.ToString(),
            ((IPEndPoint)listener.LocalEndpoint).Port).Uri.AbsoluteUri;
        await fixture.GitAsync(fixture.Primary, token, "remote", "set-url", "origin", endpoint).ConfigureAwait(true);
        await fixture.GitAsync(fixture.Primary, token, "config", "--local", "http.proxy", "").ConfigureAwait(true);

        await using var primary = new WorkbenchSession(Guid.NewGuid());
        await using var linked = new WorkbenchSession(Guid.NewGuid());
        await using var direct = await Connection.ConnectAsync(primary, token).ConfigureAwait(true);
        await using var other = await Connection.ConnectAsync(linked, token).ConfigureAwait(true);
        await using var relay = await other.ConnectStdioAsync(Path.Combine(fixture.Peer, ".git", "remote-transfer-stdio.json"))
            .ConfigureAwait(true);
        await SelectAsync(direct, fixture.Primary).ConfigureAwait(true);
        await SelectAsync(other, fixture.Linked).ConfigureAwait(true);
        var revision = await ReviewAsync(direct, fixture.Primary).ConfigureAwait(true);
        Assert.Equal(revision, await ReviewAsync(other, fixture.Linked, relay).ConfigureAwait(true), StringComparer.Ordinal);

        var context = await direct.CallAsync("workbench_get_context").ConfigureAwait(true);
        var args = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["worktreePath"] = fixture.Primary,
            ["name"] = "origin",
            ["revision"] = revision,
            ["background"] = true,
            ["context"] = context.GetProperty("context"),
        };
        if (!string.Equals(tool, "git_fetch", StringComparison.Ordinal))
        {
            args["branchName"] = "main";
            args["expectedHead"] = (await fixture.GitAsync(fixture.Primary, token, "rev-parse", "HEAD").ConfigureAwait(true)).Trim();
            args["remoteBranch"] = "main";
        }

        var start = await direct.CallAsync(tool, args).ConfigureAwait(true);
        Assert.Equal("ok", Code(start), StringComparer.Ordinal);
        var id = start.GetProperty("data").GetProperty("operationId").GetGuid();
        using var connection = await listener.AcceptTcpClientAsync(token).ConfigureAwait(true);
        const string replacement = "https://updated.invalid/review";
        var edit = WriteAsync(other, fixture.Linked, revision, replacement, relay);
        var finishedEarly = ReferenceEquals(await Task.WhenAny(edit, Task.Delay(TimeSpan.FromSeconds(2), token)).ConfigureAwait(true), edit);
        var urlWhileRunning = await fixture.GitAsync(fixture.Linked, token, "config", "--get", "remote.origin.url").ConfigureAwait(true);

        var response = "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray();
        await connection.GetStream().WriteAsync(response, token).ConfigureAwait(true);
        var edited = await edit.WaitAsync(token).ConfigureAwait(true);
        await VerifyTransferCompletedAsync(direct, id, expectedFailure, token).ConfigureAwait(true);

        Assert.False(finishedEarly, $"The linked worktree edited origin while {tool} was still running.");
        Assert.Equal(endpoint, urlWhileRunning.Trim(), StringComparer.Ordinal);
        Assert.Equal("ok", edited.Code, StringComparer.Ordinal);
        var final = await fixture.GitAsync(fixture.Primary, token, "config", "--get", "remote.origin.url").ConfigureAwait(true);
        Assert.Equal(replacement, final.Trim(), StringComparer.Ordinal);
    }

    private static async Task VerifyTransferCompletedAsync(Connection connection, Guid operation, string expected, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var result = await connection.CallAsync("workbench_operation_result",
                new(StringComparer.Ordinal) { ["operationId"] = operation }).ConfigureAwait(false);
            if (!string.Equals(Code(result), "operation_pending", StringComparison.Ordinal))
            {
                Assert.Equal("ok", Code(result), StringComparer.Ordinal);
                Assert.Equal(expected, Code(result.GetProperty("data")), StringComparer.Ordinal);
                return;
            }

            await Task.Delay(25, token).ConfigureAwait(false);
        }
    }

    private static async Task SelectAsync(Connection connection, string path)
    {
        var root = await connection.AddRootAsync(path).ConfigureAwait(false);
        await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal)
        {
            ["rootId"] = root,
            ["relativePath"] = "",
        }).ConfigureAwait(false);
    }

    private static async Task<string> ReviewAsync(Connection connection, string path, McpClient? transport = null)
    {
        var result = await connection.CallAsync("git_list_remotes", new(StringComparer.Ordinal) { ["worktreePath"] = path }, transport)
            .ConfigureAwait(false);
        Assert.Equal("ok", Code(result), StringComparer.Ordinal);
        return Assert.IsType<string>(result.GetProperty("data").GetProperty("revision").GetString());
    }

    private static async Task<(string? Code, string Url)> WriteAsync(Connection connection, string path, string revision,
        string url, McpClient? transport = null)
    {
        var current = await connection.CallAsync("workbench_get_context", transport: transport).ConfigureAwait(false);
        var result = await connection.CallAsync("git_set_remote_url", new(StringComparer.Ordinal)
        {
            ["worktreePath"] = path,
            ["name"] = "origin",
            ["revision"] = revision,
            ["value"] = url,
            ["context"] = current.GetProperty("context"),
        }, transport).ConfigureAwait(false);
        return (Code(result), url);
    }

    private static string? Code(JsonElement result) => result.GetProperty("code").GetString();
}
