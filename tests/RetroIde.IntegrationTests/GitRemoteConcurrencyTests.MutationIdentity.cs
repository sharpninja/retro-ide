using System.Net;
using System.Net.Sockets;
using Avalonia.Headless.XUnit;
using RetroIde.Application.Git;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;
using Connection = RetroIde.IntegrationTests.GitWorkspaceStateTests.WorkspaceConnection;

namespace RetroIde.IntegrationTests;

public sealed partial class GitRemoteConcurrencyTests
{
    private const string QueuedBranch = "topic/queued";
    private const string RenamedBranch = "topic/renamed";

    /// <remarks>TR-RETRO-GIT-013#ac-git-machine-state; TEST-RETRO-013#ac-test-git-stage-and-push; TEST-RETRO-013#ac-test-git-recovery.</remarks>
    [AvaloniaTheory]
    [InlineData("git_create_branch", "")]
    [InlineData("git_switch_branch", "")]
    [InlineData("git_rename_branch", "")]
    [InlineData("git_delete_branch", "")]
    [InlineData("git_apply_patch", "index")]
    [InlineData("git_apply_patch", "worktree")]
    public async Task A_queued_git_write_cannot_mutate_the_replaced_checkouts_old_metadata(string tool, string layer)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var token = deadline.Token;
        using var fixture = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = LocalEndpoint(listener);
        await PointOriginAsync(fixture, endpoint, token).ConfigureAwait(true);
        var primary = GitPath(fixture.Primary);
        var linked = GitPath(fixture.Linked);
        var replacement = await PrepareReplacementMetadataAsync(fixture, linked, endpoint, token).ConfigureAwait(true);
        var captured = await NativeGit.DiscoverAsync(linked, "", token).ConfigureAwait(true);
        Assert.NotNull(captured);

        await using var first = new WorkbenchSession(Guid.NewGuid());
        await using var second = new WorkbenchSession(Guid.NewGuid());
        await using var blocker = await Connection.ConnectAsync(first, token).ConfigureAwait(true);
        await using var writer = await Connection.ConnectAsync(second, token).ConfigureAwait(true);
        await using var relay = await writer.ConnectStdioAsync(Path.Combine(fixture.Peer, ".git", "mutations-relay.json")).ConfigureAwait(true);
        await SelectAsync(blocker, primary).ConfigureAwait(true);
        await SelectAsync(writer, linked).ConfigureAwait(true);
        var args = await ReviewQueuedWriteAsync(fixture, blocker, writer, tool, layer, token).ConfigureAwait(true);
        var before = await ReadWriteStateAsync(fixture, captured.Repository, token).ConfigureAwait(true);
        var active = await StartReviewedFetchAsync(blocker, primary).ConfigureAwait(true);
        using var request = await listener.AcceptTcpClientAsync(token).ConfigureAwait(true);

        var pending = writer.CallAsync(tool, args, relay);
        Assert.NotSame(pending, await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(2), token)).ConfigureAwait(true));
        await ReplaceGitPointerAsync(linked, replacement, token).ConfigureAwait(true);
        var current = await NativeGit.DiscoverAsync(linked, "", token).ConfigureAwait(true);
        Assert.NotNull(current);
        Assert.Equal(replacement, GitPath(current.Repository.GitDirectory), StringComparer.Ordinal);
        Assert.True(Directory.Exists(captured.Repository.GitDirectory));
        await RejectRequestAsync(request, token).ConfigureAwait(true);

        var result = Code(await pending.WaitAsync(token).ConfigureAwait(true));
        await VerifyTransferCompletedAsync(blocker, active, "git_fetch_failed", token).ConfigureAwait(true);
        var after = await ReadWriteStateAsync(fixture, captured.Repository, token).ConfigureAwait(true);
        Assert.True(before == after, $"{tool} ({layer}) modified the previous checkout after replacement. It returned {result}.");
        Assert.Equal("git_repository_unavailable", result, StringComparer.Ordinal);
    }

    private static async Task<Dictionary<string, object?>> ReviewQueuedWriteAsync(GitWorktreeFixture fixture,
        Connection blocker, Connection writer, string tool, string layer, CancellationToken token)
    {
        var linked = GitPath(fixture.Linked);
        var args = new Dictionary<string, object?>(StringComparer.Ordinal) { ["worktreePath"] = linked };
        if (string.Equals(tool, "git_apply_patch", StringComparison.Ordinal))
            await AddPatchReviewAsync(fixture, blocker, writer, args, layer).ConfigureAwait(false);
        else
        {
            var head = (await fixture.GitAsync(linked, token, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
            if (string.Equals(tool, "git_create_branch", StringComparison.Ordinal))
                args["startCommit"] = head;
            else
            {
                await fixture.GitAsync(linked, token, "branch", "--no-track", QueuedBranch, head).ConfigureAwait(false);
                args["expectedHead"] = head;
                if (string.Equals(tool, "git_rename_branch", StringComparison.Ordinal))
                    args["newName"] = RenamedBranch;
            }
            args["branchName"] = QueuedBranch;
        }
        args["context"] = (await writer.CallAsync("workbench_get_context").ConfigureAwait(false)).GetProperty("context");
        return args;
    }

    private static async Task AddPatchReviewAsync(GitWorktreeFixture fixture, Connection blocker, Connection writer,
        Dictionary<string, object?> args, string layer)
    {
        GitWorktreeFixture.Write(fixture.Primary, GitWorktreeFixture.ConflictPath, "queued patch fixture\n");
        var exported = await blocker.CallAsync("git_export_patch", new(StringComparer.Ordinal)
            { ["worktreePath"] = GitPath(fixture.Primary), ["layer"] = "worktree" }).ConfigureAwait(false);
        Assert.Equal("ok", Code(exported), StringComparer.Ordinal);
        args["patchBase64"] = exported.GetProperty("data").GetProperty("patchBase64").GetString();
        args["layer"] = layer;
        var review = await writer.CallAsync("git_review_patch", args).ConfigureAwait(false);
        Assert.Equal("ok", Code(review), StringComparer.Ordinal);
        Assert.True(review.GetProperty("data").GetProperty("canApply").GetBoolean());
        args["revision"] = review.GetProperty("data").GetProperty("revision").GetString();
    }

    private static async Task<(string Refs, string Head, string Index, string WorkingBytes)> ReadWriteStateAsync(
        GitWorktreeFixture fixture, GitWorktree repository, CancellationToken token)
    {
        var refs = await fixture.GitAsync(repository.WorktreePath, token, "--git-dir", GitPath(repository.GitDirectory),
            "--work-tree", GitPath(repository.WorktreePath), "for-each-ref", "--format=%(refname) %(objectname)").ConfigureAwait(false);
        var head = await File.ReadAllTextAsync(Path.Combine(repository.GitDirectory, "HEAD"), token).ConfigureAwait(false);
        var index = await File.ReadAllBytesAsync(Path.Combine(repository.GitDirectory, "index"), token).ConfigureAwait(false);
        var working = await File.ReadAllBytesAsync(Path.Combine(repository.WorktreePath, GitWorktreeFixture.ConflictPath), token).ConfigureAwait(false);
        return (refs, head, Convert.ToBase64String(index), Convert.ToBase64String(working));
    }
}
