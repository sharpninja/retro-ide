using System.Globalization;
using System.Text.Json;

namespace RetroIde.Application.Git;

/// <summary>Starts a native worktree integration from a reviewed HEAD and immutable commit.</summary>
internal static class GitOperationStart
{
    internal sealed record Input(string WorktreePath, string Kind, string ExpectedHead, string TargetCommit,
        string? ExpectedBranch = null, int? Mainline = null);
    internal sealed record Result(string Kind, string PreviousHead, string TargetCommit, GitOperationRecovery.State Operation);

    internal static Task<FeatureOutcome> StartAsync(Input input, IWorkbenchRegistration view, CancellationToken token)
    {
        if (!GitOperationRecovery.IsSupported(input.Kind))
            return Task.FromResult(new FeatureOutcome("git_operation_kind_invalid", "Choose merge, rebase, cherry-pick, or revert."));
        if (!GitHistoryOperations.IsObjectId(input.ExpectedHead) || !GitHistoryOperations.IsObjectId(input.TargetCommit))
            return Task.FromResult(new FeatureOutcome("git_operation_commit_invalid", "Supply full commit IDs for HEAD and the target."));

        return GitOperationRecovery.WithSelectionAsync(input.WorktreePath, view, async (git, selection) =>
        {
            var repo = selection.Repository;
            if (await CheckCheckoutAsync(selection, input, token).ConfigureAwait(false) is { } failure)
                return failure;
            var target = await RunAsync(repo, token, "rev-list", "--parents", "-n", "1", input.TargetCommit, "--").ConfigureAwait(false);
            var graph = target?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (graph is null || graph.Length == 0 || !string.Equals(graph[0], input.TargetCommit, StringComparison.OrdinalIgnoreCase) ||
                !graph.All(GitHistoryOperations.IsObjectId))
                return new("git_operation_target_unavailable", "The reviewed target is not an available commit.");
            var parentCount = graph.Length - 1;
            var appliesCommit = input.Kind is "cherry-pick" or "revert";
            if (input.Mainline is { } parent && (!appliesCommit || parentCount < 2 || parent < 1 || parent > parentCount) ||
                input.Mainline is null && appliesCommit && parentCount > 1)
                return new("git_operation_mainline_invalid", "Select a valid one-based mainline parent when applying a merge commit.");

            var basis = parentCount == 0 ? null : graph[input.Mainline ?? 1];
            return new FeatureOutcome("ok", "Git operation prepared.")
            {
                Commit = current => GitOperationRecovery.SameSelection(current, selection)
                    ? new FeatureOutcome("ok", "Git operation authorized.")
                    {
                        ExecuteAuthorizedAsync = cancellation => GitOperationRecovery.TryAsync(
                            () => ExecuteAsync(git, selection, input, basis, cancellation)),
                    }
                    : GitOperationRecovery.ChangedSelection(),
            };
        }, token);
    }

    private static async Task<FeatureOutcome> ExecuteAsync(IGitRegistration git, GitSelection selection, Input input,
        string? basis, CancellationToken token)
    {
        using var held = await WorkspaceMutationGate.EnterAsync(token).ConfigureAwait(false);
        // A three-way integration can also change paths renamed on the local side.
        var editors = await git.BeginIntegrationAsync(selection, paths: null, token).ConfigureAwait(false);
        if (editors.Guard is not { } guard)
            return new(editors.ErrorCode ?? "git_operation_editors_busy", "Save open editor changes in this worktree before starting a Git operation.");

        var invoked = false;
        try
        {
            if (await CheckCheckoutAsync(selection, input, token).ConfigureAwait(false) is { } problem)
                return problem;
            if (input.Kind is not "merge" && await WouldOverwriteLocalAsync(selection, input, basis, token).ConfigureAwait(false))
                return new("git_operation_local_changes", "Move the untracked or ignored files that this operation would overwrite.");

            var repo = selection.Repository;
            var arguments = NativeGit.WorktreeArguments(repo);
            arguments.AddRange(["-c", "submodule.recurse=false", input.Kind]);
            arguments.AddRange(input.Kind switch
            {
                "merge" => ["--ff", "--no-autostash", "--no-overwrite-ignore", "--no-edit"],
                "rebase" => ["--no-autostash", "--no-update-refs", "--no-fork-point"],
                _ => ["--no-edit"],
            });
            if (input.Mainline is { } mainline)
                arguments.AddRange(["--mainline", mainline.ToString(CultureInfo.InvariantCulture)]);
            arguments.AddRange(["--", input.TargetCommit]);
            token.ThrowIfCancellationRequested();
            invoked = true;
            var native = await NativeGit.RunCaptureAsync(repo.WorktreePath, arguments, CancellationToken.None, reuseCommitMessage: true)
                .ConfigureAwait(false);
            var operation = await GitOperationRecovery.CaptureAsync(selection, CancellationToken.None).ConfigureAwait(false);
            var result = new Result(input.Kind, input.ExpectedHead, input.TargetCommit, operation);
            if (native.ExitCode == 0)
                return FeatureOutcome.Success("Git operation completed.", result);
            return operation.CanAbort
                ? new("git_operation_paused", "Git paused the operation. Resolve the conflicts in Source Control.",
                    JsonSerializer.SerializeToElement(result, WireJson.Options))
                : new("git_operation_failed", "Git could not start the operation. Check worktree status, local files, and native Git configuration.");
        }
        finally { await guard.CompleteAsync(invoked).ConfigureAwait(false); }
    }

