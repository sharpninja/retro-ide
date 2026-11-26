using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitDiffIndexTests
{
    /// <summary>Checks native reset modes, reviewed state, and local data protection across worktrees.</summary>
    /// <remarks>TEST-RETRO-013#ac-test-git-recovery.</remarks>
    [AvaloniaFact]
    public async Task Workbench_resets_the_reviewed_Git_checkout()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var token = timeout.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token);
        var primary = sample.Primary;
        var file = GitWorktreeFixture.ConflictPath;
        const string Added = "added by commit.txt";
        var original = await HeadAsync(primary);
        var sibling = await HeadAsync(sample.Linked);
        GitWorktreeFixture.Write(primary, file, "committed local revision\n");
        GitWorktreeFixture.Write(primary, Added, "version recorded in Git\n");
        await sample.GitAsync(primary, token, "add", "--", file, Added);
        await sample.GitAsync(primary, token, "commit", "-m", "Reset candidate");
        var tip = await HeadAsync(primary);

        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token);
        await using var stdio = await connection.ConnectStdioAsync(Path.Combine(primary, ".git", "reset-stdio.json"));
        var root = await connection.AddRootAsync(primary);
        var linkedRoot = await connection.AddRootAsync(sample.Linked);
        await SelectAsync(root);
        await connection.ChangeAsync("workspace_open_file", new(StringComparer.Ordinal) { ["rootId"] = root, ["relativePath"] = file });
        var view = Assert.IsType<DocumentViewModel>(session.Layout.CurrentSource);

        var soft = await PreviewAsync(original);
        await SelectAsync(linkedRoot);
        Assert.Equal("git_worktree_mismatch", Code(await ResetAsync(soft, "soft")));
        await SelectAsync(root);
        Assert.Equal("ok", Code(await ResetAsync(soft, "soft", stdio)));
        Assert.Equal(original, await HeadAsync(primary));
        Assert.Equal("committed local revision\n", view.Buffer.Text);
        Assert.Contains(Added, await sample.GitAsync(primary, token, "diff", "--cached", "--name-only"), StringComparison.Ordinal);
        Assert.Empty(await sample.GitAsync(primary, token, "diff", "--name-only"));

        Assert.Equal("ok", Code(await ResetAsync(await PreviewAsync(original), "mixed")));
        Assert.Equal(original, await HeadAsync(primary));
        Assert.Empty(await sample.GitAsync(primary, token, "diff", "--cached", "--name-only"));
        Assert.Contains(file, await sample.GitAsync(primary, token, "diff", "--name-only"), StringComparison.Ordinal);
        Assert.Equal("committed local revision\n", await File.ReadAllTextAsync(Path.Combine(primary, file), token));

        GitWorktreeFixture.Write(primary, Added, "untracked local file\n");
        var collision = await PreviewAsync(tip);
        Assert.Contains(Added, collision.GetProperty("hardResetBlockedPaths").EnumerateArray().Select(path => path.GetString()));
        Assert.Equal("git_reset_local_changes", Code(await ResetAsync(collision, "hard", stdio)));
        Assert.Equal("untracked local file\n", await File.ReadAllTextAsync(Path.Combine(primary, Added), token));
        Assert.Equal(original, await HeadAsync(primary));

        var indexLock = Path.Combine(primary, ".git", "index.lock");
        var locked = await PreviewAsync(original);
        await File.WriteAllTextAsync(indexLock, "fixture lock", token);
        try
        {
            Assert.Equal("git_index_busy", Code(await ResetAsync(locked, "mixed")));
            Assert.Equal("git_index_busy", Code(await ResetAsync(locked, "hard")));
        }
        finally { File.Delete(indexLock); }

        var stale = await PreviewAsync(original);
        GitWorktreeFixture.Write(primary, file, "disk changed after review\n");
        Assert.Equal("git_reset_changed", Code(await ResetAsync(stale, "hard")));
        Assert.Equal("disk changed after review\n", await File.ReadAllTextAsync(Path.Combine(primary, file), token));

        var existingText = view.Buffer.Text;
        await EditAsync("unsaved editor text");
        Assert.Equal("git_reset_editors_busy", Code(await ResetAsync(await PreviewAsync(original), "hard")));
        Assert.Equal("unsaved editor text", view.Buffer.Text);
        await EditAsync(existingText);
        await connection.ChangeAsync("workspace_open_file", new(StringComparer.Ordinal)
        {
            ["rootId"] = root, ["relativePath"] = GitWorktreeFixture.HunkPath,
        });
        var unrelated = Assert.IsType<DocumentViewModel>(session.Layout.CurrentSource);
        await connection.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
        {
            ["document"] = unrelated.Capture(), ["offset"] = 0, ["length"] = unrelated.Buffer.TextLength, ["text"] = "unrelated draft",
        });
        Assert.Equal("ok", Code(await ResetAsync(await PreviewAsync(original), "hard", stdio)));
        Assert.Equal(original, await HeadAsync(primary));
        Assert.Equal(GitWorktreeFixture.SeedConflict, view.Buffer.Text);
        Assert.False(view.IsDirty);
        Assert.Equal("unrelated draft", unrelated.Buffer.Text);
        Assert.True(unrelated.IsDirty);
        Assert.Empty(await sample.GitAsync(primary, token, "diff", "--name-only"));
        Assert.Equal("untracked local file\n", await File.ReadAllTextAsync(Path.Combine(primary, Added), token));
        Assert.Equal(sibling, await HeadAsync(sample.Linked));

        async Task<string> HeadAsync(string checkout) => (await sample.GitAsync(checkout, token, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        async Task SelectAsync(Guid id) => await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal)
        {
            ["rootId"] = id, ["relativePath"] = "",
        }).ConfigureAwait(false);
        async Task EditAsync(string text) => await connection.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
        {
            ["document"] = view.Capture(), ["offset"] = 0, ["length"] = view.Buffer.TextLength, ["text"] = text,
        });
        async Task<JsonElement> PreviewAsync(string target)
        {
            var result = await connection.CallAsync("git_review_reset", new(StringComparer.Ordinal)
            {
                ["worktreePath"] = primary, ["targetCommit"] = target,
            }).ConfigureAwait(false);
            Assert.Equal("ok", Code(result));
            return result.GetProperty("data");
        }
        async Task<JsonElement> ResetAsync(JsonElement review, string mode, McpClient? transport = null) =>
            await connection.CallAsync("git_reset", new(StringComparer.Ordinal)
            {
                ["worktreePath"] = primary, ["targetCommit"] = review.GetProperty("targetCommit").GetString(),
                ["revision"] = review.GetProperty("revision").GetString(), ["mode"] = mode,
                ["context"] = (await connection.CallAsync("workbench_get_context").ConfigureAwait(false)).GetProperty("context"),
            }, transport).ConfigureAwait(false);

        static string? Code(JsonElement response) => response.GetProperty("code").GetString();
    }
}
