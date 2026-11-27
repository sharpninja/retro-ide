using System.Text;

namespace RetroIde.Application.Git;

internal static partial class GitRemoteOperations
{
    internal sealed record PushInput(string WorktreePath, string Name, string Revision, string BranchName, string ExpectedHead,
        string RemoteBranch, bool ForceWithLease = false, string? ExpectedRemoteHead = null);
    internal sealed record PullInput(string WorktreePath, string Name, string Revision, string BranchName, string ExpectedHead,
        string RemoteBranch, string Strategy = "fast-forward");
    internal sealed record SyncResult(GitWorktree Repository, string Name, string BranchName, string RemoteBranch, string Head,
        TrackingStatus? Status = null);

    internal static Task<FeatureOutcome> PushAsync(PushInput input, IWorkbenchRegistration view, CancellationToken token)
    {
        if ((input.ForceWithLease && !GitHistoryOperations.IsObjectId(input.ExpectedRemoteHead)) ||
            (!input.ForceWithLease && input.ExpectedRemoteHead is not null))
            return Task.FromResult(new FeatureOutcome("git_push_lease_invalid", "A force-with-lease push requires an explicit expected remote object ID."));

        var target = new SyncTarget(input.Name, input.Revision, input.BranchName, input.ExpectedHead, input.RemoteBranch);
        return InScopeAsync(input.WorktreePath, view, async selection =>
        {
            if (await CheckTargetAsync(selection.Repository, target, pull: false, token).ConfigureAwait(false) is { } failure)
                return failure;
            return Authorize(selection, cancellation => RunReviewedAsync(selection, cancellation,
                () => PushCoreAsync(selection, target, input, cancellation)));
        }, token);
    }

    private static async Task<FeatureOutcome> PushCoreAsync(GitSelection selection, SyncTarget target, PushInput input, CancellationToken token)
    {
        var repo = selection.Repository;
        if (await CheckTargetAsync(repo, target, pull: false, token).ConfigureAwait(false) is { } failure)
            return failure;
        var destination = "refs/heads/" + target.RemoteBranch;
        var command = new List<string> { "-c", "protocol.ext.allow=never", "push", "--porcelain", "--atomic", "--no-follow-tags", "--no-recurse-submodules" };
        if (input.ForceWithLease)
            command.Add($"--force-with-lease={destination}:{input.ExpectedRemoteHead}");
        command.AddRange(["--", target.Name, $"{target.ExpectedHead}:{destination}"]);
        token.ThrowIfCancellationRequested();
        var pushed = await RunAsync(repo, CancellationToken.None, [.. command]).ConfigureAwait(false);
        if (pushed.ExitCode == 0)
            return await SyncSuccessAsync(selection, target, "Git branch pushed.").ConfigureAwait(false);

        // Porcelain identifies a declined ref with ! and a tab. Avoid displaying raw remote URLs.
        var rejected = Encoding.UTF8.GetString(pushed.Output).Split('\n').Any(line => line.StartsWith("!\t", StringComparison.Ordinal));
        return rejected
            ? new(input.ForceWithLease ? "git_push_lease_rejected" : "git_push_rejected", "The remote rejected the reviewed branch update.")
            : new("git_push_failed", "Git could not push the branch. Check the remote and Git authentication.");
    }

    internal static Task<FeatureOutcome> PullAsync(PullInput input, IWorkbenchRegistration view, CancellationToken token)
    {
        if (input.Strategy is not ("fast-forward" or "merge" or "rebase"))
            return Task.FromResult(new FeatureOutcome("git_pull_strategy_invalid", "Choose fast-forward, merge, or rebase."));

        var target = new SyncTarget(input.Name, input.Revision, input.BranchName, input.ExpectedHead, input.RemoteBranch);
        return InScopeAsync(input.WorktreePath, view, async selection =>
        {
            if (await CheckTargetAsync(selection.Repository, target, pull: true, token).ConfigureAwait(false) is { } failure)
                return failure;
            return Finish(selection, () => new FeatureOutcome("ok", "Git pull authorized.")
            {
                ExecuteAuthorizedAsync = cancellation => TryAsync(() => RunReviewedAsync(selection, cancellation,
                    () => PullCoreAsync(selection, (IGitRegistration)view, target, input.Strategy, cancellation))),
            });
        }, token);
    }

