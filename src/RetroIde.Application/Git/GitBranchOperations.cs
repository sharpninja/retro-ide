using System.ComponentModel;
using System.Text;

namespace RetroIde.Application.Git;

/// <summary>Creates and switches local branches using the selected native checkout.</summary>
internal static partial class GitBranchOperations
{
    private const string LocalRefs = "refs/heads/";
    internal sealed record CreateInput(string WorktreePath, string BranchName, string? StartCommit = null, bool Switch = false);
    internal sealed record SwitchInput(string WorktreePath, string BranchName, string ExpectedHead);
    internal sealed record BranchResult(GitWorktree Repository, string BranchName, string Head, bool Switched);

    internal static Task<FeatureOutcome> CreateAsync(CreateInput input, IWorkbenchRegistration view, CancellationToken token) =>
        PrepareAsync(input.WorktreePath, input.BranchName, input.StartCommit, create: true, input.Switch, view, token);

    internal static Task<FeatureOutcome> SwitchAsync(SwitchInput input, IWorkbenchRegistration view, CancellationToken token) =>
        PrepareAsync(input.WorktreePath, input.BranchName, input.ExpectedHead, create: false, switchBranch: true, view, token);

    private static async Task<FeatureOutcome> PrepareAsync(string path, string name, string? expected, bool create, bool switchBranch,
        IWorkbenchRegistration view, CancellationToken token)
    {
        if (view is not IGitRegistration { SelectedGit: { } selection })
            return new("git_selection_required", "Select a Git worktree first.");
        var repo = selection.Repository;
        if (!SamePath(path, repo.WorktreePath))
            return new("git_worktree_mismatch", "The requested worktree differs from the current selection.");
        if (!SamePath(selection.ScopeRoot, repo.WorktreePath))
            return new("git_branch_scope_required", "Open the worktree root before changing local branches.");
        if (!IsPotentialLocalName(name))
            return InvalidName();
        if ((!create && expected is null) || (expected is not null && !GitHistoryOperations.IsObjectId(expected)))
            return new("git_branch_object_invalid", "Supply a full commit object ID.");

        try
        {
            var found = await NativeGit.DiscoverAsync(selection.ScopeRoot, "", token).ConfigureAwait(false);
            if (found is null || !NativeGit.IsSameWorktree(found.Repository, repo))
                return new("git_repository_unavailable", "The selected worktree is unavailable.");
            if (!await IsLocalNameAsync(repo, name, token).ConfigureAwait(false))
                return InvalidName();

            var refs = await GitHistoryOperations.ReadRefsAsync(repo, token).ConfigureAwait(false);
            var current = CurrentWorktree(refs, repo);
            if (ValidateRef(refs, repo, name, expected, create) is { } failure)
                return failure;
            var target = await RunAsync(repo, token, "rev-parse", "--quiet", "--verify", "--end-of-options",
                (expected ?? "HEAD") + "^{commit}").ConfigureAwait(false);
            if (target is null)
                return new("git_branch_start_unavailable", "The starting commit is unavailable in this worktree.");
            var commit = target.Trim();
            var paths = switchBranch ? await GetChangesAsync(repo, current?.Head, commit, token).ConfigureAwait(false) : [];
            if (paths is null)
                return new("git_branch_diff_unavailable", "Git could not identify the files affected by switching branches.");

            var plan = new Plan(repo, name, commit, current?.Branch, current?.Head, create, switchBranch, paths);
            return new FeatureOutcome("ok", "Git branch operation prepared.")
            {
                Commit = live => Authorize(live, selection, plan),
            };
        }
        catch (Win32Exception) { return new("git_unavailable", "Git could not be started."); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return new("git_branch_unavailable", "Git could not read the selected worktree or its refs.");
        }
    }

    private static FeatureOutcome Authorize(IWorkbenchRegistration view, GitSelection selection, Plan plan)
    {
        if (view is not IGitRegistration git || !ReferenceEquals(git.SelectedGit, selection))
            return new("git_selection_changed", "The selected Git worktree changed.");
        var guard = plan.SwitchBranch ? git.BeginBranchSwitch(plan.Repository, plan.Paths) : null;
        if (plan.SwitchBranch && guard is null)
            return new("git_branch_editors_busy", "Save affected unsaved editor tabs and wait for active saves before switching.");

        return new FeatureOutcome("ok", "Git branch operation authorized.")
        {
            ExecuteAuthorizedAsync = token => ExecuteAsync(plan, guard, token),
        };
    }

