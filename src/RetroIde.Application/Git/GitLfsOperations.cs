namespace RetroIde.Application.Git;

internal static class GitLfsOperations
{
    internal sealed record StatusInput(string WorktreePath);
    internal sealed record ActionInput(string WorktreePath, string ExpectedRevision, string? Remote = null);
    internal enum Operation { Fetch, Checkout, Pull }

    internal static Task<FeatureOutcome> StatusAsync(StatusInput input, IWorkbenchRegistration view, CancellationToken token) =>
        WithSourceAsync(input.WorktreePath, view, async (_, selection) =>
        {
            var status = await GitLfsState.ReadAsync(selection.Repository, token).ConfigureAwait(false);
            return Publish(selection, () => FeatureOutcome.Success("Git LFS status read.", status));
        }, token);

    internal static Task<FeatureOutcome> RunAsync(ActionInput input, Operation operation, IWorkbenchRegistration view, CancellationToken token) =>
        WithSourceAsync(input.WorktreePath, view, (git, selection) => Task.FromResult(Publish(selection, () =>
            new FeatureOutcome("ok", "Git LFS operation authorized.")
            {
                ExecuteAuthorizedAsync = cancellation => GitOperationRecovery.TryAsync(
                    () => WorkspaceMutationGate.RunAsync(cancellation,
                        () => ExecuteAsync(git, selection, input, operation, cancellation))),
            })), token);

    private static async Task<FeatureOutcome> ExecuteAsync(IGitRegistration git, GitSelection selection, ActionInput input,
        Operation operation, CancellationToken token)
    {
        var repo = selection.Repository;
        if (!await SourceAvailableAsync(repo, token).ConfigureAwait(false))
            return SourceUnavailable();
        var status = await GitLfsState.ReadAsync(repo, token).ConfigureAwait(false);
        if (!status.IsInstalled)
            return new("git_lfs_unavailable", "Native Git LFS is unavailable on this host.");
        if (!string.Equals(status.Revision, input.ExpectedRevision, StringComparison.Ordinal))
            return Changed();
        if (status.Head is not { } head)
            return new("git_lfs_head_unavailable", "The selected worktree has no committed HEAD.");

        if (operation == Operation.Checkout)
            return await CheckoutAsync(git, selection, status, "Git LFS files checked out.", token).ConfigureAwait(false);
        if (input.Remote is not { } name || !status.Remotes.Contains(name, StringComparer.Ordinal))
            return new("git_lfs_remote_unavailable", "Choose a configured Git remote.");

        var fetched = await RunNativeAsync(repo, token, "fetch", "--", name, head).ConfigureAwait(false);
        if (fetched.ExitCode != 0)
            return new("git_lfs_fetch_failed", "Git LFS could not fetch the recorded commit. Check the selected remote and its authentication.");
        var cached = await GitLfsState.ReadAsync(repo, token).ConfigureAwait(false);
        if (!string.Equals(cached.Head, head, StringComparison.OrdinalIgnoreCase))
            return Changed();
        return operation == Operation.Pull
            ? await CheckoutAsync(git, selection, cached, "Git LFS files fetched and checked out.", token).ConfigureAwait(false)
            : FeatureOutcome.Success("Git LFS objects fetched.", cached);
    }

    private static async Task<FeatureOutcome> CheckoutAsync(IGitRegistration git, GitSelection selection, GitLfsState.Status reviewed,
        string summary, CancellationToken token)
    {
        var paths = reviewed.Files.Select(entry => entry.Path).ToArray();
        if (paths.Length == 0)
            return FeatureOutcome.Success(summary, reviewed);
        if (paths.Any(path => !IsSafePath(selection.Repository.WorktreePath, path)))
            return new("git_lfs_checkout_path", "One or more Git LFS paths traverse filesystem links or leave the selected worktree.");
        var lease = await git.BeginIntegrationAsync(selection, paths, token).ConfigureAwait(false);
        if (lease.Guard is not { } guard)
            return new(lease.ErrorCode ?? "git_lfs_editors_busy", "The selection changed or an affected editor has unsaved work.");

        var attempted = false;
        try
        {
            var repo = selection.Repository;
            var current = await GitLfsState.ReadAsync(repo, token).ConfigureAwait(false);
            if (!string.Equals(current.Revision, reviewed.Revision, StringComparison.Ordinal) ||
                paths.Any(path => !IsSafePath(repo.WorktreePath, path)))
                return Changed();
            token.ThrowIfCancellationRequested();
            attempted = true;
            var checkout = await RunNativeAsync(repo, CancellationToken.None, "checkout").ConfigureAwait(false);
            if (checkout.ExitCode != 0)
                return new("git_lfs_checkout_failed", "Native Git LFS could not check out the available objects.");
            return FeatureOutcome.Success(summary, await GitLfsState.ReadAsync(repo, CancellationToken.None).ConfigureAwait(false));
        }
        finally { await guard.CompleteAsync(attempted).ConfigureAwait(false); }
    }

    private static bool IsSafePath(string root, string relative)
    {
        var segments = relative.Split('/');
        if (Path.IsPathRooted(relative) || segments.Any(segment => segment is "" or "." or ".." ||
            segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            return false;
        var path = root;
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
            try
            {
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                    return false;
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return true; }
        }
        return true;
    }

    private static Task<NativeGit.CommandResult> RunNativeAsync(GitWorktree repo, CancellationToken token, params string[] args)
    {
        var command = NativeGit.WorktreeArguments(repo);
        // Git LFS uses attribute pathspec magic in its own Git subprocesses during checkout.
        command.Remove("--literal-pathspecs");
        command.AddRange(["-c", "protocol.ext.allow=never", "lfs", .. args]);
        return NativeGit.RunCaptureAsync(repo.WorktreePath, command, token);
    }

    private static Task<FeatureOutcome> WithSourceAsync(string path, IWorkbenchRegistration view,
        Func<IGitRegistration, GitSelection, Task<FeatureOutcome>> run, CancellationToken token) =>
        GitOperationRecovery.WithSelectionAsync(path, view, async (git, selection) =>
            await SourceAvailableAsync(selection.Repository, token).ConfigureAwait(false)
                ? await run(git, selection).ConfigureAwait(false) : SourceUnavailable(), token);

    private static async Task<bool> SourceAvailableAsync(GitWorktree repo, CancellationToken token) =>
        await NativeGit.DiscoverAsync(repo.WorktreePath, "", token).ConfigureAwait(false) is { } found &&
            NativeGit.IsSameWorktree(found.Repository, repo);

    private static FeatureOutcome Publish(GitSelection selection, Func<FeatureOutcome> run) =>
        new("ok", "Git LFS operation prepared.")
        {
            Commit = current => GitOperationRecovery.SameSelection(current, selection) ? run() : GitOperationRecovery.ChangedSelection(),
        };

    private static FeatureOutcome SourceUnavailable() => new("git_repository_unavailable", "The selected Git worktree is unavailable.");
    private static FeatureOutcome Changed() => new("git_lfs_changed", "Git LFS state changed. Refresh its status and review the operation again.");
}