    private static async Task<FeatureOutcome> PullCoreAsync(GitSelection selection, IGitRegistration git, SyncTarget target,
        string strategy, CancellationToken token)
    {
        var repo = selection.Repository;
        if (await CheckTargetAsync(repo, target, pull: true, token).ConfigureAwait(false) is { } failure)
            return failure;
        if (HasIntegrationState(repo))
            return IntegrationInProgress();

        // Capture this fetch in its own ref so another fetch cannot replace its commit before integration.
        var reviewed = $"refs/retro-ide/pull/{Guid.NewGuid():N}";
        try
        {
            var fetched = await RunAsync(repo, token, "-c", "protocol.ext.allow=never", "fetch", "--atomic", "--no-recurse-submodules",
                "--no-write-fetch-head", "--", target.Name, $"refs/heads/{target.RemoteBranch}:{reviewed}").ConfigureAwait(false);
            if (fetched.ExitCode != 0)
                return new("git_fetch_failed", "Git could not fetch the reviewed branch. Check the remote and Git authentication.");
            var incoming = await ReadObjectAsync(repo, reviewed, token).ConfigureAwait(false);
            if (incoming is null)
                return new("git_pull_target_unavailable", "The fetched branch commit could not be resolved.");
            return await IntegrateAsync(selection, git, target, strategy, incoming, token).ConfigureAwait(false);
        }
        finally
        {
            await RunAsync(repo, CancellationToken.None, "update-ref", "-d", reviewed).ConfigureAwait(false);
        }
    }

    private static async Task<FeatureOutcome> IntegrateAsync(GitSelection selection, IGitRegistration git, SyncTarget target,
        string strategy, string incoming, CancellationToken token)
    {
        var repo = selection.Repository;
        if (await CheckBranchAsync(repo, target, active: true, token).ConfigureAwait(false) is { } failure)
            return failure;
        var ancestor = await RunAsync(repo, token, "merge-base", "--is-ancestor", incoming, target.ExpectedHead).ConfigureAwait(false);
        if (ancestor.ExitCode == 0)
            return await SyncSuccessAsync(selection, target, "The local branch already contains the fetched commit.").ConfigureAwait(false);
        if (string.Equals(strategy, "fast-forward", StringComparison.Ordinal) &&
            (await RunAsync(repo, token, "merge-base", "--is-ancestor", target.ExpectedHead, incoming).ConfigureAwait(false)).ExitCode != 0)
            return new("git_pull_diverged", "The branches diverged. Review both tips and choose merge or rebase.");

        var paths = await ReadIntegrationPathsAsync(repo, target.ExpectedHead, incoming, strategy, token).ConfigureAwait(false);
        var lease = await git.BeginIntegrationAsync(selection, paths, token).ConfigureAwait(false);
        if (lease.Guard is not { } guard)
            return new(lease.ErrorCode ?? "git_pull_editors_busy", "The worktree selection changed or affected editor buffers are unsaved or busy.");

        var reload = false;
        try
        {
            if (await CheckBranchAsync(repo, target, active: true, token).ConfigureAwait(false) is { } stale)
                return stale;
            if (HasIntegrationState(repo))
                return IntegrationInProgress();
            var status = await NativeGit.ReadStatusAsync(selection, token).ConfigureAwait(false);
            if (status is null)
                return new("git_pull_status_unavailable", "Git could not inspect the worktree before integration.");
            if (status.Entries.Any(entry => entry.Worktree == "!" && AffectsIgnoredPath(entry.Path, paths)))
                return new("git_pull_local_changes", "Pull would overwrite ignored files in the worktree.");

            string[] command = strategy switch
            {
                "rebase" => ["-c", "submodule.recurse=false", "rebase", "--no-autostash", "--no-update-refs", "--", incoming],
                "merge" => ["-c", "submodule.recurse=false", "merge", "--ff", "--no-autostash", "--no-overwrite-ignore", "--no-edit", "--", incoming],
                _ => ["-c", "submodule.recurse=false", "merge", "--ff-only", "--no-autostash", "--no-overwrite-ignore", "--no-edit", "--", incoming],
            };
            token.ThrowIfCancellationRequested();
            var result = await RunAsync(repo, CancellationToken.None, command).ConfigureAwait(false);
            reload = result.ExitCode == 0 || HasIntegrationState(repo) ||
                !string.Equals(await ReadObjectAsync(repo, "HEAD", CancellationToken.None).ConfigureAwait(false),
                    target.ExpectedHead, StringComparison.OrdinalIgnoreCase);
            if (result.ExitCode == 0)
                return await SyncSuccessAsync(selection, target, "Git changes pulled.").ConfigureAwait(false);
            if (HasIntegrationState(repo))
                return new("git_pull_conflict", "Git paused integration. Review the conflicts before continuing or aborting.");
            if (reload)
                return await SyncSuccessAsync(selection, target, "Git updated HEAD and reported an integration warning. Refresh your status.").ConfigureAwait(false);
            return new("git_pull_failed", "Git could not integrate the branch. Review local changes and Git status.");
        }
        finally { await guard.CompleteAsync(reload).ConfigureAwait(false); }
    }

    private static async Task<string[]> ReadIntegrationPathsAsync(GitWorktree repo, string current, string incoming, string strategy, CancellationToken token)
    {
        var result = await ChangedPathsAsync(repo, current, incoming, token).ConfigureAwait(false);
        if (!string.Equals(strategy, "rebase", StringComparison.Ordinal))
            return result;
        var baseResult = await RunAsync(repo, token, "merge-base", current, incoming).ConfigureAwait(false);
        var mergeBase = Encoding.UTF8.GetString(baseResult.Output).Trim();
        return baseResult.ExitCode == 0 && GitHistoryOperations.IsObjectId(mergeBase)
            ? [.. result.Union(await ChangedPathsAsync(repo, mergeBase, current, token).ConfigureAwait(false), StringComparer.Ordinal)]
            : result;
    }

