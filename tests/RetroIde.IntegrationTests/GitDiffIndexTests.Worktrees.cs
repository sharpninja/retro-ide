using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitDiffIndexTests
{
    /// <remarks>TEST-RETRO-013#ac-test-git-extensions.</remarks>
    [AvaloniaFact]
    public async Task Git_linked_worktrees_use_native_lifecycle_and_protect_local_changes()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(75));
        var token = deadline.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token).ConfigureAwait(true);
        await using var stdio = await connection.ConnectStdioAsync(Path.Combine(sample.Primary, ".git", "worktrees-stdio.json")).ConfigureAwait(true);
        var port = new WorktreePort(connection, stdio, sample.Primary);
        var primary = await connection.AddRootAsync(sample.Primary).ConfigureAwait(true);
        await port.SelectAsync(primary).ConfigureAwait(true);
        var baseline = await port.ListAsync().ConfigureAwait(true);
        var main = FindWorktree(baseline, sample.Primary);
        Assert.True(main.GetProperty("isPrimary").GetBoolean());
        Assert.False(FindWorktree(baseline, sample.Linked).GetProperty("isPrimary").GetBoolean());
        Assert.Equal("git_worktree_target_protected", Code(await port.MutateAsync("git_remove_worktree", ReviewWorktree(main)).ConfigureAwait(true)));
        var head = main.GetProperty("head").GetString();

        var destination = Path.Combine(Path.GetDirectoryName(sample.Primary)!, "Café [linked] #2");
        var added = await port.MutateAsync("git_add_worktree", new(StringComparer.Ordinal)
        {
            ["destination"] = destination, ["targetCommit"] = head, ["newBranch"] = "worktree/new-feature",
        }, stdio).ConfigureAwait(true);
        Assert.Equal("ok", Code(added));
        var target = added.GetProperty("data");
        Assert.Equal("refs/heads/worktree/new-feature", target.GetProperty("branch").GetString());
        Assert.True(File.Exists(Path.Combine(destination, ".git")));
        Assert.Equal(head, (await sample.GitAsync(destination, token, "rev-parse", "HEAD").ConfigureAwait(true)).Trim());

        var review = ReviewWorktree(target);
        review["reason"] = "Portable drive";
        Assert.Equal("ok", Code(await port.MutateAsync("git_lock_worktree", review).ConfigureAwait(true)));
        var locked = FindWorktree(await port.ListAsync().ConfigureAwait(true), destination);
        Assert.Equal("Portable drive", locked.GetProperty("lockReason").GetString());
        Assert.Equal("git_worktree_changed", Code(await port.MutateAsync("git_unlock_worktree", ReviewWorktree(target)).ConfigureAwait(true)));
        Assert.Equal("git_worktree_locked", Code(await port.MutateAsync("git_remove_worktree", ReviewWorktree(locked)).ConfigureAwait(true)));
        Assert.Equal("ok", Code(await port.MutateAsync("git_unlock_worktree", ReviewWorktree(locked), stdio).ConfigureAwait(true)));

        var opened = await port.MutateAsync("git_open_worktree", ReviewWorktree(target)).ConfigureAwait(true);
        Assert.Equal("ok", Code(opened));
        var newRoot = opened.GetProperty("data").GetProperty("root").GetProperty("id").GetGuid();
        Assert.Contains(session.Explorer.Roots, root => GitPathComparer.Equals(destination, root.Path));
        Assert.Equal(destination, session.SourceControl.Repository?.WorktreePath, GitPathComparer);
        await connection.ChangeAsync("workspace_open_file", new(StringComparer.Ordinal)
            { ["rootId"] = newRoot, ["relativePath"] = GitWorktreeFixture.ChangedPath }).ConfigureAwait(true);
        var editor = Assert.IsType<DocumentViewModel>(session.Layout.CurrentSource);
        var originalText = editor.Buffer.Text;
        await EditAsync("unsaved worktree draft").ConfigureAwait(true);
        await port.SelectAsync(primary).ConfigureAwait(true);
        Assert.Equal("git_worktree_editors_busy", Code(await port.MutateAsync("git_remove_worktree", ReviewWorktree(target)).ConfigureAwait(true)));
        Assert.Equal("unsaved worktree draft", editor.Buffer.Text);
        await EditAsync(originalText).ConfigureAwait(true);

        var localFile = Path.Combine(destination, "local draft");
        await File.WriteAllTextAsync(localFile, "keep this file", token).ConfigureAwait(true);
        Assert.Equal("git_worktree_dirty", Code(await port.MutateAsync("git_remove_worktree", ReviewWorktree(target)).ConfigureAwait(true)));
        Assert.Equal("keep this file", await File.ReadAllTextAsync(localFile, token).ConfigureAwait(true));
        File.Delete(localFile);
        Assert.Equal("ok", Code(await port.MutateAsync("git_remove_worktree", ReviewWorktree(target), stdio).ConfigureAwait(true)));
        Assert.False(Directory.Exists(destination));
        Assert.DoesNotContain(session.Explorer.Roots, root => GitPathComparer.Equals(destination, root.Path));
        Assert.Equal(head, (await sample.GitAsync(sample.Primary, token, "rev-parse", "HEAD").ConfigureAwait(true)).Trim());
        Assert.Equal(head, (await sample.GitAsync(sample.Primary, token, "rev-parse", "refs/heads/worktree/new-feature").ConfigureAwait(true)).Trim());
        Assert.True(Directory.Exists(sample.Linked));

        var attached = await port.MutateAsync("git_add_worktree", new(StringComparer.Ordinal)
        {
            ["destination"] = destination, ["targetCommit"] = head, ["existingBranch"] = "worktree/new-feature",
        }).ConfigureAwait(true);
        Assert.Equal("ok", Code(attached));
        Assert.Equal("refs/heads/worktree/new-feature", attached.GetProperty("data").GetProperty("branch").GetString());
        Assert.Equal("ok", Code(await port.MutateAsync("git_remove_worktree", ReviewWorktree(attached.GetProperty("data"))).ConfigureAwait(true)));

        async Task EditAsync(string text) =>
            await connection.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
            {
                ["document"] = editor.Capture(), ["offset"] = 0, ["length"] = editor.Buffer.TextLength, ["text"] = text,
            }).ConfigureAwait(false);
    }

    private static StringComparer GitPathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static JsonElement FindWorktree(JsonElement list, string path) =>
        Assert.Single(list.EnumerateArray(), row => GitPathComparer.Equals(row.GetProperty("path").GetString(), path));

    private static Dictionary<string, object?> ReviewWorktree(JsonElement row) => new(StringComparer.Ordinal)
    {
        ["targetPath"] = row.GetProperty("path").GetString(),
        ["expectedHead"] = row.GetProperty("head").GetString(),
        ["expectedBranch"] = row.TryGetProperty("branch", out var branch) ? branch.GetString() : null,
        ["expectedLocked"] = row.GetProperty("isLocked").GetBoolean(),
    };

    private static string? Code(JsonElement reply) => reply.GetProperty("code").GetString();

    private sealed class WorktreePort(GitWorkspaceStateTests.WorkspaceConnection connection, McpClient stdio, string worktree)
    {
        internal async Task SelectAsync(Guid root) =>
            await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal)
                { ["rootId"] = root, ["relativePath"] = "" }).ConfigureAwait(false);

        internal async Task<JsonElement> ListAsync()
        {
            var result = await connection.CallAsync("git_list_worktrees", new(StringComparer.Ordinal) { ["worktreePath"] = worktree }, stdio)
                .ConfigureAwait(false);
            Assert.Equal("ok", Code(result));
            return result.GetProperty("data");
        }

        internal async Task<JsonElement> MutateAsync(string tool, Dictionary<string, object?> args, McpClient? relay = null)
        {
            args["worktreePath"] = worktree;
            args["context"] = (await connection.CallAsync("workbench_get_context").ConfigureAwait(false)).GetProperty("context");
            return await connection.CallAsync(tool, args, relay).ConfigureAwait(false);
        }
    }
}
