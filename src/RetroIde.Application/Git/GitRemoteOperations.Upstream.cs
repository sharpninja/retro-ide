namespace RetroIde.Application.Git;

internal static partial class GitRemoteOperations
{
    internal sealed record UpstreamInput(string WorktreePath, string BranchName, string ExpectedHead, string? Upstream = null);
    internal sealed record UpstreamResult(GitWorktree Repository, string BranchName, string? Upstream = null);

    internal static Task<FeatureOutcome> SetUpstreamAsync(UpstreamInput input, IWorkbenchRegistration view, CancellationToken token) =>
        InScopeAsync(input.WorktreePath, view, async selection =>
        {
            var repo = selection.Repository;
            var problem = await ValidateUpstreamAsync(repo, input, token).ConfigureAwait(false);
            if (problem is not null)
                return problem;

            return Authorize(selection, async cancellation =>
            {
                using var held = await GitConfigurationGate.EnterAsync(cancellation).ConfigureAwait(false);
                var current = await ValidateUpstreamAsync(repo, input, cancellation).ConfigureAwait(false);
                if (current is not null)
                    return current;
                string[] command = input.Upstream is { } upstream
                    ? ["branch", "--set-upstream-to=" + upstream, "--", input.BranchName]
                    : ["branch", "--unset-upstream", "--", input.BranchName];
                cancellation.ThrowIfCancellationRequested();
                var result = await RunAsync(repo, CancellationToken.None, command).ConfigureAwait(false);
                return result.ExitCode == 0
                    ? FeatureOutcome.Success("Git branch tracking updated.", new UpstreamResult(repo, input.BranchName, input.Upstream))
                    : new("git_upstream_update_failed", "Git could not update the branch upstream.");
            });
        }, token);

    private static async Task<FeatureOutcome?> ValidateUpstreamAsync(GitWorktree repo, UpstreamInput input, CancellationToken token)
    {
        if (!GitHistoryOperations.IsObjectId(input.ExpectedHead))
            return new("git_branch_object_invalid", "Supply a full expected commit object ID.");
        var refs = await GitHistoryOperations.ReadRefsAsync(repo, token).ConfigureAwait(false);
        var branch = Array.Find(refs.Refs, reference => string.Equals(reference.Name, "refs/heads/" + input.BranchName, StringComparison.Ordinal));
        if (branch is null)
            return new("git_branch_unavailable", "The selected local branch is unavailable.");
        if (!string.Equals(branch.ObjectId, input.ExpectedHead, StringComparison.OrdinalIgnoreCase))
            return new("git_branch_changed", "The branch moved. Refresh references and try again.");

        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (refs.Worktrees.Any(tree => string.Equals(tree.Branch, branch.Name, StringComparison.Ordinal) &&
            !string.Equals(tree.Path, repo.WorktreePath, pathComparison)))
            return new("git_branch_in_use", "The local branch is checked out in another worktree.");
        if (input.Upstream is { } upstream &&
            ((!upstream.StartsWith("refs/remotes/", StringComparison.Ordinal) &&
              !upstream.StartsWith("refs/heads/", StringComparison.Ordinal)) ||
             string.Equals(upstream, branch.Name, StringComparison.Ordinal) ||
             !refs.Refs.Any(reference => string.Equals(reference.Name, upstream, StringComparison.Ordinal))))
            return new("git_upstream_unavailable", "Choose an existing local or remote-tracking branch as the upstream.");

        return null;
    }
}