    private static async Task<string[]> ChangedPathsAsync(GitWorktree repo, string from, string to, CancellationToken token)
    {
        var result = await NativeGit.RunAsync(repo.WorktreePath, [.. NativeGit.WorktreeArguments(repo), "diff", "--no-renames",
            "--no-ext-diff", "--no-textconv", "--name-only", "-z", from, to, "--"], token).ConfigureAwait(false);
        return result?.Split('\0', StringSplitOptions.RemoveEmptyEntries) ??
            throw new InvalidDataException("Git could not read the paths affected by integration.");
    }

    private static bool HasIntegrationState(GitWorktree repo) => GitOperationRecovery.ReadKind(repo) is not null;

    private static FeatureOutcome IntegrationInProgress() =>
        new("git_pull_in_progress", "Finish or abort the current Git operation before pulling.");

    private static bool AffectsIgnoredPath(string ignored, string[] changedPaths)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var path = ignored.TrimEnd('/');
        return changedPaths.Any(changed => string.Equals(changed, path, comparison) ||
            changed.StartsWith(path + "/", comparison) || path.StartsWith(changed + "/", comparison));
    }

    private static async Task<FeatureOutcome?> CheckTargetAsync(GitWorktree repo, SyncTarget input, bool pull, CancellationToken token)
    {
        var config = await ReadConfigAsync(repo, token).ConfigureAwait(false);
        if (!Matches(config, input.Revision))
            return Changed();
        if (!config.Remotes.TryGetValue(input.Name, out var remote))
            return new("git_remote_unavailable", "The selected Git remote is unavailable.");
        if (pull && !remote.FetchMappings.All(IsTrackingRefspec))
            return new("git_fetch_refspec_unsupported", "Fetch must target remote tracking refs or tags.");
        if (string.IsNullOrWhiteSpace(input.RemoteBranch) || input.RemoteBranch.StartsWith('-') || input.RemoteBranch.Contains('\0') ||
            !await SuccessfulAsync(repo, token, "check-ref-format", "refs/heads/" + input.RemoteBranch).ConfigureAwait(false))
            return new("git_branch_name_invalid", "Enter a valid remote branch name.");
        return await CheckBranchAsync(repo, input, active: pull, token).ConfigureAwait(false);
    }

    private static async Task<FeatureOutcome?> CheckBranchAsync(GitWorktree repo, SyncTarget input, bool active, CancellationToken token)
    {
        if (!GitHistoryOperations.IsObjectId(input.ExpectedHead))
            return new("git_branch_object_invalid", "Supply a full expected local commit ID.");
        var refs = await GitHistoryOperations.ReadRefsAsync(repo, token).ConfigureAwait(false);
        var name = "refs/heads/" + input.BranchName;
        var reference = Array.Find(refs.Refs, candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        if (reference is null || !string.Equals(reference.ObjectId, input.ExpectedHead, StringComparison.OrdinalIgnoreCase))
            return new("git_branch_changed", "The local branch changed. Refresh references and retry.");
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (active && !refs.Worktrees.Any(tree => string.Equals(tree.Path, repo.WorktreePath, pathComparison) &&
                string.Equals(tree.Branch, name, StringComparison.Ordinal)))
            return new("git_pull_branch_unavailable", "Pull applies to the branch checked out in the selected worktree.");
        return null;
    }

    private static async Task<string?> ReadObjectAsync(GitWorktree repo, string name, CancellationToken token)
    {
        var result = await RunAsync(repo, token, "rev-parse", "--verify", "--quiet", "--end-of-options", name + "^{commit}").ConfigureAwait(false);
        var oid = Encoding.UTF8.GetString(result.Output).Trim();
        return result.ExitCode == 0 && GitHistoryOperations.IsObjectId(oid) ? oid : null;
    }

    private static async Task<FeatureOutcome> SyncSuccessAsync(GitSelection selection, SyncTarget target, string message)
    {
        var repo = selection.Repository;
        var status = await NativeGit.ReadStatusAsync(selection, CancellationToken.None).ConfigureAwait(false);
        var tracking = status is null ? null : new TrackingStatus(status.Branch, status.Upstream, status.Ahead, status.Behind);
        var head = await ReadObjectAsync(repo, "refs/heads/" + target.BranchName, CancellationToken.None).ConfigureAwait(false) ?? target.ExpectedHead;
        return FeatureOutcome.Success(message, new SyncResult(repo, target.Name, target.BranchName, target.RemoteBranch, head, tracking));
    }

    private sealed record SyncTarget(string Name, string Revision, string BranchName, string ExpectedHead, string RemoteBranch);
}
