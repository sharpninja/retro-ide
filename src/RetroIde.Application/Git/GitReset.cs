using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RetroIde.Application.Git;

/// <summary>Reviews and resets the selected worktree to an immutable Git commit.</summary>
internal static class GitReset
{
    private static readonly byte[] RevisionKey = RandomNumberGenerator.GetBytes(32);
    private static readonly string[] Diff = ["diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--no-color"];

    internal sealed record ReviewInput(string WorktreePath, string TargetCommit);
    internal sealed record ResetInput(string WorktreePath, string TargetCommit, string Revision, string Mode);
    internal sealed record Review(GitWorktree Repository, string Branch, string? Head, string TargetCommit, string Revision,
        string[] IndexChanges, string[] HardTrackedChanges, string[] HardResetBlockedPaths);
    internal sealed record Result(GitWorktree Repository, string Mode, string? PreviousHead, string Head);

    internal static Task<FeatureOutcome> ReviewAsync(ReviewInput input, IWorkbenchRegistration view, CancellationToken token) =>
        WithReviewAsync(input.WorktreePath, input.TargetCommit, view, (selection, review) =>
            new FeatureOutcome("ok", "Git reset effects reviewed.")
            {
                Commit = current => GitOperationRecovery.SameSelection(current, selection)
                    ? FeatureOutcome.Success("Git reset effects reviewed.", review)
                    : GitOperationRecovery.ChangedSelection(),
            }, token);

    internal static Task<FeatureOutcome> ResetAsync(ResetInput input, IWorkbenchRegistration view, CancellationToken token)
    {
        if (input.Mode is not ("soft" or "mixed" or "hard"))
            return Task.FromResult(new FeatureOutcome("git_reset_mode_invalid", "Choose soft, mixed, or hard reset."));

        return WithReviewAsync(input.WorktreePath, input.TargetCommit, view, (selection, review) =>
        {
            if (Validate(review, input) is { } problem)
                return problem;
            return new FeatureOutcome("ok", "Git reset prepared.")
            {
                Commit = current => current is IGitRegistration git && GitOperationRecovery.SameSelection(current, selection)
                    ? new FeatureOutcome("ok", "Git reset authorized.")
                    {
                        ExecuteAuthorizedAsync = cancellation => GitOperationRecovery.TryAsync(
                            () => ExecuteAsync(git, selection, review, input, cancellation)),
                    }
                    : GitOperationRecovery.ChangedSelection(),
            };
        }, token);
    }

    private static Task<FeatureOutcome> WithReviewAsync(string path, string target, IWorkbenchRegistration view,
        Func<GitSelection, Review, FeatureOutcome> action, CancellationToken token)
    {
        if (!GitHistoryOperations.IsObjectId(target))
            return Task.FromResult(new FeatureOutcome("git_reset_target_invalid", "Supply the full commit ID to reset to."));
        return GitOperationRecovery.WithSelectionAsync(path, view, async (_, selection) =>
        {
            if (GitOperationRecovery.ReadKind(selection.Repository) is not null)
                return OperationInProgress();
            var review = await CaptureAsync(selection, target, token).ConfigureAwait(false);
            return review is null ? new("git_reset_target_unavailable", "The target is unavailable or worktree state changed during review.") :
                action(selection, review);
        }, token);
    }

    private static async Task<FeatureOutcome> ExecuteAsync(IGitRegistration git, GitSelection selection, Review reviewed, ResetInput input,
        CancellationToken token)
    {
        using var held = await GitMutationLease.TryAcquireAsync(selection.Repository, token).ConfigureAwait(false);
        if (held is null)
            return GitMutationLease.WorktreeUnavailable();
        var hard = input.Mode is "hard";
        var paths = hard ? reviewed.HardTrackedChanges.Union(reviewed.IndexChanges, StringComparer.Ordinal).ToArray() : [];
        var lease = await git.BeginIntegrationAsync(selection, paths, token).ConfigureAwait(false);
        if (lease.Guard is not { } guard)
            return new(lease.ErrorCode ?? "git_reset_editors_busy", "The checkout is busy or an affected editor has unsaved changes.");

        var invoked = false;
        try
        {
            var repo = selection.Repository;
            if (GitOperationRecovery.ReadKind(repo) is not null)
                return OperationInProgress();
            var review = await CaptureAsync(selection, input.TargetCommit, token).ConfigureAwait(false);
            if (review is null)
                return Changed();
            if (Validate(review, input) is { } problem)
                return problem;
            var args = NativeGit.WorktreeArguments(repo);
            args.AddRange(["-c", "submodule.recurse=false", "reset", "--" + input.Mode, "--no-recurse-submodules", review.TargetCommit, "--"]);
            token.ThrowIfCancellationRequested();
            invoked = true;
            var result = await NativeGit.RunCaptureAsync(repo.WorktreePath, args, CancellationToken.None).ConfigureAwait(false);
            if (result.ExitCode != 0)
                return File.Exists(Path.Combine(repo.GitDirectory, "index.lock"))
                    ? new("git_index_busy", "Another process owns the Git index lock.")
                    : new("git_reset_failed", "Git declined the reset. Review the selected worktree before retrying.");
            var head = await ReadAsync(repo, CancellationToken.None, "rev-parse", "--verify", "HEAD").ConfigureAwait(false);
            return FeatureOutcome.Success("Git checkout reset.",
                new Result(repo, input.Mode, review.Head, head?.Trim() ?? review.TargetCommit));
        }
        finally { await guard.CompleteAsync(hard && invoked).ConfigureAwait(false); }
    }

