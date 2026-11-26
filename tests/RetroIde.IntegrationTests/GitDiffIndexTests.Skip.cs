using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitDiffIndexTests
{
    /// <summary>Skips reviewed native sequencer steps and keeps recovery scoped to the selected checkout.</summary>
    /// <remarks>TEST-RETRO-013#ac-test-git-recovery.</remarks>
    [AvaloniaFact]
    public async Task Workbench_skips_reviewed_Git_sequence_steps()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var token = timeout.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token);
        var primary = sample.Primary;
        var linked = sample.Linked;
        var path = GitWorktreeFixture.ConflictPath;
        await sample.GitAsync(primary, token, "config", "--local", "user.name", "Retro Skip Test");
        await sample.GitAsync(primary, token, "config", "--local", "user.email", "skip@example.invalid");
        await sample.GitAsync(primary, token, "config", "--local", "commit.gpgsign", "false");
        await sample.GitAsync(primary, token, "config", "--local", "core.hooksPath",
            Directory.CreateDirectory(Path.Combine(primary, ".git", "skip-fixture-hooks")).FullName);
        var local = await CommitAsync(primary, path, "local edit\n", "Local edit");
        var first = await CommitAsync(linked, path, "first incoming edit\n", "First conflict");
        var second = await CommitAsync(linked, path, "second incoming edit\n", "Second conflict");
        const string FollowUp = "after-skip.txt";
        var last = await CommitAsync(linked, FollowUp, "third commit\n", "Third applies cleanly");

        await using var session = new WorkbenchSession(Guid.NewGuid());
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token);
        await using var stdio = await connection.ConnectStdioAsync(Path.Combine(primary, ".git", "skip-stdio.json"));
        var root = await connection.AddRootAsync(primary);
        await connection.AddRootAsync(linked);
        await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal)
        {
            ["rootId"] = root, ["relativePath"] = "",
        });

        Assert.NotEqual(0, (await sample.RunAsync(primary, token, "merge", "--no-edit", first)).ExitCode);
        var merge = await ReadAsync();
        Assert.Equal("git_operation_skip_unsupported", Code(await RecoverAsync("git_skip_operation", merge)));
        Assert.False(merge.GetProperty("canSkip").GetBoolean());
        Assert.Equal("ok", Code(await RecoverAsync("git_abort_operation", merge)));

        Assert.NotEqual(0, (await sample.RunAsync(primary, token, "cherry-pick", first, second, last)).ExitCode);
        var paused = await ReadAsync();
        Assert.Equal(first, paused.GetProperty("pendingCommit").GetString());
        Assert.True(paused.GetProperty("canSkip").GetBoolean());
        var advanced = await RecoverAsync("git_skip_operation", paused, stdio);
        Assert.Equal("git_operation_paused", Code(advanced));
        var next = advanced.GetProperty("data");
        Assert.Equal(second, next.GetProperty("pendingCommit").GetString());
        Assert.NotEqual(paused.GetProperty("revision").GetString(), next.GetProperty("revision").GetString());
        Assert.Equal("git_operation_changed", Code(await RecoverAsync("git_skip_operation", paused)));
        Assert.Equal(local, await HeadAsync(primary));
        await File.AppendAllTextAsync(Path.Combine(primary, ".git", "info", "exclude"), "\n/after-skip.txt\n", token);
        GitWorktreeFixture.Write(primary, FollowUp, "ignored local data\n");
        Assert.Equal("git_operation_local_changes", Code(await RecoverAsync("git_skip_operation", await ReadAsync())));
        Assert.Equal("ignored local data\n", await File.ReadAllTextAsync(Path.Combine(primary, FollowUp), token));
        Assert.Equal(second, (await ReadAsync()).GetProperty("pendingCommit").GetString());
        File.Delete(Path.Combine(primary, FollowUp));
        Assert.Equal("ok", Code(await RecoverAsync("git_skip_operation", await ReadAsync(), stdio)));
        Assert.Equal("none", (await ReadAsync()).GetProperty("kind").GetString());
        Assert.Equal("Third applies cleanly", (await sample.GitAsync(primary, token, "show", "-s", "--format=%s", "HEAD")).Trim());
        Assert.Equal("third commit\n", await File.ReadAllTextAsync(Path.Combine(primary, FollowUp), token));
        Assert.Equal("local edit\n", await File.ReadAllTextAsync(Path.Combine(primary, path), token));

        var later = await CommitAsync(primary, path, "later local edit\n", "Later edit");
        Assert.NotEqual(0, (await sample.RunAsync(primary, token, "revert", "--no-edit", local)).ExitCode);
        var revert = await ReadAsync();
        Assert.Equal("revert", revert.GetProperty("kind").GetString());
        Assert.Equal(local, revert.GetProperty("pendingCommit").GetString());
        Assert.Equal("ok", Code(await RecoverAsync("git_skip_operation", revert)));
        Assert.Equal(later, await HeadAsync(primary));

        Assert.NotEqual(0, (await sample.RunAsync(primary, token, "rebase", first)).ExitCode);
        var rebase = await ReadAsync();
        Assert.Equal("rebase", rebase.GetProperty("kind").GetString());
        Assert.Equal(local, rebase.GetProperty("pendingCommit").GetString());
        var rebased = await RecoverAsync("git_skip_operation", rebase, stdio);
        Assert.Equal("git_operation_paused", Code(rebased));
        Assert.Equal(later, rebased.GetProperty("data").GetProperty("pendingCommit").GetString());
        Assert.Equal("ok", Code(await RecoverAsync("git_skip_operation", rebased.GetProperty("data"))));
        Assert.Equal("main", (await sample.GitAsync(primary, token, "branch", "--show-current")).Trim());
        Assert.Equal("first incoming edit\n", await File.ReadAllTextAsync(Path.Combine(primary, path), token));
        Assert.Equal(last, await HeadAsync(linked));

        async Task<string> CommitAsync(string checkout, string file, string text, string message)
        {
            GitWorktreeFixture.Write(checkout, file, text);
            await sample.GitAsync(checkout, token, "add", "--", file).ConfigureAwait(false);
            await sample.GitAsync(checkout, token, "commit", "-m", message).ConfigureAwait(false);
            return await HeadAsync(checkout).ConfigureAwait(false);
        }

        async Task<string> HeadAsync(string checkout) => (await sample.GitAsync(checkout, token, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        async Task<JsonElement> ReadAsync()
        {
            var result = await connection.CallAsync("git_get_operation", new(StringComparer.Ordinal) { ["worktreePath"] = primary }).ConfigureAwait(false);
            Assert.Equal("ok", Code(result));
            return result.GetProperty("data");
        }

        async Task<JsonElement> RecoverAsync(string tool, JsonElement state, McpClient? transport = null) =>
            await connection.CallAsync(tool, new(StringComparer.Ordinal)
            {
                ["worktreePath"] = primary, ["revision"] = state.GetProperty("revision").GetString(),
                ["context"] = (await connection.CallAsync("workbench_get_context").ConfigureAwait(false)).GetProperty("context"),
            }, transport).ConfigureAwait(false);

        static string? Code(JsonElement response) => response.GetProperty("code").GetString();
    }
}
