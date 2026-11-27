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

public sealed partial class GitRemoteConcurrencyTests
{
    private const string ReplacementEndpoint = "https://replacement.invalid/review";
    private const string SubmodulePath = "deps/driver";
    private const string SubmoduleName = "driver";
    private const string AlternateBranch = "alternate-endpoint";

    /// <remarks>TR-RETRO-GIT-013#ac-git-state-consistency; TEST-RETRO-013#ac-test-git-stage-and-push; TEST-RETRO-013#ac-test-git-extensions.</remarks>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Built_in_endpoint_changes_wait_for_the_reviewed_native_transfer(bool submodule)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(55));
        var token = deadline.Token;
        using var fixture = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var endpoint = new UriBuilder(Uri.UriSchemeHttp, IPAddress.Loopback.ToString(),
                ((IPEndPoint)listener.LocalEndpoint).Port).Uri.AbsoluteUri;
            var checkout = submodule
                ? await ConfigureSubmoduleEndpointAsync(fixture, endpoint, token).ConfigureAwait(true)
                : await ConfigureBranchEndpointsAsync(fixture, endpoint, token).ConfigureAwait(true);
            Assert.Equal(endpoint, await ReadEffectiveEndpointAsync(fixture, checkout, token).ConfigureAwait(true), StringComparer.Ordinal);

            await using var transferSession = new WorkbenchSession(Guid.NewGuid());
            await using var changeSession = new WorkbenchSession(Guid.NewGuid());
            await using var transfer = await Connection.ConnectAsync(transferSession, token).ConfigureAwait(true);
            await using var change = await Connection.ConnectAsync(changeSession, token).ConfigureAwait(true);
            await using var relay = await change.ConnectStdioAsync(Path.Combine(fixture.Peer, ".git", "effective-endpoint.json"))
                .ConfigureAwait(true);
            await SelectAsync(transfer, checkout).ConfigureAwait(true);
            await SelectAsync(change, fixture.Primary).ConfigureAwait(true);
            var mutation = await ReviewEndpointChangeAsync(change, fixture, submodule, token).ConfigureAwait(true);
            var operation = await StartReviewedFetchAsync(transfer, checkout).ConfigureAwait(true);
            using var request = await listener.AcceptTcpClientAsync(token).ConfigureAwait(true);

            var changed = change.CallAsync(mutation.Tool, mutation.Arguments, relay);
            var finishedEarly = false;
            string duringTransfer;
            try
            {
                finishedEarly = ReferenceEquals(await Task.WhenAny(changed, Task.Delay(TimeSpan.FromSeconds(2), token))
                    .ConfigureAwait(true), changed);
                duringTransfer = await ReadEffectiveEndpointAsync(fixture, checkout, token).ConfigureAwait(true);
            }
            finally
            {
                await request.GetStream().WriteAsync("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8
                    .ToArray(), token).ConfigureAwait(true);
            }

