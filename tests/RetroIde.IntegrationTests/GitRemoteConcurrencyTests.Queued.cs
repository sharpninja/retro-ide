using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia.Headless.XUnit;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;
using Connection = RetroIde.IntegrationTests.GitWorkspaceStateTests.WorkspaceConnection;

namespace RetroIde.IntegrationTests;

public sealed partial class GitRemoteConcurrencyTests
{
    /// <remarks>TR-RETRO-GIT-013#ac-git-state-consistency; TEST-RETRO-013#ac-test-git-recovery.</remarks>
    [AvaloniaFact]
    public async Task Cancelling_a_queued_fetch_never_opens_its_connection_and_leaves_the_gate_available()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(50));
        var token = deadline.Token;
        using var fixture = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = LocalEndpoint(listener);
        await PointOriginAsync(fixture, endpoint, token).ConfigureAwait(true);

        await using var leftSession = new WorkbenchSession(Guid.NewGuid());
        await using var rightSession = new WorkbenchSession(Guid.NewGuid());
        await using var left = await Connection.ConnectAsync(leftSession, token).ConfigureAwait(true);
        await using var right = await Connection.ConnectAsync(rightSession, token).ConfigureAwait(true);
        var primary = GitPath(fixture.Primary);
        var linked = GitPath(fixture.Linked);
        await SelectAsync(left, primary).ConfigureAwait(true);
        await SelectAsync(right, linked).ConfigureAwait(true);

        var running = await StartReviewedFetchAsync(left, primary).ConfigureAwait(true);
        using var originalRequest = await listener.AcceptTcpClientAsync(token).ConfigureAwait(true);
        var waiting = await StartReviewedFetchAsync(right, linked).ConfigureAwait(true);
        var status = await right.CallAsync("workbench_operation_status", new(StringComparer.Ordinal)
            { ["operationId"] = waiting }).ConfigureAwait(true);
        Assert.True(status.GetProperty("data").GetProperty("canCancel").GetBoolean());
        await right.ChangeAsync("workbench_operation_cancel", new(StringComparer.Ordinal) { ["operationId"] = waiting }).ConfigureAwait(true);
        await VerifyTransferCompletedAsync(right, waiting, "cancelled", token).ConfigureAwait(true);
        Assert.False(listener.Pending(), "The cancelled job opened a Git connection while another transfer held the gate.");

        await RejectRequestAsync(originalRequest, token).ConfigureAwait(true);
        await VerifyTransferCompletedAsync(left, running, "git_fetch_failed", token).ConfigureAwait(true);
        var next = await StartReviewedFetchAsync(right, linked).ConfigureAwait(true);
        using var nextRequest = await listener.AcceptTcpClientAsync(token).ConfigureAwait(true);
        await RejectRequestAsync(nextRequest, token).ConfigureAwait(true);
        await VerifyTransferCompletedAsync(right, next, "git_fetch_failed", token).ConfigureAwait(true);
    }

    /// <remarks>TR-RETRO-GIT-013#ac-git-state-consistency; TEST-RETRO-013#ac-test-git-extensions.</remarks>
    [AvaloniaFact]
    public async Task Replacing_the_checkout_while_submodule_sync_waits_requires_a_fresh_review()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(55));
        var token = deadline.Token;
        using var fixture = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = LocalEndpoint(listener);
        var checkout = await ConfigureSubmoduleEndpointAsync(fixture, endpoint, token).ConfigureAwait(true);
        var replacement = await PrepareReplacementMetadataAsync(fixture, checkout, endpoint, token).ConfigureAwait(true);
        await PointOriginAsync(fixture, endpoint, token).ConfigureAwait(true);

        await using var transferSession = new WorkbenchSession(Guid.NewGuid());
        await using var changeSession = new WorkbenchSession(Guid.NewGuid());
        await using var transfer = await Connection.ConnectAsync(transferSession, token).ConfigureAwait(true);
        await using var change = await Connection.ConnectAsync(changeSession, token).ConfigureAwait(true);
        var parent = GitPath(fixture.Primary);
        await SelectAsync(transfer, parent).ConfigureAwait(true);
        await SelectAsync(change, parent).ConfigureAwait(true);
        var reviewed = await ReviewEndpointChangeAsync(change, fixture, submodule: true, token).ConfigureAwait(true);
        var running = await StartReviewedFetchAsync(transfer, parent).ConfigureAwait(true);
        using var request = await listener.AcceptTcpClientAsync(token).ConfigureAwait(true);

        var queued = change.CallAsync(reviewed.Tool, reviewed.Arguments);
        Assert.NotSame(queued, await Task.WhenAny(queued, Task.Delay(TimeSpan.FromSeconds(2), token)).ConfigureAwait(true));
        // Git for Windows marks the pointer file hidden. Open it in place, preserving its attributes.
        await using (var pointer = new FileStream(Path.Combine(checkout, ".git"), FileMode.Open, FileAccess.Write,
            FileShare.None, bufferSize: 4096, useAsync: true))
        {
            pointer.SetLength(0);
            await pointer.WriteAsync(Encoding.UTF8.GetBytes($"gitdir: {replacement}\n"), token).ConfigureAwait(true);
        }
        var current = (await fixture.GitAsync(checkout, token, "rev-parse", "--absolute-git-dir").ConfigureAwait(true)).Trim();
        Assert.Equal(replacement, GitPath(current), StringComparer.Ordinal);
        var actualParent = (await fixture.GitAsync(checkout, token, "rev-parse", "--show-superproject-working-tree").ConfigureAwait(true)).Trim();
        Assert.Equal(parent, GitPath(actualParent), StringComparer.Ordinal);
        await RejectRequestAsync(request, token).ConfigureAwait(true);

        Assert.Equal("git_submodule_changed", Code(await queued.WaitAsync(token).ConfigureAwait(true)), StringComparer.Ordinal);
        Assert.Equal(endpoint, await ReadEffectiveEndpointAsync(fixture, checkout, token).ConfigureAwait(true), StringComparer.Ordinal);
        await VerifyTransferCompletedAsync(transfer, running, "git_fetch_failed", token).ConfigureAwait(true);
    }

    private static async Task<string> PrepareReplacementMetadataAsync(GitWorktreeFixture fixture, string checkout,
        string endpoint, CancellationToken token)
    {
        var admin = GitPath(Path.Combine(fixture.Peer, ".git", "replacement-submodule"));
        await fixture.GitAsync(fixture.Peer, token, "clone", "--bare", "--no-local", GitPath(fixture.Remote), admin).ConfigureAwait(false);
        var config = GitPath(Path.Combine(admin, "config"));
        await fixture.GitAsync(fixture.Peer, token, "config", "--file", config, "core.bare", "false").ConfigureAwait(false);
        await fixture.GitAsync(fixture.Peer, token, "config", "--file", config, "core.worktree", checkout).ConfigureAwait(false);
        await fixture.GitAsync(fixture.Peer, token, "config", "--file", config, "remote.origin.url", endpoint).ConfigureAwait(false);
        return admin;
    }

    private static string LocalEndpoint(TcpListener listener) =>
        new UriBuilder(Uri.UriSchemeHttp, IPAddress.Loopback.ToString(), ((IPEndPoint)listener.LocalEndpoint).Port).Uri.AbsoluteUri;

    private static async Task PointOriginAsync(GitWorktreeFixture fixture, string endpoint, CancellationToken token)
    {
        await fixture.GitAsync(fixture.Primary, token, "remote", "set-url", "origin", endpoint).ConfigureAwait(false);
        await fixture.GitAsync(fixture.Primary, token, "config", "--local", "http.proxy", "").ConfigureAwait(false);
    }

    private static ValueTask RejectRequestAsync(TcpClient request, CancellationToken token) =>
        request.GetStream().WriteAsync("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), token);
}
