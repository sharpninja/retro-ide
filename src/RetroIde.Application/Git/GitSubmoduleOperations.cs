namespace RetroIde.Application.Git;

internal static class GitSubmoduleOperations
{
    internal sealed record ListInput(string WorktreePath);
    internal sealed record TargetInput(string WorktreePath, string Path, string ExpectedRevision);
    internal enum Operation { Init, Update, Sync }

    internal static Task<FeatureOutcome> ListAsync(ListInput input, IWorkbenchRegistration view, CancellationToken token) =>
        WithSourceAsync(input.WorktreePath, view, async (_, selection) =>
        {
            var modules = await GitSubmoduleState.ReadAsync(selection.Repository, token).ConfigureAwait(false);
            return Publish(selection, () => FeatureOutcome.Success("Git submodules read.", modules.Select(entry => entry.Module).ToArray()));
        }, token);

    internal static Task<FeatureOutcome> RunAsync(TargetInput input, Operation operation, IWorkbenchRegistration view, CancellationToken token) =>
        WithSourceAsync(input.WorktreePath, view, (git, selection) => Task.FromResult(Publish(selection, () =>
            new FeatureOutcome("ok", "Git submodule operation authorized.")
            {
                ExecuteAuthorizedAsync = cancel => GitOperationRecovery.TryAsync(() => ExecuteAsync(git, selection, input, operation, cancel)),
            })), token);

    private static async Task<FeatureOutcome> ExecuteAsync(IGitRegistration git, GitSelection selection, TargetInput input,
        Operation operation, CancellationToken token)
    {
        var repo = selection.Repository;
        if (!await SourceAvailableAsync(repo, token).ConfigureAwait(false))
            return SourceUnavailable();
        var (reviewed, initialFailure) = await ReviewAsync(repo, input, token).ConfigureAwait(false);
        if (initialFailure is not null)
            return initialFailure;

        using var held = await WorkspaceMutationGate.EnterAsync(token).ConfigureAwait(false);
        if (!await SourceAvailableAsync(repo, token).ConfigureAwait(false))
            return SourceUnavailable();
        var (target, failure) = await ReviewAsync(repo, input, token).ConfigureAwait(false);
        if (failure is not null)
            return failure;
        if (!SameCheckout(reviewed!.Checkout, target!.Checkout))
            return Changed();
        if (operation == Operation.Update)
            return await UpdateAsync(git, selection, input, target, token).ConfigureAwait(false);
        if (operation == Operation.Sync && !target.Module.IsConfigured)
            return new("git_submodule_not_configured", "Initialize the selected submodule before syncing its URL.");
        return await InvokeAsync(repo, target, operation, token).ConfigureAwait(false);
    }

    private static bool SameCheckout(GitWorktree? expected, GitWorktree? current) => expected is null
        ? current is null
        : current is not null && NativeGit.IsSameWorktree(expected, current);

    private static async Task<FeatureOutcome> UpdateAsync(IGitRegistration git, GitSelection selection, TargetInput input,
        GitSubmoduleState.Entry reviewed, CancellationToken token)
    {
        if (reviewed.Module.HasCheckout && reviewed.Module.IsConfigured &&
            string.Equals(reviewed.Module.Head, reviewed.Module.IndexCommit, StringComparison.OrdinalIgnoreCase))
            return FeatureOutcome.Success("The submodule is at the recorded commit.", reviewed.Module);

        var lease = await git.BeginIntegrationAsync(selection, paths: null, token, reviewed.FullPath!).ConfigureAwait(false);
        if (lease.ErrorCode is not null)
            return GitOperationRecovery.ChangedSelection();
        if (lease.Guard is not { } guard)
            return new("git_submodule_editors_busy", "Save or close changed editors in the submodule before updating it.");

        var attempted = false;
        try
        {
            var (target, failure) = await ReviewAsync(selection.Repository, input, token).ConfigureAwait(false);
            if (failure is not null)
                return failure;
            if (!SameCheckout(reviewed.Checkout, target!.Checkout))
                return Changed();
            if (await HasLocalChangesAsync(target, token).ConfigureAwait(false))
                return new("git_submodule_dirty", "Save or move local submodule files and finish any Git operation before updating.");
            token.ThrowIfCancellationRequested();
            attempted = true;
            return await InvokeAsync(selection.Repository, target, Operation.Update, token).ConfigureAwait(false);
        }
        finally
        {
            // The native command can initialize a checkout or change files before reporting a failure.
            await guard.CompleteAsync(attempted).ConfigureAwait(false);
        }
    }