    private static async Task<FeatureOutcome> ExecuteAsync(Plan plan, IGitBranchSwitch? guard, CancellationToken token)
    {
        var changedCheckout = false;
        try
        {
            using var held = await GitConfigurationGate.EnterAsync(plan.Repository, token).ConfigureAwait(false);
            var refs = await GitHistoryOperations.ReadRefsAsync(plan.Repository, token).ConfigureAwait(false);
            var current = CurrentWorktree(refs, plan.Repository);
            if (!string.Equals(current?.Branch, plan.PreviousBranch, StringComparison.Ordinal) ||
                !string.Equals(current?.Head, plan.PreviousHead, StringComparison.Ordinal))
                return Changed();
            if (ValidateRef(refs, plan.Repository, plan.Name, plan.Target, plan.Create) is { } failure)
                return failure;

            var args = NativeGit.WorktreeArguments(plan.Repository);
            if (plan.SwitchBranch)
            {
                args.AddRange(["switch", "--no-guess", "--no-overwrite-ignore", "--no-recurse-submodules"]);
                if (plan.Create)
                    args.AddRange(["--no-track", "-c", plan.Name]);
                args.AddRange(["--", plan.Create ? plan.Target : plan.Name]);
            }
            else
            {
                args.AddRange(["branch", "--no-track", "--", plan.Name, plan.Target]);
            }

            // Git may already have moved HEAD by the time it runs a post-checkout hook.
            token.ThrowIfCancellationRequested();
            var native = await NativeGit.RunCaptureAsync(plan.Repository.WorktreePath, args, CancellationToken.None,
                captureError: true).ConfigureAwait(false);
            var head = plan.Target;
            if (plan.SwitchBranch && native.ExitCode != 0)
            {
                var after = await GitHistoryOperations.ReadRefsAsync(plan.Repository, CancellationToken.None).ConfigureAwait(false);
                var checkout = CurrentWorktree(after, plan.Repository);
                changedCheckout = string.Equals(checkout?.Branch, LocalRefs + plan.Name, StringComparison.Ordinal) &&
                    !string.Equals(checkout?.Branch, plan.PreviousBranch, StringComparison.Ordinal);
                head = checkout?.Head ?? plan.Target;
            }
            if (native.ExitCode != 0 && !changedCheckout)
                return NativeFailure(native, plan.SwitchBranch ? "git_branch_switch_failed" : "git_branch_create_failed");
            changedCheckout = plan.SwitchBranch;
            var summary = native.ExitCode != 0 ? "Git switched branches, but the post-checkout hook reported an error." :
                plan.SwitchBranch ? "Git branch switched." : "Git branch created.";
            return FeatureOutcome.Success(summary, new BranchResult(plan.Repository, plan.Name, head, plan.SwitchBranch));
        }
        catch (Win32Exception) { return new("git_unavailable", "Git could not be started."); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new("git_branch_unavailable", "Git could not finish the branch operation.");
        }
        finally
        {
            if (guard is not null)
                await guard.CompleteAsync(changedCheckout).ConfigureAwait(false);
        }
    }

    private static FeatureOutcome? ValidateRef(GitHistoryOperations.RefState refs, GitWorktree repo, string name, string? expected, bool create)
    {
        var fullName = LocalRefs + name;
        var local = FindLocalRef(refs, name);
        if (create)
            return local is null ? null : new("git_branch_exists", "A local branch already has that name.");
        if (local is null)
            return new("git_branch_unavailable", "The local branch is unavailable.");
        if (!string.Equals(local.ObjectId, expected, StringComparison.OrdinalIgnoreCase))
            return Changed();
        if (refs.Worktrees.Any(tree => string.Equals(tree.Branch, fullName, StringComparison.Ordinal) && !SamePath(tree.Path, repo.WorktreePath)))
            return new("git_branch_in_use", "The branch is checked out in another worktree.");
        return null;
    }

    private static GitHistoryOperations.Worktree? CurrentWorktree(GitHistoryOperations.RefState refs, GitWorktree repo) =>
        Array.Find(refs.Worktrees, tree => SamePath(tree.Path, repo.WorktreePath));

    private static async Task<string[]?> GetChangesAsync(GitWorktree repo, string? head, string target, CancellationToken token)
    {
        if (string.Equals(head, target, StringComparison.OrdinalIgnoreCase))
            return [];
        var output = !GitHistoryOperations.IsObjectId(head) || head!.All(value => value == '0')
            ? await RunAsync(repo, token, "ls-tree", "--full-tree", "-r", "--name-only", "-z", target, "--").ConfigureAwait(false)
            : await RunAsync(repo, token, "diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--name-only", "-z", head, target, "--").ConfigureAwait(false);
        return output?.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static FeatureOutcome NativeFailure(NativeGit.CommandResult result, string code)
    {
        var message = string.IsNullOrWhiteSpace(result.Error) ? Encoding.UTF8.GetString(result.Output) : result.Error;
        var diagnostic = string.Concat(message.Take(600).Where(character => !char.IsControl(character) || character is '\n' or '\t')).Trim();
        return new(code, diagnostic.Length > 0 ? diagnostic : "Git declined the branch operation.");
    }

    private static GitHistoryOperations.Reference? FindLocalRef(GitHistoryOperations.RefState refs, string name) =>
        Array.Find(refs.Refs, reference => string.Equals(reference.Name, LocalRefs + name, StringComparison.Ordinal));

    private static bool IsPotentialLocalName(string? name) => !string.IsNullOrWhiteSpace(name) &&
        !name.StartsWith('-') && !name.StartsWith(LocalRefs, StringComparison.Ordinal) && !name.Contains('\0');

    private static async Task<bool> IsLocalNameAsync(GitWorktree repo, string? name, CancellationToken token) =>
        IsPotentialLocalName(name) &&
        await RunAsync(repo, token, "check-ref-format", LocalRefs + name).ConfigureAwait(false) is not null &&
        await RunAsync(repo, token, "check-ref-format", "--branch", name!).ConfigureAwait(false) is not null;

    private static bool SamePath(string left, string right) => string.Equals(left, right,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static FeatureOutcome InvalidName() => new("git_branch_name_invalid", "Enter a valid local branch name.");
    private static FeatureOutcome Changed() => new("git_branch_changed", "HEAD or the target branch changed. Refresh references and retry.");

    private static Task<string?> RunAsync(GitWorktree repo, CancellationToken token, params string[] command)
    {
        var args = NativeGit.WorktreeArguments(repo);
        args.AddRange(command);
        return NativeGit.RunAsync(repo.WorktreePath, args, token);
    }

    private sealed record Plan(GitWorktree Repository, string Name, string Target, string? PreviousBranch, string? PreviousHead,
        bool Create, bool SwitchBranch, string[] Paths);
}
