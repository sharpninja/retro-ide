using Avalonia.Headless.XUnit;
using RetroIde.Application.Workspaces;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitDiffIndexTests
{
    /// <summary>Exercises the staged and working workflow through the live workbench's MCP tools.</summary>
    [AvaloniaFact]
    public async Task Workbench_reads_and_updates_literal_index_paths()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = timeout.Token;
        using var sample = await CreateDiffSampleAsync(token);
        await using var session = new WorkbenchSession(
            new WorkspaceExplorerStore(Path.Combine(sample.Primary, ".git", "retro-workspace.json")), null);
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token);
        var root = await connection.AddRootAsync(sample.Primary);
        await connection.AddRootAsync(sample.Linked);
        await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal)
        {
            ["rootId"] = root, ["relativePath"] = "",
        });
        var port = new McpPort(connection, sample.Primary);
        Assert.Contains("+literal path change", await port.DiffAsync(GitWorktreeFixture.ChangedPath, false), StringComparison.Ordinal);
        await VerifyDiffWorkflowAsync(port, sample, token);

        var indexPath = Path.Combine(sample.Primary, ".git", "index");
        var lockedIndex = indexPath + ".lock";
        var before = await File.ReadAllBytesAsync(indexPath, token);
        const string ExternalLock = "owned by another process";
        await File.WriteAllTextAsync(lockedIndex, ExternalLock, token);
        try
        {
            var blocked = await port.StageAsync(GitWorktreeFixture.ChangedPath);
            Assert.Equal("git_index_busy", blocked);
            Assert.Equal(ExternalLock, await File.ReadAllTextAsync(lockedIndex, token));
            Assert.Equal(before, await File.ReadAllBytesAsync(indexPath, token));
        }
        finally { File.Delete(lockedIndex); }

        Assert.Equal("git_scope_invalid", await port.StageAsync("src/../" + GitWorktreeFixture.ChangedPath));
        Assert.Equal("git_worktree_mismatch", await new McpPort(connection, sample.Linked).StageAsync(GitWorktreeFixture.ChangedPath));
        File.Delete(Path.Combine(sample.Primary, GitWorktreeFixture.HunkPath));
        GitWorktreeFixture.Write(sample.Primary, GitWorktreeFixture.HunkPath + "/unrelated.txt", "untracked descendant");
        Assert.Equal("git_scope_invalid", await port.StageAsync(GitWorktreeFixture.HunkPath));
        Assert.Equal(before, await File.ReadAllBytesAsync(indexPath, token));
    }

    private sealed class McpPort(GitWorkspaceStateTests.WorkspaceConnection connection, string worktreePath) : IDiffPort
    {
        public async Task<string> DiffAsync(string path, bool staged)
        {
            var arguments = Arguments(path);
            arguments["layer"] = staged ? "staged" : "working";
            var result = await connection.CallAsync("git_get_diff", arguments).ConfigureAwait(true);
            Assert.Equal("ok", result.GetProperty("code").GetString());
            return result.GetProperty("data").GetProperty("patch").GetString()!;
        }

        public async Task<string> SetStagedAsync(string path, bool staged)
        {
            var result = await connection.ChangeAsync(staged ? "git_stage_file" : "git_unstage_file", Arguments(path)).ConfigureAwait(true);
            return result.GetProperty("code").GetString()!;
        }

        internal async Task<string> StageAsync(string path)
        {
            var result = await connection.CallAsync("workbench_get_context").ConfigureAwait(true);
            var arguments = Arguments(path);
            arguments["context"] = result.GetProperty("context");
            var staged = await connection.CallAsync("git_stage_file", arguments).ConfigureAwait(true);
            return staged.GetProperty("code").GetString()!;
        }

        private Dictionary<string, object?> Arguments(string path) => new(StringComparer.Ordinal)
        {
            ["worktreePath"] = worktreePath,
            ["path"] = path,
        };
    }
}
