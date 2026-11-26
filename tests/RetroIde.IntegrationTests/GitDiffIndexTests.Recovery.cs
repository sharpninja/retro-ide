using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitDiffIndexTests
{
    /// <summary>Recovers externally started Git operations without applying a reviewed state to a sibling worktree.</summary>
    /// <remarks>FR-RETRO-013; TR-RETRO-GIT-013; TEST-RETRO-013#ac-test-git-recovery.</remarks>
    [AvaloniaFact]
    public async Task Workbench_recovers_external_Git_operations()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var token = timeout.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token);
        var primary = sample.Primary;
        var linked = sample.Linked;
        var file = GitWorktreeFixture.ConflictPath;
        await sample.GitAsync(primary, token, "config", "--local", "user.name", "Retro Recovery Test");
        await sample.GitAsync(primary, token, "config", "--local", "user.email", "recovery@example.invalid");
        await sample.GitAsync(primary, token, "config", "--local", "commit.gpgsign", "false");
        await sample.GitAsync(primary, token, "config", "--local", "core.hooksPath",
            Directory.CreateDirectory(Path.Combine(primary, ".git", "recovery-fixture-hooks")).FullName);
        var local = await CommitFileAsync(primary, "primary edit\n");
        var incoming = await CommitFileAsync(linked, "linked edit\n");
        Assert.NotEqual(0, (await sample.RunAsync(primary, token, "merge", "--no-edit", incoming)).ExitCode);

        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token);
        await using var stdio = await connection.ConnectStdioAsync(Path.Combine(primary, ".git", "recovery-stdio.json"));
        var primaryRoot = await connection.AddRootAsync(primary);
        var linkedRoot = await connection.AddRootAsync(linked);
        await SelectAsync(primaryRoot);
        var operation = await ReadAsync(primary);
        Assert.Equal("merge", Kind(operation));
        Assert.Equal(file, operation.GetProperty("conflicts").EnumerateArray().Single().GetString());
        Assert.Equal("git_operation_conflicts", Code(await RecoverAsync("git_continue_operation", primary, operation, stdio)));
        Assert.Equal(local, await HeadAsync(primary));

        await SelectAsync(linkedRoot);
        Assert.Equal("none", Kind(await ReadAsync(linked)));
        Assert.Equal("git_worktree_mismatch", Code(await RecoverAsync("git_abort_operation", primary, operation)));
        await SelectAsync(primaryRoot);
        await connection.ChangeAsync("workspace_open_file", new(StringComparer.Ordinal)
        {
            ["rootId"] = primaryRoot, ["relativePath"] = file,
        });
        var view = Assert.IsType<DocumentViewModel>(session.Layout.CurrentSource);
        var initialConflict = view.Buffer.Text;
        await EditAsync("uncommitted editor content");
        Assert.Equal("git_operation_editors_busy", Code(await RecoverAsync("git_abort_operation", primary, await ReadAsync(primary))));
        Assert.Equal("uncommitted editor content", view.Buffer.Text);
        await EditAsync(initialConflict);

        GitWorktreeFixture.Write(primary, file, "reviewed merge resolution\n");
        Assert.Equal("git_operation_changed", Code(await RecoverAsync("git_abort_operation", primary, operation)));
        await StageAsync(primary);
        var commitState = await connection.CallAsync("git_get_commit_state", new(StringComparer.Ordinal) { ["worktreePath"] = primary });
        Assert.Equal("ok", Code(commitState));
        var context = await connection.CallAsync("workbench_get_context");
        var ordinaryCommit = await connection.CallAsync("git_commit", new(StringComparer.Ordinal)
        {
            ["worktreePath"] = primary, ["preimage"] = commitState.GetProperty("data").GetProperty("preimage").GetString(),
            ["context"] = context.GetProperty("context"), ["message"] = "Wrong operation",
        });
        Assert.Equal("git_operation_in_progress", Code(ordinaryCommit));
        var merged = await RecoverAsync("git_continue_operation", primary, await ReadAsync(primary), stdio);
        Assert.Equal("ok", Code(merged));
        Assert.Equal("none", Kind(await ReadAsync(primary)));
        Assert.Equal("reviewed merge resolution\n", view.Buffer.Text);
        Assert.Equal(local + " " + incoming, (await sample.GitAsync(primary, token, "show", "-s", "--format=%P", "HEAD")).Trim());
        var mergeHead = await HeadAsync(primary);

        await SelectAsync(linkedRoot);
        const string RestorePath = "rebase-restored-file.txt";
        GitWorktreeFixture.Write(linked, RestorePath, "tracked on original branch\n");
        await sample.GitAsync(linked, token, "add", "--", RestorePath);
        await sample.GitAsync(linked, token, "commit", "-m", "Path restored by abort");
        var originalTip = await HeadAsync(linked);
        Assert.NotEqual(0, (await sample.RunAsync(linked, token, "rebase", local)).ExitCode);
        var rebase = await ReadAsync(linked);
        Assert.Equal("rebase", Kind(rebase));
        Assert.Equal(file, rebase.GetProperty("conflicts").EnumerateArray().Single().GetString());
        GitWorktreeFixture.Write(linked, RestorePath, "untracked while rebase is paused\n");
        Assert.Equal("git_operation_local_changes", Code(await RecoverAsync("git_abort_operation", linked, await ReadAsync(linked))));
        Assert.Equal("untracked while rebase is paused\n", await File.ReadAllTextAsync(Path.Combine(linked, RestorePath), token));
        File.Delete(Path.Combine(linked, RestorePath));
        Assert.Equal("ok", Code(await RecoverAsync("git_abort_operation", linked, await ReadAsync(linked), stdio)));
        Assert.Equal(originalTip, await HeadAsync(linked));
        Assert.Equal("tracked on original branch\n", await File.ReadAllTextAsync(Path.Combine(linked, RestorePath), token));
        Assert.Equal("none", Kind(await ReadAsync(linked)));

        Assert.NotEqual(0, (await sample.RunAsync(linked, token, "rebase", local)).ExitCode);
        GitWorktreeFixture.Write(linked, file, "reviewed rebase resolution\n");
        await StageAsync(linked);
        Assert.Equal("ok", Code(await RecoverAsync("git_continue_operation", linked, await ReadAsync(linked))));
        Assert.Equal(local, (await sample.GitAsync(linked, token, "rev-parse", "HEAD~2")).Trim());
        Assert.Equal("reviewed rebase resolution\n", await File.ReadAllTextAsync(Path.Combine(linked, file), token));
        Assert.Equal(mergeHead, await HeadAsync(primary));

        await SelectAsync(primaryRoot);
        const string FollowUpPath = "cherry-pick-follow-up.txt";
        GitWorktreeFixture.Write(linked, FollowUpPath, "next commit in sequence\n");
        await sample.GitAsync(linked, token, "add", "--", FollowUpPath);
        await sample.GitAsync(linked, token, "commit", "-m", "Follow-up commit");
        var followUp = await HeadAsync(linked);
        Assert.NotEqual(0, (await sample.RunAsync(primary, token, "cherry-pick", incoming, followUp)).ExitCode);
        var cherryPick = await ReadAsync(primary);
        Assert.Equal("cherry-pick", Kind(cherryPick));
        Assert.Equal("ok", Code(await RecoverAsync("git_abort_operation", primary, cherryPick)));
        Assert.Equal(mergeHead, await HeadAsync(primary));
        Assert.False(File.Exists(Path.Combine(primary, FollowUpPath)));
        Assert.NotEqual(0, (await sample.RunAsync(primary, token, "cherry-pick", incoming, followUp)).ExitCode);
        GitWorktreeFixture.Write(primary, file, "reviewed cherry-pick resolution\n");
        await StageAsync(primary);
        Assert.Equal("ok", Code(await RecoverAsync("git_continue_operation", primary, await ReadAsync(primary), stdio)));
        Assert.Equal("next commit in sequence\n", await File.ReadAllTextAsync(Path.Combine(primary, FollowUpPath), token));
        var cherryPicked = await HeadAsync(primary);

        Assert.NotEqual(0, (await sample.RunAsync(primary, token, "revert", "--no-edit", local)).ExitCode);
        var revert = await ReadAsync(primary);
        Assert.Equal("revert", Kind(revert));
        Assert.Equal("ok", Code(await RecoverAsync("git_abort_operation", primary, revert, stdio)));
        Assert.Equal(cherryPicked, await HeadAsync(primary));
        Assert.NotEqual(0, (await sample.RunAsync(primary, token, "revert", "--no-edit", local)).ExitCode);
        GitWorktreeFixture.Write(primary, file, "reviewed revert resolution\n");
        await StageAsync(primary);
        Assert.Equal("ok", Code(await RecoverAsync("git_continue_operation", primary, await ReadAsync(primary))));
        Assert.Equal("none", Kind(await ReadAsync(primary)));
        Assert.Equal("reviewed revert resolution\n", await File.ReadAllTextAsync(Path.Combine(primary, file), token));
        Assert.Equal(followUp, await HeadAsync(linked));

        async Task<string> CommitFileAsync(string repo, string contents)
        {
            GitWorktreeFixture.Write(repo, file, contents);
            await sample.GitAsync(repo, token, "add", "--", file).ConfigureAwait(false);
            await sample.GitAsync(repo, token, "commit", "-m", contents.Trim()).ConfigureAwait(false);
            return await HeadAsync(repo).ConfigureAwait(false);
        }

        async Task<string> HeadAsync(string repo) => (await sample.GitAsync(repo, token, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();

        async Task SelectAsync(Guid root) => await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal)
        {
            ["rootId"] = root, ["relativePath"] = "",
        }).ConfigureAwait(false);

        async Task EditAsync(string text) => await connection.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
        {
            ["document"] = view.Capture(), ["offset"] = 0, ["length"] = view.Buffer.TextLength, ["text"] = text,
        });

        async Task StageAsync(string repo) => await connection.ChangeAsync("git_stage_file", new(StringComparer.Ordinal)
        {
            ["worktreePath"] = repo, ["path"] = file,
        }).ConfigureAwait(false);

        async Task<JsonElement> ReadAsync(string repo)
        {
            var result = await connection.CallAsync("git_get_operation", new(StringComparer.Ordinal) { ["worktreePath"] = repo }).ConfigureAwait(false);
            Assert.Equal("ok", Code(result));
            return result.GetProperty("data");
        }

        async Task<JsonElement> RecoverAsync(string tool, string repo, JsonElement reviewed, McpClient? transport = null)
        {
            var context = await connection.CallAsync("workbench_get_context").ConfigureAwait(false);
            return await connection.CallAsync(tool, new(StringComparer.Ordinal)
            {
                ["worktreePath"] = repo, ["revision"] = reviewed.GetProperty("revision").GetString(),
                ["context"] = context.GetProperty("context"),
            }, transport).ConfigureAwait(false);
        }

        static string? Kind(JsonElement result) => result.GetProperty("kind").GetString();
        static string? Code(JsonElement result) => result.GetProperty("code").GetString();
    }
}