    private static async Task<Review?> CaptureAsync(GitSelection selection, string expectedTarget, CancellationToken token)
    {
        var repo = selection.Repository;
        var indexPath = Path.Combine(repo.GitDirectory, "index");
        var headPath = Path.Combine(repo.GitDirectory, "HEAD");
        var index = GitIndexLock.Read(indexPath);
        var symbolic = GitIndexLock.Read(headPath);
        var status = await NativeGit.ReadStatusAsync(selection, token).ConfigureAwait(false);
        if (status is null || status.OperationKind is not null)
            return null;
        var target = (await ReadAsync(repo, token, "rev-parse", "--verify", "--quiet", "--end-of-options", expectedTarget + "^{commit}")
            .ConfigureAwait(false))?.Trim();
        if (!string.Equals(target, expectedTarget, StringComparison.OrdinalIgnoreCase))
            return null;

        var indexChanges = await ReadPathsAsync(repo, token, [.. Diff, "--cached", "--name-only", "-z", target!, "--"]).ConfigureAwait(false);
        var worktreeChanges = await ReadPathsAsync(repo, token, [.. Diff, "--name-only", "-z", target!, "--"]).ConfigureAwait(false);
        var working = await NativeGit.ReadBytesAsync(repo.WorktreePath,
            [.. NativeGit.WorktreeArguments(repo), .. Diff, "--binary", "--full-index", target!, "--"], token).ConfigureAwait(false)
            ?? throw new InvalidDataException("Git could not review the bytes that a hard reset would change.");
        var blocked = await FindBlockedPathsAsync(repo, target!, status, token).ConfigureAwait(false);
        var head = (await ReadAsync(repo, token, "rev-parse", "--verify", "--quiet", "HEAD").ConfigureAwait(false))?.Trim();
        if (!string.Equals(head, status.Head, StringComparison.OrdinalIgnoreCase) ||
            !GitIndexLock.ContentsEqual(symbolic, GitIndexLock.Read(headPath)) ||
            !GitIndexLock.ContentsEqual(index, GitIndexLock.Read(indexPath)) || GitOperationRecovery.ReadKind(repo) is not null)
            return null;

        using var hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, RevisionKey);
        hash.AppendData(SHA256.HashData(working));
        hash.AppendData(SHA256.HashData(index ?? []));
        hash.AppendData(SHA256.HashData(symbolic ?? []));
        hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            repo.WorktreePath, repo.GitDirectory, status.Branch, status.Head, Target = target, status.Entries, indexChanges, worktreeChanges, blocked,
        }, WireJson.Options));
        return new(repo, status.Branch, status.Head, target!, Convert.ToHexString(hash.GetHashAndReset()), indexChanges, worktreeChanges, blocked);
    }

    private static async Task<string[]> FindBlockedPathsAsync(GitWorktree repo, string target, GitStatus status, CancellationToken token)
    {
        var local = status.Entries.Where(entry => entry.Worktree is "?" or "!").Select(entry => entry.Path.TrimEnd('/')).Distinct(StringComparer.Ordinal)
            .ToArray();
        if (local.Length == 0)
            return [];
        var tree = await ReadPathsAsync(repo, token, "ls-tree", "-r", "--full-tree", "--name-only", "-z", target).ConfigureAwait(false);
        return local.Where(path => GitOperationRecovery.Collides(tree, [path])).ToArray();
    }

    private static async Task<string[]> ReadPathsAsync(GitWorktree repo, CancellationToken token, params string[] command) =>
        (await ReadAsync(repo, token, command).ConfigureAwait(false))?.Split('\0', StringSplitOptions.RemoveEmptyEntries)
        ?? throw new InvalidDataException("Git could not review the paths affected by reset.");

    private static Task<string?> ReadAsync(GitWorktree repo, CancellationToken token, params string[] command) =>
        NativeGit.RunAsync(repo.WorktreePath, [.. NativeGit.WorktreeArguments(repo), .. command], token);

    private static FeatureOutcome? Validate(Review review, ResetInput input)
    {
        if (!string.Equals(review.Revision, input.Revision, StringComparison.Ordinal))
            return Changed();
        return input.Mode is "hard" && review.HardResetBlockedPaths.Length > 0
            ? new("git_reset_local_changes", "Move the untracked or ignored paths that a hard reset would overwrite.") : null;
    }

    private static FeatureOutcome Changed() => new("git_reset_changed", "HEAD, the index or worktree changed. Review the reset again.");
    private static FeatureOutcome OperationInProgress() =>
        new("git_operation_in_progress", "Finish or abort the paused Git operation before resetting this checkout.");
}
