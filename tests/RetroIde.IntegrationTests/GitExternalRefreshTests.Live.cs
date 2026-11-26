using Avalonia.Headless.XUnit;
using RetroIde.Application;
using RetroIde.Application.Git;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitWorkspaceStateTests
{
    /// <summary>Shows native edits, index writes and refs on the selected desktop checkout without an MCP status read.</summary>
    [AvaloniaFact]
    public async Task External_Git_changes_automatically_refresh_the_selected_checkout()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = deadline.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token);
        await using var session = new WorkbenchSession(Guid.NewGuid());
        var window = new WorkbenchWindow(session) { Width = 1440, Height = 900 };
        window.Show();
        try
        {
            await using var connection = await WorkspaceConnection.ConnectAsync(session, token);
            var primary = await connection.AddRootAsync(sample.Primary);
            var linked = await connection.AddRootAsync(sample.Linked);
            await connection.ExpandAsync(primary, "");
            await connection.ExpandAsync(primary, "src");
            await connection.ChangeAsync("git_select_repository", Address(primary, ""));
            Assert.Equal("ok", (await connection.CallAsync("git_get_status")).GetProperty("code").GetString());

            GitWorktreeFixture.Write(sample.Primary, GitWorktreeFixture.ChangedPath, "externally edited\n");
            var dirty = await WaitForRefreshAsync(session, sample.Primary, "main", ".M", token);
            VerifyRefresh(dirty, sample.Primary, "main", ".M");
            Assert.Equal("Modified", await ReadBadgeAsync(window, primary, GitWorktreeFixture.ChangedPath, token));

            await sample.GitAsync(sample.Primary, token, "add", "--", GitWorktreeFixture.ChangedPath);
            await WaitForRefreshAsync(session, sample.Primary, "main", "M.", token);
            Assert.Equal("Staged", await ReadBadgeAsync(window, primary, GitWorktreeFixture.ChangedPath, token));

            await sample.GitAsync(sample.Primary, token, "switch", "-c", "fixture-automatic");
            await WaitForRefreshAsync(session, sample.Primary, "fixture-automatic", "M.", token);
            GitWorktreeFixture.Write(sample.Primary, GitWorktreeFixture.ChangedPath, "old checkout changes again\n");
            await connection.ChangeAsync("git_select_repository", Address(linked, ""));
            GitWorktreeFixture.Write(sample.Linked, GitWorktreeFixture.ChangedPath, "linked external index update\n");
            await sample.GitAsync(sample.Linked, token, "add", "--", GitWorktreeFixture.ChangedPath);
            GitWorktreeFixture.Write(sample.Linked, GitWorktreeFixture.ChangedPath, "linked working change\n");
            var latest = await WaitForRefreshAsync(session, sample.Linked, "fixture-linked", "MM", token);
            VerifyRefresh(latest, sample.Linked, "fixture-linked", "MM");
            Assert.Empty(await ReadBadgeAsync(window, primary, GitWorktreeFixture.ChangedPath, token));

            Directory.Move(sample.Linked, sample.Linked + "-temporarily-offline");
            await WaitForConditionAsync(() => session.SourceControl.Status is null, "A missing checkout retained its old Git status.", token);
            Directory.Move(sample.Linked + "-temporarily-offline", sample.Linked);
            await WaitForRefreshAsync(session, sample.Linked, "fixture-linked", "MM", token);
        }
        finally { window.Close(); }
    }

    /// <summary>Holds a real controller read open across selection and overlapping refresh completions.</summary>
    [AvaloniaFact]
    public async Task Older_Git_status_reads_cannot_overwrite_current_desktop_status()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token);
        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var connection = await WorkspaceConnection.ConnectAsync(session, token);
        var primary = await connection.AddRootAsync(sample.Primary);
        var linked = await connection.AddRootAsync(sample.Linked);
        await connection.ChangeAsync("git_select_repository", Address(primary, ""));
        await WaitForConditionAsync(() => string.Equals(session.SourceControl.Status?.Branch, "main", StringComparison.Ordinal),
            "The primary watcher did not start.", token);

        var oldScope = await BeginHeldReadAsync(session, token);
        Assert.Equal(sample.Primary, oldScope.Selection.Repository.WorktreePath, NativePaths);
        await connection.ChangeAsync("git_select_repository", Address(linked, ""));
        await WaitForConditionAsync(() => string.Equals(session.SourceControl.Status?.Branch, "fixture-linked", StringComparison.Ordinal),
            "The linked watcher did not start.", token);
        Assert.Equal("git_selection_changed", (await oldScope.FinishAsync("obsolete-worktree")).Code);
        Assert.Equal("fixture-linked", session.SourceControl.Status?.Branch);

        var older = await BeginHeldReadAsync(session, token);
        var newer = await BeginHeldReadAsync(session, token);
        Assert.Equal("ok", (await newer.FinishAsync("fresh-status")).Code);
        Assert.Equal("ok", (await older.FinishAsync("superseded-status")).Code);
        Assert.True(string.Equals("fresh-status", session.SourceControl.Status?.Branch, StringComparison.Ordinal),
            "A superseded read replaced the current Git status.");
    }

    private static async Task<HeldGitRead> BeginHeldReadAsync(WorkbenchSession session, CancellationToken token)
    {
        var start = new TaskCompletionSource<GitSelection>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var features = new GitFeatures(async (selection, cancel) =>
        {
            start.TrySetResult(selection);
            var branch = await release.Task.WaitAsync(cancel).ConfigureAwait(false);
            return new GitStatus(selection.Repository, branch, null, null, null, null, []);
        });
        var actor = new ActorGrant("test-held-read", session.View.WorkspaceId, session.View.ViewKey, canMutate: false);
        var running = session.Controller.DispatchAsync(features.Status, new GitFeatures.StatusInput { DisplayOnly = true },
            context: null, actor, Guid.NewGuid(), token);
        return new HeldGitRead(await start.Task.WaitAsync(token).ConfigureAwait(false), release, running);
    }

    private sealed record HeldGitRead(GitSelection Selection, TaskCompletionSource<string> Release, Task<OperationResult> Running)
    {
        internal Task<OperationResult> FinishAsync(string branch)
        {
            Release.SetResult(branch);
            return Running;
        }
    }

    private static async Task WaitForConditionAsync(Func<bool> predicate, string failure, CancellationToken token)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow >= until)
                throw new Xunit.Sdk.XunitException(failure);
            await Task.Delay(30, token).ConfigureAwait(true);
        }
    }

    private static async Task<RefreshState> WaitForRefreshAsync(WorkbenchSession session, string path, string branch, string state, CancellationToken token)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < until)
        {
            if (session.SourceControl.Status is { } status && NativePaths.Equals(path, status.Repository.WorktreePath) &&
                string.Equals(status.Branch, branch, StringComparison.Ordinal) && FindChange(status) is { } entry &&
                string.Equals(entry.Index + entry.Worktree, state, StringComparison.Ordinal))
                return new RefreshState(status.Repository.WorktreePath, status.Branch, entry.Index + entry.Worktree);
            await Task.Delay(30, token).ConfigureAwait(true);
        }

        throw new Xunit.Sdk.XunitException($"Source Control did not automatically refresh {branch} to {state}.");
    }

    private static GitStatusEntry? FindChange(GitStatus status) => status.Entries.SingleOrDefault(entry =>
        string.Equals(entry.Path, GitWorktreeFixture.ChangedPath, StringComparison.Ordinal));
}
