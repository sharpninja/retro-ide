using Xunit;
using Xunit.Sdk;

namespace RetroIde.IntegrationTests;

/// <summary>Defines reviewed Git commit and index invariants before binding the native handler.</summary>
/// <remarks>2026-11-26. TEST-RETRO-013#ac-test-git-stage-and-push. Exercises the stale review, configured hook, and selected-index outcomes independently of the Git implementation.</remarks>
public sealed partial class Phase03GitCommitContractTests
{
    private const string InitialHead = "initial-head";
    private const string Staged = "staged source\n";
    private const string UpdatedIndex = "new staged source\n";
    private const string Dirty = "unsaved worktree text\n";

    /// <summary>Accepts the reviewed index and detects unwanted ref, staged, and working changes.</summary>
    /// <remarks>2026-11-26. TEST-RETRO-013#ac-test-git-stage-and-push. Preserves one passing reference scenario and wrong-input controls for each review boundary.</remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("accept-stale-preimage")]
    [InlineData("ignore-rejected-hook")]
    [InlineData("write-working-copy")]
    [InlineData("commit-working-copy")]
    [InlineData("wrong-parent")]
    public async Task Reference_detects_review_and_commit_scope_faults(string? fault)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sample = new ReferenceCommitPort(fault);
        if (fault is null)
            await VerifyReviewedCommitAsync(sample, timeout.Token).ConfigureAwait(false);
        else
            await Assert.ThrowsAnyAsync<XunitException>(() => VerifyReviewedCommitAsync(sample, timeout.Token)).ConfigureAwait(false);
    }

    private static async Task VerifyReviewedCommitAsync(IReviewedCommitPort port, CancellationToken token)
    {
        var original = await port.ReadStateAsync(token).ConfigureAwait(false);
        Assert.Equal(Staged, original.IndexText);
        Assert.Equal(Dirty, original.WorktreeText);
        var reviewed = await port.ReviewAsync(token).ConfigureAwait(false);
        Assert.False(string.IsNullOrWhiteSpace(reviewed));

        await port.ReplaceIndexTextAsync(UpdatedIndex, token).ConfigureAwait(false);
        Assert.Equal("git_commit_state_changed", await port.CommitAsync(reviewed, token).ConfigureAwait(false));
        var unchanged = await port.ReadStateAsync(token).ConfigureAwait(false);
        Assert.Equal(original.Head, unchanged.Head);
        Assert.Equal(UpdatedIndex, unchanged.IndexText);
        Assert.Equal(Dirty, unchanged.WorktreeText);

        var revised = await port.ReviewAsync(token).ConfigureAwait(false);
        Assert.NotEqual(reviewed, revised);
        await port.ConfigureHookAsync(allow: false, token).ConfigureAwait(false);
        Assert.Equal("git_commit_failed", await port.CommitAsync(revised, token).ConfigureAwait(false));
        Assert.Equal(unchanged, await port.ReadStateAsync(token).ConfigureAwait(false));

        await port.ConfigureHookAsync(allow: true, token).ConfigureAwait(false);
        Assert.Equal("ok", await port.CommitAsync(revised, token).ConfigureAwait(false));
        var committed = await port.ReadStateAsync(token).ConfigureAwait(false);
        Assert.NotEqual(original.Head, committed.Head);
        Assert.Equal(original.Head, committed.Parent);
        Assert.Equal(UpdatedIndex, committed.CommittedText);
        Assert.Equal(UpdatedIndex, committed.IndexText);
        Assert.Equal(Dirty, committed.WorktreeText);
    }

    private sealed record CommitState(string Head, string? Parent, string CommittedText, string IndexText, string WorktreeText);

    private interface IReviewedCommitPort
    {
        Task<CommitState> ReadStateAsync(CancellationToken token);
        Task<string> ReviewAsync(CancellationToken token);
        Task ReplaceIndexTextAsync(string text, CancellationToken token);
        Task ConfigureHookAsync(bool allow, CancellationToken token);
        Task<string?> CommitAsync(string preimage, CancellationToken token);
    }

    private sealed class ReferenceCommitPort(string? fault) : IReviewedCommitPort
    {
        private CommitState _state = new(InitialHead, null, "prior revision\n", Staged, Dirty);
        private bool _allow = true;
        private int _indexVersion;

        public Task<CommitState> ReadStateAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(_state);
        }

        public Task<string> ReviewAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Preimage());
        }

        public Task ReplaceIndexTextAsync(string text, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _state = _state with { IndexText = text };
            _indexVersion++;
            return Task.CompletedTask;
        }

        public Task ConfigureHookAsync(bool allow, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _allow = allow;
            return Task.CompletedTask;
        }

        public Task<string?> CommitAsync(string preimage, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!string.Equals(Preimage(), preimage, StringComparison.Ordinal) && !HasFault("accept-stale-preimage"))
                return Task.FromResult<string?>("git_commit_state_changed");
            if (!_allow && !HasFault("ignore-rejected-hook"))
                return Task.FromResult<string?>("git_commit_failed");

            _state = _state with
            {
                Head = "newly-published",
                Parent = HasFault("wrong-parent") ? null : _state.Head,
                CommittedText = HasFault("commit-working-copy") ? _state.WorktreeText : _state.IndexText,
                WorktreeText = HasFault("write-working-copy") ? _state.IndexText : _state.WorktreeText,
            };
            return Task.FromResult<string?>("ok");
        }

        private string Preimage() => FormattableString.Invariant($"{_state.Head}-{_indexVersion}");
        private bool HasFault(string candidate) => string.Equals(fault, candidate, StringComparison.Ordinal);
    }
}
