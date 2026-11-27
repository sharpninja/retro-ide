namespace RetroIde.Application.Git;

/// <summary>Revalidates a captured native worktree after entering the shared write gate.</summary>
internal static class GitMutationLease
{
    internal static async Task<FeatureOutcome> RunAsync(GitWorktree expected, CancellationToken token, Func<Task<FeatureOutcome>> operation)
    {
        using var held = await TryAcquireAsync(expected, token).ConfigureAwait(false);
        return held is null ? WorktreeUnavailable() : await operation().ConfigureAwait(false);
    }

    internal static async Task<IDisposable?> TryAcquireAsync(GitWorktree expected, CancellationToken token)
    {
        var lease = await WorkspaceMutationGate.EnterAsync(token).ConfigureAwait(false);
        var current = false;
        try
        {
            current = await IsCurrentAsync(expected, token).ConfigureAwait(false);
            return current ? lease : null;
        }
        finally
        {
            if (!current)
                lease.Dispose();
        }
    }

    internal static async Task<bool> IsCurrentAsync(GitWorktree expected, CancellationToken token) =>
        await NativeGit.DiscoverAsync(expected.WorktreePath, "", token).ConfigureAwait(false) is { } found &&
        NativeGit.IsSameWorktree(found.Repository, expected);

    internal static FeatureOutcome WorktreeUnavailable() =>
        new("git_repository_unavailable", "The selected Git worktree is unavailable. Reopen it and review the operation again.");
}
