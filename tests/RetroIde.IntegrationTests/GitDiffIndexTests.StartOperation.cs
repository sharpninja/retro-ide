using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitDiffIndexTests
{
    /// <summary>Starts reviewed Git operations and recovers their real native conflicts in the selected worktree.</summary>
    /// <remarks>TEST-RETRO-013#ac-test-git-recovery.</remarks>
    [AvaloniaFact]
    public async Task Workbench_starts_reviewed_Git_operations()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var token = timeout.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token);
        var primary = sample.Primary;
        var linked = sample.Linked;
        var path = GitWorktreeFixture.ConflictPath;
        await sample.GitAsync(primary, token, "config", "--local", "user.name", "Retro Operation Test");
        await sample.GitAsync(primary, token, "config", "--local", "user.email", "operations@example.invalid");
        await sample.GitAsync(primary, token, "config", "--local", "commit.gpgsign", "false");
        await sample.GitAsync(primary, token, "config", "--local", "core.hooksPath",
            Directory.CreateDirectory(Path.Combine(primary, ".git", "operation-fixture-hooks")).FullName);
        var local = await CommitAsync(primary, "local edit\n");
        var upstream = await CommitAsync(linked, "upstream edit\n");

        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token);
        await using var stdio = await connection.ConnectStdioAsync(Path.Combine(primary, ".git", "start-operation.json"));
        var root = await connection.AddRootAsync(primary);
        await connection.AddRootAsync(linked);
        await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal)
        {
            ["rootId"] = root, ["relativePath"] = "",
        });

        Assert.Equal("git_operation_head_changed", Code(await StartAsync("merge", local, upstream, branch: "refs/heads/stale")));
        Assert.Equal(local, await HeadAsync(primary));
        await connection.ChangeAsync("workspace_open_file", new(StringComparer.Ordinal) { ["rootId"] = root, ["relativePath"] = path });
        var view = Assert.IsType<DocumentViewModel>(session.Layout.CurrentSource);
        await EditAsync("unsaved editor text");
        Assert.Equal("git_operation_editors_busy", Code(await StartAsync("merge", local, upstream)));
        Assert.Equal("unsaved editor text", view.Buffer.Text);
        Assert.Equal(local, await HeadAsync(primary));
        await EditAsync("local edit\n");
        foreach (var kind in new[] { "merge", "rebase", "cherry-pick" })
        {
            var started = await StartAsync(kind, local, upstream, transport: stdio);
            Assert.Equal("git_operation_paused", Code(started));
            Assert.False(started.TryGetProperty("context", out _));
            var review = await ReviewAsync();
            Assert.Equal(kind, started.GetProperty("data").GetProperty("operation").GetProperty("kind").GetString());
            Assert.Equal(kind, review.GetProperty("kind").GetString());
            Assert.Equal(path, review.GetProperty("conflicts").EnumerateArray().Single().GetString());
            Assert.Equal("git_operation_in_progress", Code(await StartAsync("merge", local, upstream)));
            Assert.Equal("ok", Code(await MutateAsync("git_abort_operation", new(StringComparer.Ordinal)
            {
                ["worktreePath"] = primary, ["revision"] = review.GetProperty("revision").GetString(),
            })));
            Assert.Equal(local, await HeadAsync(primary));
        }

        var picked = await StartAsync("cherry-pick", local, upstream);
        Assert.Equal("git_operation_paused", Code(picked));
        GitWorktreeFixture.Write(primary, path, "resolved edit\n");
        await connection.ChangeAsync("git_stage_file", new(StringComparer.Ordinal) { ["worktreePath"] = primary, ["path"] = path });
        var resolved = await ReviewAsync();
        Assert.Equal("ok", Code(await MutateAsync("git_continue_operation", new(StringComparer.Ordinal)
        {
            ["worktreePath"] = primary, ["revision"] = resolved.GetProperty("revision").GetString(),
        }, stdio)));
        var applied = await HeadAsync(primary);
        Assert.Equal("ok", Code(await StartAsync("revert", applied, applied, transport: stdio)));
        Assert.NotEqual(applied, await HeadAsync(primary));
        Assert.Equal("local edit\n", await File.ReadAllTextAsync(Path.Combine(primary, path), token));
        Assert.Equal(upstream, await HeadAsync(linked));

        Assert.Equal("git_operation_paused", Code(await StartAsync("merge", await HeadAsync(primary), upstream)));
        GitWorktreeFixture.Write(primary, path, "two-parent merge resolution\n");
        await connection.ChangeAsync("git_stage_file", new(StringComparer.Ordinal) { ["worktreePath"] = primary, ["path"] = path });
        Assert.Equal("ok", Code(await MutateAsync("git_continue_operation", new(StringComparer.Ordinal)
        {
            ["worktreePath"] = primary, ["revision"] = (await ReviewAsync()).GetProperty("revision").GetString(),
        })));
        var merge = await HeadAsync(primary);
        Assert.Equal("git_operation_mainline_invalid", Code(await StartAsync("revert", merge, merge)));
        Assert.Equal("git_operation_mainline_invalid", Code(await StartAsync("revert", merge, merge, mainline: 3)));
        Assert.Equal(merge, await HeadAsync(primary));
        Assert.Equal("ok", Code(await StartAsync("revert", merge, merge, mainline: 1)));
        Assert.Equal("local edit\n", view.Buffer.Text);
        Assert.False(view.IsDirty);

        const string IgnoredPath = "worktree-local.cache";
        GitWorktreeFixture.Write(linked, IgnoredPath, "candidate addition\n");
        await sample.GitAsync(linked, token, "add", "--", IgnoredPath);
        await sample.GitAsync(linked, token, "commit", "-m", "Add the candidate path");
        var addition = await HeadAsync(linked);
        await File.AppendAllTextAsync(Path.Combine(primary, ".git", "info", "exclude"), "\n/worktree-local.cache\n", token);
        GitWorktreeFixture.Write(primary, IgnoredPath, "preserve my cache\n");
        var before = await HeadAsync(primary);
        Assert.Equal("git_operation_local_changes", Code(await StartAsync("cherry-pick", before, addition)));
        Assert.Equal("git_operation_local_changes", Code(await StartAsync("rebase", before, addition)));
        Assert.Equal("preserve my cache\n", await File.ReadAllTextAsync(Path.Combine(primary, IgnoredPath), token));
        Assert.Equal(before, await HeadAsync(primary));

        async Task<string> CommitAsync(string checkout, string text)
        {
            GitWorktreeFixture.Write(checkout, path, text);
            await sample.GitAsync(checkout, token, "add", "--", path).ConfigureAwait(false);
            await sample.GitAsync(checkout, token, "commit", "-m", text.Trim()).ConfigureAwait(false);
            return await HeadAsync(checkout).ConfigureAwait(false);
        }

        async Task<string> HeadAsync(string checkout) => (await sample.GitAsync(checkout, token, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();

        async Task EditAsync(string text) => await connection.ChangeAsync("editor_edit_text", new(StringComparer.Ordinal)
        {
            ["document"] = view.Capture(), ["offset"] = 0, ["length"] = view.Buffer.TextLength, ["text"] = text,
        });

        async Task<JsonElement> StartAsync(string kind, string head, string target, string branch = "refs/heads/main",
            McpClient? transport = null, int? mainline = null) =>
            await MutateAsync("git_start_operation", new(StringComparer.Ordinal)
            {
                ["worktreePath"] = primary, ["kind"] = kind, ["expectedHead"] = head, ["expectedBranch"] = branch, ["targetCommit"] = target,
                ["mainline"] = mainline,
            }, transport).ConfigureAwait(false);

        async Task<JsonElement> ReviewAsync()
        {
            var item = await connection.CallAsync("git_get_operation", new(StringComparer.Ordinal) { ["worktreePath"] = primary }).ConfigureAwait(false);
            Assert.Equal("ok", Code(item));
            return item.GetProperty("data");
        }

        async Task<JsonElement> MutateAsync(string tool, Dictionary<string, object?> arguments, McpClient? transport = null)
        {
            arguments["context"] = (await connection.CallAsync("workbench_get_context").ConfigureAwait(false)).GetProperty("context");
            return await connection.CallAsync(tool, arguments, transport).ConfigureAwait(false);
        }

        static string? Code(JsonElement result) => result.GetProperty("code").GetString();
    }
}