    private static async Task<bool> HasLocalChangesAsync(GitSubmoduleState.Entry target, CancellationToken token)
    {
        if (target.Checkout is not { } checkout)
            return Directory.Exists(target.FullPath) && Directory.EnumerateFileSystemEntries(target.FullPath!).Any();
        var status = await NativeGit.ReadStatusAsync(new GitSelection(checkout, checkout.WorktreePath), token).ConfigureAwait(false);
        return status is null || status.OperationKind is not null || status.Entries.Length != 0;
    }

    private static async Task<FeatureOutcome> InvokeAsync(GitWorktree repo, GitSubmoduleState.Entry target, Operation operation, CancellationToken token)
    {
        var options = NativeGit.WorktreeArguments(repo);
        options.AddRange(["-c", "submodule.recurse=false", "-c", "protocol.ext.allow=never", "submodule"]);
        options.AddRange(operation switch
        {
            Operation.Init => ["init"],
            Operation.Sync => ["sync"],
            _ => ["update", "--init", "--checkout"],
        });
        options.AddRange(["--", target.Module.Path]);
        token.ThrowIfCancellationRequested();
        var native = await NativeGit.RunCaptureAsync(repo.WorktreePath, options, CancellationToken.None).ConfigureAwait(false);
        if (native.ExitCode != 0)
            return new("git_submodule_failed", "Native Git could not complete the selected submodule operation.");
        var current = await FindAsync(repo, target.Module.Path, CancellationToken.None).ConfigureAwait(false);
        if (current is null || !current.Module.IsConfigured ||
            !string.Equals(current.Module.IndexCommit, target.Module.IndexCommit, StringComparison.OrdinalIgnoreCase) ||
            operation == Operation.Update && (!current.Module.HasCheckout ||
                !string.Equals(current.Module.Head, target.Module.IndexCommit, StringComparison.OrdinalIgnoreCase)))
            return Changed();
        return FeatureOutcome.Success(operation switch
        {
            Operation.Init => "Git submodule initialized.",
            Operation.Sync => "Git submodule URL synced.",
            _ => "Git submodule updated to the recorded commit.",
        }, current.Module);
    }

    private static async Task<(GitSubmoduleState.Entry? Value, FeatureOutcome? Failure)> ReviewAsync(GitWorktree repo, TargetInput input,
        CancellationToken token)
    {
        var target = await FindAsync(repo, input.Path, token).ConfigureAwait(false);
        if (target is null || !target.Module.CanChange)
            return (null, new("git_submodule_unavailable", "Select a configured gitlink with an available path and a resolved index entry."));
        if (!string.Equals(target.Module.Revision, input.ExpectedRevision, StringComparison.Ordinal))
            return (null, Changed());
        return (target, null);
    }

    private static async Task<GitSubmoduleState.Entry?> FindAsync(GitWorktree repo, string path, CancellationToken token) =>
        Array.Find(await GitSubmoduleState.ReadAsync(repo, token).ConfigureAwait(false),
            entry => string.Equals(entry.Module.Path, path, StringComparison.Ordinal));

    private static Task<FeatureOutcome> WithSourceAsync(string path, IWorkbenchRegistration view,
        Func<IGitRegistration, GitSelection, Task<FeatureOutcome>> run, CancellationToken token) =>
        GitOperationRecovery.WithSelectionAsync(path, view, async (git, selection) =>
            await SourceAvailableAsync(selection.Repository, token).ConfigureAwait(false)
                ? await run(git, selection).ConfigureAwait(false) : SourceUnavailable(), token);

    private static async Task<bool> SourceAvailableAsync(GitWorktree repo, CancellationToken token) =>
        await NativeGit.DiscoverAsync(repo.WorktreePath, "", token).ConfigureAwait(false) is { } found &&
            NativeGit.IsSameWorktree(found.Repository, repo);

    private static FeatureOutcome Publish(GitSelection selection, Func<FeatureOutcome> outcome) =>
        new("ok", "Git submodule operation prepared.")
        {
            Commit = current => GitOperationRecovery.SameSelection(current, selection) ? outcome() : GitOperationRecovery.ChangedSelection(),
        };

    private static FeatureOutcome SourceUnavailable() => new("git_repository_unavailable", "The selected Git worktree is unavailable.");
    private static FeatureOutcome Changed() => new("git_submodule_changed", "The submodule changed. Refresh its status and review it again.");
}