    private static async Task<FeatureOutcome?> CheckCheckoutAsync(GitSelection selection, Input input, CancellationToken token)
    {
        var repo = selection.Repository;
        var discovered = await NativeGit.DiscoverAsync(selection.ScopeRoot, "", token).ConfigureAwait(false);
        if (discovered is null || !NativeGit.IsSameWorktree(discovered.Repository, repo))
            return new("git_repository_unavailable", "The selected Git worktree is unavailable.");
        if (GitOperationRecovery.ReadKind(repo) is not null)
            return new("git_operation_in_progress", "Continue or abort the active Git operation before starting another.");
        var refs = await GitHistoryOperations.ReadRefsAsync(repo, token).ConfigureAwait(false);
        var current = GitHistoryOperations.CurrentWorktree(refs);
        return current is not null && string.Equals(current.Branch, input.ExpectedBranch, StringComparison.Ordinal) &&
               string.Equals(current.Head, input.ExpectedHead, StringComparison.OrdinalIgnoreCase)
            ? null : new("git_operation_head_changed", "HEAD or the checked-out branch changed. Review the operation again.");
    }

    private static async Task<bool> WouldOverwriteLocalAsync(GitSelection selection, Input input, string? basis, CancellationToken token)
    {
        var status = await NativeGit.ReadStatusAsync(selection, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("Git status is unavailable.");
        var local = status.Entries.Where(entry => entry.Worktree is "?" or "!").Select(entry => entry.Path.TrimEnd('/')).ToArray();
        if (local.Length == 0)
            return false;

        var repo = selection.Repository;
        var changed = input.Kind switch
        {
            "rebase" => await RunAsync(repo, token, "diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--name-only", "-z",
                input.ExpectedHead, input.TargetCommit, "--")
                .ConfigureAwait(false),
            _ when basis is null => await RunAsync(repo, token, "ls-tree", "-r", "--full-tree", "--name-only", "-z", input.TargetCommit)
                .ConfigureAwait(false),
            _ => await RunAsync(repo, token, "diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--name-only", "-z",
                basis, input.TargetCommit, "--").ConfigureAwait(false),
        } ?? throw new InvalidDataException("Git could not identify the affected paths.");
        if (input.Kind is "rebase")
        {
            var replayed = await RunAsync(repo, token, "log", "--diff-merges=first-parent", "--no-renames", "--name-only", "--format=", "-z",
                input.TargetCommit + ".." + input.ExpectedHead, "--").ConfigureAwait(false)
                ?? throw new InvalidDataException("Git could not identify the replayed paths.");
            changed += replayed;
        }
        return GitOperationRecovery.Collides(changed.Split('\0', StringSplitOptions.RemoveEmptyEntries), local);
    }

    private static Task<string?> RunAsync(GitWorktree repo, CancellationToken token, params string[] command)
    {
        var args = NativeGit.WorktreeArguments(repo);
        args.AddRange(command);
        return NativeGit.RunAsync(repo.WorktreePath, args, token);
    }
}