            Assert.Equal("ok", Code(await changed.WaitAsync(token).ConfigureAwait(true)), StringComparer.Ordinal);
            await VerifyTransferCompletedAsync(transfer, operation, "git_fetch_failed", token).ConfigureAwait(true);
            Assert.Equal(ReplacementEndpoint, await ReadEffectiveEndpointAsync(fixture, checkout, token).ConfigureAwait(true), StringComparer.Ordinal);
            Assert.False(finishedEarly, $"{mutation.Tool} changed the effective endpoint during a live reviewed fetch. Native Git read {duringTransfer}.");
            Assert.Equal(endpoint, duringTransfer, StringComparer.Ordinal);
        }
        finally { listener.Stop(); }
    }

    private static async Task<string> ConfigureSubmoduleEndpointAsync(GitWorktreeFixture fixture, string endpoint, CancellationToken token)
    {
        await fixture.GitAsync(fixture.Primary, token, "-c", "protocol.file.allow=always", "submodule", "add",
            "--name", SubmoduleName, "--", fixture.Remote, SubmodulePath).ConfigureAwait(false);
        var checkout = Path.Combine(fixture.Primary, SubmodulePath);
        await fixture.GitAsync(checkout, token, "remote", "set-url", "origin", endpoint).ConfigureAwait(false);
        await fixture.GitAsync(checkout, token, "config", "--local", "http.proxy", "").ConfigureAwait(false);
        await fixture.GitAsync(fixture.Primary, token, "config", "--file", ".gitmodules", $"submodule.{SubmoduleName}.url",
            ReplacementEndpoint).ConfigureAwait(false);
        return checkout;
    }

    private static async Task<string> ConfigureBranchEndpointsAsync(GitWorktreeFixture fixture, string endpoint, CancellationToken token)
    {
        var checkout = fixture.Primary;
        await fixture.GitAsync(checkout, token, "branch", AlternateBranch).ConfigureAwait(false);
        await fixture.GitAsync(checkout, token, "config", "--local", "--unset-all", "remote.origin.url").ConfigureAwait(false);
        await fixture.GitAsync(checkout, token, "config", "--local", "http.proxy", "").ConfigureAwait(false);
        await IncludeEndpointAsync("main", endpoint).ConfigureAwait(false);
        await IncludeEndpointAsync(AlternateBranch, ReplacementEndpoint).ConfigureAwait(false);
        return checkout;

        async Task IncludeEndpointAsync(string branch, string url)
        {
            var include = Path.Combine(fixture.Peer, ".git", $"{branch}.config");
            await File.WriteAllTextAsync(include, $"[remote \"origin\"]\n\turl = {url}\n", token).ConfigureAwait(false);
            await fixture.GitAsync(checkout, token, "config", "--local", "--add", $"includeIf.onbranch:{branch}.path",
                include.Replace('\\', '/')).ConfigureAwait(false);
        }
    }

    private static async Task<(string Tool, Dictionary<string, object?> Arguments)> ReviewEndpointChangeAsync(Connection connection,
        GitWorktreeFixture fixture, bool submodule, CancellationToken token)
    {
        var checkout = fixture.Primary;
        var context = await connection.CallAsync("workbench_get_context").ConfigureAwait(false);
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["worktreePath"] = checkout, ["context"] = context.GetProperty("context"),
        };
        if (!submodule)
        {
            arguments["branchName"] = AlternateBranch;
            arguments["expectedHead"] = (await fixture.GitAsync(checkout, token, "rev-parse", AlternateBranch).ConfigureAwait(false)).Trim();
            return ("git_switch_branch", arguments);
        }

        var reply = await connection.CallAsync("git_list_submodules", new(StringComparer.Ordinal) { ["worktreePath"] = checkout })
            .ConfigureAwait(false);
        Assert.Equal("ok", Code(reply), StringComparer.Ordinal);
        var child = Assert.Single(reply.GetProperty("data").EnumerateArray());
        Assert.True(child.GetProperty("hasCheckout").GetBoolean());
        arguments["path"] = SubmodulePath;
        arguments["expectedRevision"] = child.GetProperty("revision").GetString();
        return ("git_sync_submodule", arguments);
    }

    private static async Task<Guid> StartReviewedFetchAsync(Connection connection, string checkout)
    {
        var revision = await ReviewAsync(connection, checkout).ConfigureAwait(false);
        var context = await connection.CallAsync("workbench_get_context").ConfigureAwait(false);
        var result = await connection.CallAsync("git_fetch", new(StringComparer.Ordinal)
        {
            ["worktreePath"] = checkout, ["name"] = "origin", ["revision"] = revision, ["background"] = true,
            ["context"] = context.GetProperty("context"),
        }).ConfigureAwait(false);
        Assert.Equal("ok", Code(result), StringComparer.Ordinal);
        return result.GetProperty("data").GetProperty("operationId").GetGuid();
    }

    private static async Task<string> ReadEffectiveEndpointAsync(GitWorktreeFixture fixture, string path, CancellationToken token) =>
        (await fixture.GitAsync(path, token, "remote", "get-url", "origin").ConfigureAwait(false)).Trim();
}
