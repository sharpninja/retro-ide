using System.Collections.Concurrent;
using RetroIde.Application.Workspaces;

namespace RetroIde.Application.Git;

internal static class GitWorktreeOperations
{
    internal sealed record ListInput(string WorktreePath);
    internal sealed record AddInput(string WorktreePath, string Destination, string TargetCommit,
        string? NewBranch = null, string? ExistingBranch = null);
    internal sealed record TargetInput(string WorktreePath, string TargetPath, string ExpectedHead, bool ExpectedLocked,
        string? ExpectedBranch = null, string? Reason = null);

    internal enum Maintenance { Lock, Unlock, Remove }
    private sealed record Target(GitHistoryOperations.Worktree Row, GitWorktree Repository);
    private static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(Paths);
    private const string HeadPrefix = "refs/heads/";

    internal static Task<FeatureOutcome> ListAsync(ListInput input, IWorkbenchRegistration view, CancellationToken token) =>
        WithSourceAsync(input.WorktreePath, view, async (_, selection) =>
        {
            var worktrees = await GitHistoryOperations.ReadWorktreesAsync(selection.Repository, token).ConfigureAwait(false);
            return Publish(selection, _ => FeatureOutcome.Success("Git worktrees read.", worktrees));
        }, token);

    internal static Task<FeatureOutcome> AddAsync(AddInput input, IWorkbenchRegistration view, CancellationToken token) =>
        WithSourceAsync(input.WorktreePath, view, (_, selection) =>
            Task.FromResult(Authorize(selection, cancel => ExecuteAddAsync(selection.Repository, input, cancel))), token);

    internal static Task<FeatureOutcome> OpenAsync(TargetInput input, IWorkbenchRegistration view, CancellationToken token) =>
        WithSourceAsync(input.WorktreePath, view, async (_, selection) =>
        {
            var (target, failure) = await ResolveTargetAsync(selection.Repository, input, mode: null, token).ConfigureAwait(false);
            return failure ?? Publish(selection, git => git is IExplorerRegistration { HasExplorer: true, CanChangeExplorer: true }
                ? FeatureOutcome.Success("Git worktree opened.", git.OpenGitWorkspace(target!.Repository))
                : new("explorer_busy", "Project Explorer cannot open the worktree now."));
        }, token);

    internal static Task<FeatureOutcome> MaintainAsync(TargetInput input, Maintenance mode, IWorkbenchRegistration view,
        CancellationToken token) =>
        WithSourceAsync(input.WorktreePath, view, (git, selection) => Task.FromResult(Authorize(selection,
            cancel => ExecuteMaintenanceAsync(git, selection, input, mode, cancel))), token);

    private static async Task<FeatureOutcome> ExecuteAddAsync(GitWorktree repo, AddInput input, CancellationToken token)
    {
        if (NormalizePath(input.Destination) is not { } requested || Path.GetDirectoryName(requested) is not { } parent ||
            !Directory.Exists(parent))
            return new("git_worktree_destination_invalid", "Choose an absolute destination with an existing parent folder.");
        var destination = Path.Combine(CanonicalDirectory(parent), Path.GetFileName(requested));
        if (!AvailableDestination(repo, destination))
            return new("git_worktree_destination_exists", "Choose a new or empty directory outside Git metadata.");
        if (!GitHistoryOperations.IsObjectId(input.TargetCommit) ||
            !string.Equals((await ReadAsync(repo, token, "cat-file", "-t", input.TargetCommit).ConfigureAwait(false))?.Trim(),
                "commit", StringComparison.Ordinal))
            return new("git_worktree_target_invalid", "Choose an available full commit ID.");
        if (input.NewBranch is not null && input.ExistingBranch is not null)
            return new("git_worktree_branch_invalid", "Choose either a new branch or an existing local branch.");

        var branch = input.ExistingBranch ?? input.NewBranch;
        if (branch is not null && !await IsBranchNameAsync(repo, branch, token).ConfigureAwait(false))
            return new("git_worktree_branch_invalid", "Choose a valid local branch name.");
        if (input.ExistingBranch is { } existing)
        {
            var actual = await ReadAsync(repo, token, "show-ref", "--verify", "--hash", "--", HeadPrefix + existing).ConfigureAwait(false);
            if (!string.Equals(actual?.Trim(), input.TargetCommit, StringComparison.OrdinalIgnoreCase))
                return Changed();
        }
        var args = NativeGit.WorktreeArguments(repo);
        args.AddRange(["worktree", "add", "--no-guess-remote"]);
        if (input.NewBranch is { } created)
            args.AddRange(["-b", created]);
        else if (input.ExistingBranch is null)
            args.Add("--detach");
        args.AddRange(["--", destination, input.ExistingBranch ?? input.TargetCommit]);

        token.ThrowIfCancellationRequested();
        var result = await NativeGit.RunCaptureAsync(repo.WorktreePath, args, CancellationToken.None).ConfigureAwait(false);
        if (result.ExitCode != 0)
            return new("git_worktree_add_failed", "Native Git could not add the requested worktree. Check the destination and branch.");
        var row = await FindRowAsync(repo, destination, CancellationToken.None).ConfigureAwait(false);
        return row is not null && string.Equals(row.Head, input.TargetCommit, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row.Branch, branch is null ? null : HeadPrefix + branch, StringComparison.Ordinal)
            ? FeatureOutcome.Success("Git worktree added.", row) : Changed();
    }

