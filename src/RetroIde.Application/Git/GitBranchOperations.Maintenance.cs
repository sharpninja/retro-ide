using System.ComponentModel;

namespace RetroIde.Application.Git;

internal static partial class GitBranchOperations
{
    internal sealed record RenameInput(string WorktreePath, string BranchName, string ExpectedHead, string NewName);
    internal sealed record DeleteInput(string WorktreePath, string BranchName, string ExpectedHead);
    internal sealed record MaintenanceResult(GitWorktree Repository, string BranchName, string Head, string? NewName = null);

    internal static Task<FeatureOutcome> RenameAsync(RenameInput input, IWorkbenchRegistration view, CancellationToken token) =>
        PrepareMaintenanceAsync(input.WorktreePath, input.BranchName, input.ExpectedHead, input.NewName, delete: false, view, token);

    internal static Task<FeatureOutcome> DeleteAsync(DeleteInput input, IWorkbenchRegistration view, CancellationToken token) =>
        PrepareMaintenanceAsync(input.WorktreePath, input.BranchName, input.ExpectedHead, null, delete: true, view, token);

    private static async Task<FeatureOutcome> PrepareMaintenanceAsync(string worktreePath, string name, string expected, string? newName,
        bool delete, IWorkbenchRegistration view, CancellationToken token)
    {
        if (view is not IGitRegistration { SelectedGit: { } selection })
            return new("git_selection_required", "Select a Git worktree first.");
        var repo = selection.Repository;
        if (!SamePath(worktreePath, repo.WorktreePath))
            return new("git_worktree_mismatch", "The requested worktree differs from the current selection.");
        if (!SamePath(selection.ScopeRoot, repo.WorktreePath))
            return new("git_branch_scope_required", "Open the worktree root before changing local branches.");
        if (!GitHistoryOperations.IsObjectId(expected))
            return new("git_branch_object_invalid", "Supply a full commit object ID.");

        try
        {
            var found = await NativeGit.DiscoverAsync(selection.ScopeRoot, "", token).ConfigureAwait(false);
            if (found is null || !NativeGit.IsSameWorktree(found.Repository, repo))
                return new("git_repository_unavailable", "The selected worktree is unavailable.");
            if (!await IsLocalNameAsync(repo, name, token).ConfigureAwait(false) ||
                (!delete && !await IsLocalNameAsync(repo, newName, token).ConfigureAwait(false)))
                return InvalidName();

            var plan = new Maintenance(repo, name, expected, newName);
            if (await ValidateMaintenanceAsync(plan, token).ConfigureAwait(false) is { } failure)
                return failure;

            return new FeatureOutcome("ok", "Git branch operation prepared.")
            {
                Commit = current => current is IGitRegistration git && ReferenceEquals(git.SelectedGit, selection)
                    ? new FeatureOutcome("ok", "Git branch operation authorized.")
                    {
                        ExecuteAuthorizedAsync = cancellation => ExecuteMaintenanceAsync(plan, cancellation),
                    }
                    : new("git_selection_changed", "The selected Git worktree changed."),
            };
        }
        catch (Win32Exception) { return new("git_unavailable", "Git could not be started."); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return new("git_branch_unavailable", "Git could not read the selected worktree or its refs.");
        }
    }

    private static async Task<FeatureOutcome?> ValidateMaintenanceAsync(Maintenance plan, CancellationToken token)
    {
        var refs = await GitHistoryOperations.ReadRefsAsync(plan.Repository, token).ConfigureAwait(false);
        var target = FindLocalRef(refs, plan.Name);
        if (target is null)
            return new("git_branch_unavailable", "The local branch is unavailable.");
        if (!string.Equals(target.ObjectId, plan.Expected, StringComparison.OrdinalIgnoreCase))
            return Changed();

        if (refs.Worktrees.Any(tree => string.Equals(tree.Branch, target.Name, StringComparison.Ordinal) &&
            (plan.NewName is null || !SamePath(tree.Path, plan.Repository.WorktreePath))))
            return new("git_branch_in_use", "The branch is checked out in a worktree.");
        if (plan.NewName is { } rename)
            return FindLocalRef(refs, rename) is null ? null : new("git_branch_exists", "A local branch already has that name.");

        var upstream = target.Upstream is not null
            ? Array.Find(refs.Refs, reference => string.Equals(reference.Name, target.Upstream, StringComparison.Ordinal))
            : null;
        var destination = upstream?.ObjectId ?? CurrentWorktree(refs, plan.Repository)?.Head;
        if (destination is null || destination.All(value => value == '0'))
            return Unmerged();

        var args = NativeGit.WorktreeArguments(plan.Repository);
        args.AddRange(["merge-base", "--is-ancestor", target.ObjectId, destination]);
        var merged = await NativeGit.RunCaptureAsync(plan.Repository.WorktreePath, args, token).ConfigureAwait(false);
        return merged.ExitCode switch
        {
            0 => null,
            1 => Unmerged(),
            _ => new("git_branch_unavailable", "Git could not compare the branch with its upstream or current HEAD."),
        };
    }

    private static async Task<FeatureOutcome> ExecuteMaintenanceAsync(Maintenance plan, CancellationToken token)
    {
        try
        {
            using var held = await GitConfigurationGate.EnterAsync(plan.Repository, token).ConfigureAwait(false);
            if (await ValidateMaintenanceAsync(plan, token).ConfigureAwait(false) is { } failure)
                return failure;
            var rename = plan.NewName is not null;
            var args = NativeGit.WorktreeArguments(plan.Repository);
            args.AddRange(rename ? ["branch", "--move", "--", plan.Name, plan.NewName!] : ["branch", "--delete", "--", plan.Name]);
            token.ThrowIfCancellationRequested();
            var result = await NativeGit.RunCaptureAsync(plan.Repository.WorktreePath, args, CancellationToken.None,
                captureError: true).ConfigureAwait(false);
            return result.ExitCode == 0
                ? FeatureOutcome.Success(rename ? "Git branch renamed." : "Git branch deleted.",
                    new MaintenanceResult(plan.Repository, plan.Name, plan.Expected, plan.NewName))
                : NativeFailure(result, rename ? "git_branch_rename_failed" : "git_branch_delete_failed");
        }
        catch (Win32Exception) { return new("git_unavailable", "Git could not be started."); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new("git_branch_unavailable", "Git could not finish the branch operation.");
        }
    }

    private static FeatureOutcome Unmerged() =>
        new("git_branch_unmerged", "The branch has commits that are not merged into its upstream or current HEAD.");

    private sealed record Maintenance(GitWorktree Repository, string Name, string Expected, string? NewName);
}
