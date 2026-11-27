using System.Net;
using System.Net.Sockets;
using Avalonia.Headless.XUnit;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;
using Connection = RetroIde.IntegrationTests.GitWorkspaceStateTests.WorkspaceConnection;

namespace RetroIde.IntegrationTests;

public sealed partial class GitRemoteConcurrencyTests
{
    /// <remarks>TR-RETRO-GIT-013#ac-git-state-consistency; TEST-RETRO-013#ac-test-git-stage-and-push.</remarks>
    [AvaloniaTheory]
    [InlineData("git_commit")]
    [InlineData("git_amend")]
    [InlineData("git_switch_branch")]
    public async Task Native_hooks_and_includes_from_other_checkouts_preserve_the_reviewed_endpoint(string tool)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var token = deadline.Token;
        using var fixture = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = new UriBuilder(Uri.UriSchemeHttp, IPAddress.Loopback.ToString(),
            ((IPEndPoint)listener.LocalEndpoint).Port).Uri.AbsoluteUri;
        var include = string.Equals(tool, "git_switch_branch", StringComparison.Ordinal);
        var mutationPath = include
            ? await ConfigureCrossCheckoutIncludeAsync(fixture, endpoint, token).ConfigureAwait(true)
            : await ConfigureCommitRemoteHookAsync(fixture, endpoint, token).ConfigureAwait(true);
        await fixture.GitAsync(fixture.Primary, token, "config", "--local", "http.proxy", "").ConfigureAwait(true);

        await using var transferSession = new WorkbenchSession(Guid.NewGuid());
        await using var changeSession = new WorkbenchSession(Guid.NewGuid());
        await using var transfer = await Connection.ConnectAsync(transferSession, token).ConfigureAwait(true);
        await using var change = await Connection.ConnectAsync(changeSession, token).ConfigureAwait(true);
        await using var relay = await change.ConnectStdioAsync(Path.Combine(fixture.Peer, ".git", "hook-endpoint-relay.json"))
            .ConfigureAwait(true);
        var checkout = GitPath(fixture.Primary);
        await SelectAsync(transfer, checkout).ConfigureAwait(true);
        await SelectAsync(change, mutationPath).ConfigureAwait(true);
        var arguments = await ReviewHookOrIncludeMutationAsync(change, fixture, mutationPath, include, token).ConfigureAwait(true);
        Assert.Equal(endpoint, await ReadEffectiveEndpointAsync(fixture, checkout, token).ConfigureAwait(true), StringComparer.Ordinal);

        var operation = await StartReviewedFetchAsync(transfer, checkout).ConfigureAwait(true);
        using var request = await listener.AcceptTcpClientAsync(token).ConfigureAwait(true);
        var mutation = change.CallAsync(tool, arguments, relay);
        var overlap = ReferenceEquals(await Task.WhenAny(mutation, Task.Delay(TimeSpan.FromSeconds(2), token)).ConfigureAwait(true), mutation);
        var observed = await ReadEffectiveEndpointAsync(fixture, checkout, token).ConfigureAwait(true);
        await request.GetStream().WriteAsync("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8
            .ToArray(), token).ConfigureAwait(true);
        Assert.Equal("ok", Code(await mutation.WaitAsync(token).ConfigureAwait(true)), StringComparer.Ordinal);
        await VerifyTransferCompletedAsync(transfer, operation, "git_fetch_failed", token).ConfigureAwait(true);

        Assert.Equal(ReplacementEndpoint, await ReadEffectiveEndpointAsync(fixture, checkout, token).ConfigureAwait(true), StringComparer.Ordinal);
        Assert.False(overlap, $"{tool} changed the endpoint during a live reviewed fetch. Native Git read {observed}.");
        Assert.Equal(endpoint, observed, StringComparer.Ordinal);
    }

    private static async Task<string> ConfigureCommitRemoteHookAsync(GitWorktreeFixture fixture, string endpoint, CancellationToken token)
    {
        var linked = GitPath(fixture.Linked);
        await fixture.GitAsync(fixture.Primary, token, "remote", "set-url", "origin", endpoint).ConfigureAwait(false);
        var hooks = Directory.CreateDirectory(Path.Combine(fixture.Primary, ".git", "endpoint-hooks"));
        await fixture.GitAsync(linked, token, "config", "--local", "core.hooksPath", GitPath(hooks.FullName)).ConfigureAwait(false);
        await fixture.GitAsync(linked, token, "config", "--local", "user.name", "Retro IDE Fixture").ConfigureAwait(false);
        await fixture.GitAsync(linked, token, "config", "--local", "user.email", "fixture@example.invalid").ConfigureAwait(false);
        await fixture.GitAsync(linked, token, "config", "--local", "commit.gpgsign", "false").ConfigureAwait(false);
        var hook = Path.Combine(hooks.FullName, "pre-commit");
        await File.WriteAllTextAsync(hook, $"#!/bin/sh\ngit config --local remote.origin.url {ReplacementEndpoint}\n", token).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        GitWorktreeFixture.Write(linked, GitWorktreeFixture.ChangedPath, "fixture changes to commit\n");
        await fixture.GitAsync(linked, token, "--literal-pathspecs", "add", "--", GitWorktreeFixture.ChangedPath).ConfigureAwait(false);
        return linked;
    }

    private static async Task<string> ConfigureCrossCheckoutIncludeAsync(GitWorktreeFixture fixture, string endpoint, CancellationToken token)
    {
        const string FileName = "included-remote.config";
        var peer = GitPath(fixture.Peer);
        await CommitConfigAsync(endpoint).ConfigureAwait(false);
        await fixture.GitAsync(peer, token, "switch", "-c", AlternateBranch).ConfigureAwait(false);
        await CommitConfigAsync(ReplacementEndpoint).ConfigureAwait(false);
        await fixture.GitAsync(peer, token, "switch", "main").ConfigureAwait(false);
        await fixture.GitAsync(fixture.Primary, token, "config", "--local", "--unset-all", "remote.origin.url").ConfigureAwait(false);
        await fixture.GitAsync(fixture.Primary, token, "config", "--local", "--add", "include.path",
            GitPath(Path.Combine(peer, FileName))).ConfigureAwait(false);
        return peer;

        async Task CommitConfigAsync(string url)
        {
            GitWorktreeFixture.Write(peer, FileName, $"[remote \"origin\"]\n\turl = {url}\n");
            await fixture.GitAsync(peer, token, "add", "--", FileName).ConfigureAwait(false);
            await fixture.GitAsync(peer, token, "commit", "-m", "Set fixture include").ConfigureAwait(false);
        }
    }

    private static async Task<Dictionary<string, object?>> ReviewHookOrIncludeMutationAsync(Connection connection, GitWorktreeFixture fixture,
        string checkout, bool include, CancellationToken token)
    {
        var context = await connection.CallAsync("workbench_get_context").ConfigureAwait(false);
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["worktreePath"] = checkout, ["context"] = context.GetProperty("context"),
        };
        if (include)
        {
            arguments["branchName"] = AlternateBranch;
            arguments["expectedHead"] = (await fixture.GitAsync(checkout, token, "rev-parse", AlternateBranch).ConfigureAwait(false)).Trim();
        }
        else
        {
            var review = await connection.CallAsync("git_get_commit_state", new(StringComparer.Ordinal)
                { ["worktreePath"] = checkout }).ConfigureAwait(false);
            Assert.Equal("ok", Code(review), StringComparer.Ordinal);
            arguments["preimage"] = review.GetProperty("data").GetProperty("preimage").GetString();
            arguments["message"] = "Commit the reviewed fixture";
        }
        return arguments;
    }
}