    private static async Task<FeatureOutcome> ExecuteMaintenanceAsync(IGitRegistration git, GitSelection source, TargetInput input,
        Maintenance mode, CancellationToken token)
    {
        var repo = source.Repository;
        var (target, failure) = await ResolveTargetAsync(repo, input, mode, token).ConfigureAwait(false);
        if (failure is not null)
            return failure;
        if (mode == Maintenance.Remove)
            return await RemoveAsync(git, source, input, target!, token).ConfigureAwait(false);
        if (mode == Maintenance.Lock && input.Reason?.Contains('\0') == true)
            return new("git_worktree_reason_invalid", "The lock reason contains an invalid character.");
        var args = NativeGit.WorktreeArguments(repo);
        args.AddRange(["worktree", mode == Maintenance.Lock ? "lock" : "unlock"]);
        if (mode == Maintenance.Lock && !string.IsNullOrEmpty(input.Reason))
            args.AddRange(["--reason", input.Reason]);
        args.AddRange(["--", target!.Row.Path]);

        token.ThrowIfCancellationRequested();
        var result = await NativeGit.RunCaptureAsync(repo.WorktreePath, args, CancellationToken.None).ConfigureAwait(false);
        if (result.ExitCode != 0)
            return new("git_worktree_lock_failed", "Native Git could not change the worktree lock.");
        var after = await FindRowAsync(repo, target.Row.Path, CancellationToken.None).ConfigureAwait(false);
        return after is not null && after.IsLocked == (mode == Maintenance.Lock)
            ? FeatureOutcome.Success("Git worktree lock updated.", after) : Changed();
    }

    private static async Task<FeatureOutcome> RemoveAsync(IGitRegistration git, GitSelection source, TargetInput input,
        Target reviewed, CancellationToken token)
    {
        var lease = await git.BeginIntegrationAsync(source, paths: null, token, reviewed.Repository.WorktreePath).ConfigureAwait(false);
        if (lease.ErrorCode is not null)
            return GitOperationRecovery.ChangedSelection();
        if (lease.Guard is not { } guard)
            return new("git_worktree_editors_busy", "Save or close changed editors in the worktree before removing it.");

        var removed = false;
        try
        {
            var (target, failure) = await ResolveTargetAsync(source.Repository, input, Maintenance.Remove, token).ConfigureAwait(false);
            if (failure is not null)
                return failure;
            if (!NativeGit.IsSameWorktree(reviewed.Repository, target!.Repository))
                return Changed();
            var repo = target.Repository;
            var status = await NativeGit.ReadStatusAsync(new GitSelection(repo, repo.WorktreePath), token).ConfigureAwait(false);
            if (status is null)
                return Changed();
            if (status.Entries.Length > 0 || status.OperationKind is not null)
                return new("git_worktree_dirty", "Save or move local changes and ignored files, and finish active Git operations before removal.");

            var origin = source.Repository;
            var args = NativeGit.WorktreeArguments(origin);
            args.AddRange(["worktree", "remove", "--", target.Row.Path]);
            token.ThrowIfCancellationRequested();
            var result = await NativeGit.RunCaptureAsync(origin.WorktreePath, args, CancellationToken.None).ConfigureAwait(false);
            removed = result.ExitCode == 0;
            if (!removed)
                return new("git_worktree_remove_failed", "Native Git refused to remove the worktree.");
            await git.ForgetRemovedWorktreeAsync(repo).ConfigureAwait(false);
            return FeatureOutcome.Success("Git worktree removed.", target.Row);
        }
        finally
        {
            await guard.CompleteAsync(removed).ConfigureAwait(false);
        }
    }

