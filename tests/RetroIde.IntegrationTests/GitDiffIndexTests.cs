using RetroIde.Application.Git;
using RetroIde.Testing;
using Xunit;
using Xunit.Sdk;

namespace RetroIde.IntegrationTests;

/// <summary>Exercises staged and working changes against a real repository and its linked worktree.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-stage-and-push; TR-RETRO-GIT-013#ac-git-machine-state.</remarks>
public sealed partial class GitDiffIndexTests
{
    private const string StagedMark = "staged line two";
    private const string WorkingMark = "working line nineteen";

    /// <summary>Preserves an unrelated Git process's index edit between preparation and publication.</summary>
    [Fact]
    public async Task Prepared_index_rejects_a_concurrent_native_update()
    {
        var token = TestContext.Current.CancellationToken;
        using var sample = await CreateDiffSampleAsync(token);
        var selection = await NativeGit.DiscoverAsync(sample.Primary, "", token).ConfigureAwait(true);
        Assert.NotNull(selection);
        var indexPath = Path.Combine(selection.Repository.GitDirectory, "index");
        using var prepared = await PreparedGitIndex.CreateAsync(selection.Repository,
            [GitWorktreeFixture.ChangedPath], stage: true, token).ConfigureAwait(true);
        Assert.NotNull(prepared);

        await sample.GitAsync(sample.Primary, token, "add", "--", GitWorktreeFixture.HunkPath).ConfigureAwait(true);
        var current = await File.ReadAllBytesAsync(indexPath, token).ConfigureAwait(true);
        Assert.Equal("git_index_changed", prepared.Publish());
        Assert.Equal(current, await File.ReadAllBytesAsync(indexPath, token).ConfigureAwait(true));
        Assert.False(File.Exists(indexPath + ".lock"));
    }

    /// <summary>Proves the native fixtures distinguish index layers and detect an operation on the wrong checkout.</summary>
    [Theory]
    [InlineData("swapped-diff-layers")]
    [InlineData("wrong-worktree")]
    public async Task Reference_detects_diff_and_index_scope_faults(string fault)
    {
        var token = TestContext.Current.CancellationToken;
        using var healthy = await CreateDiffSampleAsync(token).ConfigureAwait(true);
        await VerifyDiffWorkflowAsync(new NativePort(healthy, token), healthy, token).ConfigureAwait(true);
        using var broken = await CreateDiffSampleAsync(token).ConfigureAwait(true);
        await Assert.ThrowsAnyAsync<XunitException>(() =>
            VerifyDiffWorkflowAsync(new NativePort(broken, token, fault), broken, token)).ConfigureAwait(true);
    }

    private static async Task<GitWorktreeFixture> CreateDiffSampleAsync(CancellationToken token)
    {
        var sample = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(false);
        try
        {
            var staged = GitWorktreeFixture.SeedHunks.Replace("line 2\n", StagedMark + "\n", StringComparison.Ordinal);
            GitWorktreeFixture.Write(sample.Primary, GitWorktreeFixture.HunkPath, staged);
            await sample.GitAsync(sample.Primary, token, "--literal-pathspecs", "add", "--", GitWorktreeFixture.HunkPath).ConfigureAwait(false);
            GitWorktreeFixture.Write(sample.Primary, GitWorktreeFixture.HunkPath,
                staged.Replace("line 19\n", WorkingMark + "\n", StringComparison.Ordinal));
            GitWorktreeFixture.Write(sample.Primary, GitWorktreeFixture.ChangedPath, "literal path change\n");
            GitWorktreeFixture.Write(sample.Primary, "src/Café d #1.cs", "pattern match decoy\n");
            return sample;
        }
        catch { sample.Dispose(); throw; }
    }

    private static async Task VerifyDiffWorkflowAsync(IDiffPort port, GitWorktreeFixture sample, CancellationToken token)
    {
        var path = GitWorktreeFixture.HunkPath;
        var working = await port.DiffAsync(path, staged: false).ConfigureAwait(true);
        var staged = await port.DiffAsync(path, staged: true).ConfigureAwait(true);
        Assert.Contains("+" + WorkingMark, working, StringComparison.Ordinal);
        Assert.DoesNotContain(StagedMark, working, StringComparison.Ordinal);
        Assert.Contains("+" + StagedMark, staged, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkingMark, staged, StringComparison.Ordinal);

        Assert.Equal("ok", await port.SetStagedAsync(path, true).ConfigureAwait(true));
        staged = await port.DiffAsync(path, staged: true).ConfigureAwait(true);
        Assert.Contains("+" + StagedMark, staged, StringComparison.Ordinal);
        Assert.Contains("+" + WorkingMark, staged, StringComparison.Ordinal);
        Assert.Empty(await port.DiffAsync(path, staged: false).ConfigureAwait(true));

        Assert.Equal("ok", await port.SetStagedAsync(path, false).ConfigureAwait(true));
        Assert.Empty(await port.DiffAsync(path, staged: true).ConfigureAwait(true));
        working = await port.DiffAsync(path, staged: false).ConfigureAwait(true);
        Assert.Contains("+" + StagedMark, working, StringComparison.Ordinal);
        Assert.Contains("+" + WorkingMark, working, StringComparison.Ordinal);

        Assert.Equal("ok", await port.SetStagedAsync(GitWorktreeFixture.ChangedPath, true).ConfigureAwait(true));
        var changed = await sample.GitAsync(sample.Primary, token, "diff", "--cached", "--name-only", "-z").ConfigureAwait(true);
        Assert.Equal(GitWorktreeFixture.ChangedPath + "\0", changed);
        Assert.Empty(await sample.GitAsync(sample.Linked, token, "diff", "--cached", "--name-only", "-z").ConfigureAwait(true));
        Assert.Equal("ok", await port.SetStagedAsync(GitWorktreeFixture.ChangedPath, false).ConfigureAwait(true));
        var raw = File.ReadAllText(Path.Combine(sample.Primary, GitWorktreeFixture.ChangedPath));
        Assert.Equal("literal path change\n", raw);
    }

    private interface IDiffPort
    {
        Task<string> DiffAsync(string path, bool staged);
        Task<string> SetStagedAsync(string path, bool staged);
    }

    private sealed class NativePort(GitWorktreeFixture sample, CancellationToken token, string? fault = null) : IDiffPort
    {
        public Task<string> DiffAsync(string path, bool staged) =>
            sample.GitAsync(sample.Primary, token,
                (staged != string.Equals(fault, "swapped-diff-layers", StringComparison.Ordinal))
                    ? ["--literal-pathspecs", "diff", "--cached", "--", path]
                    : ["--literal-pathspecs", "diff", "--", path]);

        public async Task<string> SetStagedAsync(string path, bool staged)
        {
            var checkout = string.Equals(fault, "wrong-worktree", StringComparison.Ordinal) ? sample.Linked : sample.Primary;
            await sample.GitAsync(checkout, token, staged
                ? ["--literal-pathspecs", "add", "--", path]
                : ["--literal-pathspecs", "reset", "--", path]).ConfigureAwait(false);
            return "ok";
        }
    }
}
