using System.Text.Json;
using Avalonia.Headless.XUnit;
using RetroIde.Application.Workspaces;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class GitDiffIndexTests
{
    /// <summary>Stages and unstages one hunk while detecting changes to the patch shown to the caller.</summary>
    /// <remarks>TEST-RETRO-013#ac-test-git-stage-and-push; TR-RETRO-GIT-013#ac-git-state-consistency.</remarks>
    [AvaloniaFact]
    public async Task Workbench_applies_only_selected_hunks_and_rejects_stale_patches()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = timeout.Token;
        using var sample = await CreateDiffSampleAsync(token);
        var path = GitWorktreeFixture.HunkPath;
        await sample.GitAsync(sample.Primary, token, "reset", "--quiet", "--", path);
        await using var session = new WorkbenchSession(
            new WorkspaceExplorerStore(Path.Combine(sample.Primary, ".git", "retro-workspace.json")), null);
        await using var connection = await GitWorkspaceStateTests.WorkspaceConnection.ConnectAsync(session, token);
        await using var stdio = await connection.ConnectStdioAsync(Path.Combine(sample.Primary, ".git", "retro-stdio.json"));
        var root = await connection.AddRootAsync(sample.Primary);
        await connection.ChangeAsync("git_select_repository", new(StringComparer.Ordinal)
        {
            ["rootId"] = root, ["relativePath"] = "",
        });

        var stale = await ReadDiffAsync("working");
        var workingPath = Path.Combine(sample.Primary, path);
        var originalWorking = await File.ReadAllTextAsync(workingPath, token);
        var updatedWorking = originalWorking.Replace("line 10", "external line ten", StringComparison.Ordinal);
        GitWorktreeFixture.Write(sample.Primary, path, updatedWorking);
        Assert.Equal("git_diff_changed", await ChangeHunksAsync("git_stage_hunks", stale, [2]));
        Assert.Empty(await sample.GitAsync(sample.Primary, token, "diff", "--cached", "--", path));

        var beforeExternalStage = await ReadDiffAsync("working");
        var externalIndex = GitWorktreeFixture.SeedHunks.Replace("line 2\n", "external index line\n", StringComparison.Ordinal);
        GitWorktreeFixture.Write(sample.Primary, path, externalIndex);
        await sample.GitAsync(sample.Primary, token, "add", "--", path);
        GitWorktreeFixture.Write(sample.Primary, path, updatedWorking);
        Assert.Equal("git_diff_changed", await ChangeHunksAsync("git_stage_hunks", beforeExternalStage, [2]));
        Assert.Equal(externalIndex, await sample.GitAsync(sample.Primary, token, "show", ':' + path));
        await sample.GitAsync(sample.Primary, token, "reset", "--quiet", "--", path);

        var current = await ReadDiffAsync("working");
        Assert.True(current.GetProperty("hunks").GetArrayLength() >= 2);
        var second = current.GetProperty("hunks").EnumerateArray().Last().GetProperty("id").GetInt32();
        Assert.Equal("ok", await ChangeHunksAsync("git_stage_hunks", current, [second]));
        var staged = await ReadDiffAsync("staged");
        var stagedPatch = staged.GetProperty("patch").GetString();
        Assert.Contains("+" + WorkingMark, stagedPatch, StringComparison.Ordinal);
        Assert.DoesNotContain(StagedMark, stagedPatch, StringComparison.Ordinal);
        Assert.DoesNotContain("external line ten", stagedPatch, StringComparison.Ordinal);
        Assert.Empty(await sample.GitAsync(sample.Linked, token, "diff", "--cached", "--", path));

        Assert.Equal("ok", await ChangeHunksAsync("git_unstage_hunks", staged, [1]));
        Assert.Empty(await sample.GitAsync(sample.Primary, token, "diff", "--cached", "--", path));
        Assert.Equal(updatedWorking, await File.ReadAllTextAsync(workingPath, token));
        Assert.Equal("git_diff_changed", await ChangeHunksAsync("git_unstage_hunks", staged, [1]));

        current = await ReadDiffAsync("working");
        Assert.Equal("ok", await ChangeHunksAsync("git_stage_hunks", current, [1, second]));
        var twoHunks = (await ReadDiffAsync("staged")).GetProperty("patch").GetString();
        Assert.Contains("+" + StagedMark, twoHunks, StringComparison.Ordinal);
        Assert.Contains("+" + WorkingMark, twoHunks, StringComparison.Ordinal);
        Assert.DoesNotContain("external line ten", twoHunks, StringComparison.Ordinal);

        var legacyPath = GitWorktreeFixture.ChangedPath;
        await File.WriteAllBytesAsync(Path.Combine(sample.Primary, legacyPath), [(byte)'L', 0xe9, (byte)'\n'], token);
        var legacy = await ReadDiffAsync("working", legacyPath);
        Assert.Equal("ok", await ChangeHunksAsync("git_stage_hunks", legacy, [1], legacyPath));
        Assert.Equal(await sample.GitAsync(sample.Primary, token, "hash-object", "--", legacyPath),
            await sample.GitAsync(sample.Primary, token, "rev-parse", ':' + legacyPath));

        async Task<JsonElement> ReadDiffAsync(string layer, string? selectedPath = null)
        {
            var result = await connection.CallAsync("git_get_diff", new(StringComparer.Ordinal)
            {
                ["worktreePath"] = sample.Primary, ["path"] = selectedPath ?? path, ["layer"] = layer,
            });
            Assert.Equal("ok", result.GetProperty("code").GetString());
            var data = result.GetProperty("data");
            Assert.True(data.TryGetProperty("hunks", out _), "The selected Git diff has no selectable hunk contract.");
            return data;
        }

        async Task<string> ChangeHunksAsync(string tool, JsonElement diff, int[] hunks, string? selectedPath = null)
        {
            var context = await connection.CallAsync("workbench_get_context");
            var result = await connection.CallAsync(tool, new(StringComparer.Ordinal)
            {
                ["worktreePath"] = sample.Primary, ["path"] = selectedPath ?? path,
                ["preimage"] = diff.GetProperty("preimage").GetString(), ["hunkIds"] = hunks,
                ["context"] = context.GetProperty("context"),
            }, stdio);
            return result.GetProperty("code").GetString()!;
        }
    }
}