    private static async Task<(Target? Value, FeatureOutcome? Failure)> ResolveTargetAsync(GitWorktree repo, TargetInput input,
        Maintenance? mode, CancellationToken token)
    {
        if (NormalizePath(input.TargetPath) is not { } path || !GitHistoryOperations.IsObjectId(input.ExpectedHead))
            return (null, new("git_worktree_target_invalid", "Choose a listed worktree and its full HEAD object ID."));
        var row = await FindRowAsync(repo, path, token).ConfigureAwait(false);
        if (row is null || row.IsBare || row.IsPrunable)
            return (null, new("git_worktree_target_unavailable", "The listed Git worktree is unavailable."));
        if (!string.Equals(row.Head, input.ExpectedHead, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(row.Branch, input.ExpectedBranch, StringComparison.Ordinal) || row.IsLocked != input.ExpectedLocked)
            return (null, Changed());
        if (mode.HasValue && row.IsPrimary || mode == Maintenance.Remove && NativeGit.ContainsPath(row.Path, repo.WorktreePath))
            return (null, new("git_worktree_target_protected", "Select an eligible linked worktree."));
        if (mode == Maintenance.Remove && row.IsLocked)
            return (null, new("git_worktree_locked", "Unlock the Git worktree before removing it."));
        if (mode == Maintenance.Lock && row.IsLocked || mode == Maintenance.Unlock && !row.IsLocked)
            return (null, new("git_worktree_lock_unchanged", "The Git worktree already has the requested lock state."));
        var found = await NativeGit.DiscoverAsync(row.Path, "", token).ConfigureAwait(false);
        if (found is null || !Paths.Equals(row.Path, found.Repository.WorktreePath) ||
            !Paths.Equals(repo.CommonDirectory, found.Repository.CommonDirectory))
            return (null, Changed());
        return (new Target(row, found.Repository), null);
    }

    private static Task<FeatureOutcome> WithSourceAsync(string path, IWorkbenchRegistration view,
        Func<IGitRegistration, GitSelection, Task<FeatureOutcome>> run, CancellationToken token) =>
        GitOperationRecovery.WithSelectionAsync(path, view, async (git, selection) =>
            await SourceAvailableAsync(selection.Repository, token).ConfigureAwait(false)
                ? await run(git, selection).ConfigureAwait(false) : SourceUnavailable(), token);

    private static FeatureOutcome Publish(GitSelection selection, Func<IGitRegistration, FeatureOutcome> outcome) =>
        new("ok", "Git worktree operation prepared.")
        {
            Commit = current => GitOperationRecovery.SameSelection(current, selection)
                ? outcome((IGitRegistration)current) : GitOperationRecovery.ChangedSelection(),
        };

    private static FeatureOutcome Authorize(GitSelection selection, Func<CancellationToken, Task<FeatureOutcome>> run) =>
        Publish(selection, _ => new FeatureOutcome("ok", "Git worktree operation authorized.")
        {
            ExecuteAuthorizedAsync = token => GitOperationRecovery.TryAsync(async () =>
            {
                var repo = selection.Repository;
                var gate = Gates.GetOrAdd(repo.CommonDirectory, static _ => new SemaphoreSlim(1));
                await gate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    return await SourceAvailableAsync(repo, token).ConfigureAwait(false)
                        ? await run(token).ConfigureAwait(false) : SourceUnavailable();
                }
                finally { gate.Release(); }
            }),
        });

    private static async Task<bool> SourceAvailableAsync(GitWorktree repo, CancellationToken token) =>
        await NativeGit.DiscoverAsync(repo.WorktreePath, "", token).ConfigureAwait(false) is { } found &&
            NativeGit.IsSameWorktree(found.Repository, repo);

    private static async Task<GitHistoryOperations.Worktree?> FindRowAsync(GitWorktree repo, string path, CancellationToken token) =>
        Array.Find(await GitHistoryOperations.ReadWorktreesAsync(repo, token).ConfigureAwait(false), row => Paths.Equals(row.Path, path));

    private static async Task<bool> IsBranchNameAsync(GitWorktree repo, string name, CancellationToken token) =>
        !string.IsNullOrWhiteSpace(name) && !name.StartsWith('-') && !name.Contains('\0') &&
            await ReadAsync(repo, token, "check-ref-format", HeadPrefix + name).ConfigureAwait(false) is not null;

    private static string? NormalizePath(string path) => Path.IsPathFullyQualified(path)
        ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) : null;

    private static string CanonicalDirectory(string path)
    {
        var directory = new DirectoryInfo(path);
        if (directory.Parent is not { } parent)
            return directory.FullName;
        var entry = new DirectoryInfo(Path.Combine(CanonicalDirectory(parent.FullName), directory.Name));
        return entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? entry.FullName;
    }

    private static bool AvailableDestination(GitWorktree repo, string path)
    {
        if (NativeGit.ContainsPath(repo.CommonDirectory, path) || NativeGit.ContainsPath(repo.GitDirectory, path))
            return false;
        try
        {
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.Directory) && !attributes.HasFlag(FileAttributes.ReparsePoint) &&
                !Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static Task<string?> ReadAsync(GitWorktree repo, CancellationToken token, params string[] args) =>
        NativeGit.RunAsync(repo.WorktreePath, [.. NativeGit.WorktreeArguments(repo), .. args], token);

    private static FeatureOutcome Changed() =>
        new("git_worktree_changed", "The Git worktree changed. Refresh and review its current state.");

    private static FeatureOutcome SourceUnavailable() => new("git_repository_unavailable", "The selected Git worktree is unavailable.");
}
