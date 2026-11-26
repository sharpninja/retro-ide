using System.ComponentModel;
using System.Text;
using RetroIde.Application.Workspaces;

namespace RetroIde.Application.Git;

/// <summary>Registers scoped Git discovery, selection and status in the shared typed feature registry.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos; TEST-RETRO-015#ac-test-mcp-same-instance. Prepare all native reads outside the workbench dispatcher and apply selection in CQRS.</remarks>
internal sealed class GitFeatures
{
    /// <summary>Addresses a repository candidate by an open root and a literal relative path.</summary>
    internal sealed record LocateInput(Guid RootId, string RelativePath);

    /// <summary>Requests status for the workbench's selected worktree.</summary>
    internal sealed record StatusInput
    {
        internal bool DisplayOnly { get; init; }
    }

    /// <summary>Identifies the destination selected for Git Init.</summary>
    internal sealed record InitializeInput(string Destination);

    /// <summary>Identifies a native clone source and an explicit destination.</summary>
    internal sealed record CloneInput(string Source, string Destination);

    /// <summary>Resolves a workspace address to its native Git worktree without changing the selection.</summary>
    public Feature<LocateInput> Discover { get; } = new("git.repository.discover", "git_discover_repository",
        "Find the native Git worktree for a literal path inside an open workspace root.",
        static (input, registration, token) => LocateAsync(input, registration, select: false, token), typeof(GitWorktree));

    /// <summary>Selects the worktree identified by a captured workspace address.</summary>
    public Feature<LocateInput> Select { get; } = new("git.repository.select", "git_select_repository",
        "Select the native Git worktree for a path in the current workspace.",
        static (input, registration, token) => LocateAsync(input, registration, select: true, token), typeof(GitWorktree), mutation: true);

    /// <summary>Returns current machine-readable status for the selected worktree.</summary>
    public Feature<StatusInput> Status { get; }

    public Feature<GitFileOperations.DiffInput> Diff { get; } = new("git.diff.read", "git_get_diff",
        "Read a literal file's staged or working diff in the selected worktree.",
        static (input, view, token) => GitFileOperations.DiffAsync(input, view, openTab: false, token),
        typeof(GitFileOperations.DiffResult));

    public Feature<GitFileOperations.DiffInput> OpenDiff { get; } = new("git.diff.open", "git_open_diff",
        "Open or refresh a file's staged or working diff in the attached workbench.",
        static (input, view, token) => GitFileOperations.DiffAsync(input, view, openTab: true, token),
        typeof(GitFileOperations.DiffTab), mutation: true);

    public Feature<GitFileOperations.CloseDiffInput> CloseDiff { get; } = new("git.diff.close", "git_close_diff",
        "Close a Git diff tab in the attached workbench.", mutation: true,
        static (input, view) => view is IGitRegistration git && git.CloseDiff(input.ViewKey)
            ? new("ok", "Git diff tab closed.")
            : new("view_closed", "The Git diff tab is not open in this workbench."));

    public Feature<GitFileOperations.FileInput> Stage { get; } = new("git.file.stage", "git_stage_file",
        "Stage a whole file in the selected worktree's index.",
        static (input, view, token) => GitFileOperations.UpdateIndexAsync(input, view, stage: true, token),
        typeof(GitFileOperations.FileResult), mutation: true);

    public Feature<GitFileOperations.FileInput> Unstage { get; } = new("git.file.unstage", "git_unstage_file",
        "Unstage a whole file while preserving its working-tree changes.",
        static (input, view, token) => GitFileOperations.UpdateIndexAsync(input, view, stage: false, token),
        typeof(GitFileOperations.FileResult), mutation: true);

    public Feature<GitFileOperations.HunksInput> StageHunks { get; } = new("git.hunks.stage", "git_stage_hunks",
        "Stage selected hunks from a current working diff. Use the preimage and hunk IDs returned by the diff.",
        static (input, view, token) => GitFileOperations.UpdateHunksAsync(input, view, stage: true, token),
        typeof(GitFileOperations.HunksResult), mutation: true);

    public Feature<GitFileOperations.HunksInput> UnstageHunks { get; } = new("git.hunks.unstage", "git_unstage_hunks",
        "Unstage selected hunks from a current staged diff without changing working files.",
        static (input, view, token) => GitFileOperations.UpdateHunksAsync(input, view, stage: false, token),
        typeof(GitFileOperations.HunksResult), mutation: true);

    public Feature<GitCommitOperations.StateInput> CommitState { get; } = new("git.commit.state", "git_get_commit_state",
        "Read the selected worktree's current HEAD, staged state and commit preimage.",
        GitCommitOperations.ReadAsync, typeof(GitCommitOperations.CommitState));

    public Feature<GitCommitOperations.CommitInput> Commit { get; } = new("git.commit.create", "git_commit",
        "Commit the selected worktree's reviewed index with an explicit message.",
        static (input, view, token) => GitCommitOperations.CommitAsync(input, view, amend: false, token),
        typeof(GitCommitOperations.CommitResult), mutation: true);

    public Feature<GitCommitOperations.CommitInput> Amend { get; } = new("git.commit.amend", "git_amend",
        "Amend the selected worktree's reviewed HEAD and index with an explicit message.",
        static (input, view, token) => GitCommitOperations.CommitAsync(input, view, amend: true, token),
        typeof(GitCommitOperations.CommitResult), mutation: true);

    public Feature<GitHistoryOperations.RefsInput> Refs { get; } = new("git.refs.read", "git_list_refs",
        "List branches, remote refs, tags, and checkouts for the selected Git worktree.",
        GitHistoryOperations.RefsAsync, typeof(GitHistoryOperations.RefState));

    public Feature<GitHistoryOperations.HistoryInput> History { get; } = new("git.history.read", "git_get_history",
        "Read a page of commit history pinned to HEAD, a full ref name, or the previous page's anchor.",
        static (input, view, token) => GitHistoryOperations.HistoryAsync(input, view, openTab: false, token),
        typeof(GitHistoryOperations.HistoryPage));

    public Feature<GitHistoryOperations.DetailsInput> CommitDetails { get; } = new("git.history.commit-details", "git_get_commit_details",
        "Read a commit, its parents, and the changed files relative to its first parent.",
        GitHistoryOperations.DetailsAsync, typeof(GitHistoryOperations.CommitDetails));

    public Feature<GitHistoryOperations.HistoryInput> OpenHistory { get; } = new("git.history.open", "git_open_history",
        "Open or update the selected worktree's Git history tab.",
        static (input, view, token) => GitHistoryOperations.HistoryAsync(input, view, openTab: true, token),
        typeof(GitHistoryOperations.HistoryTab), mutation: true);

    public Feature<GitHistoryOperations.CloseHistoryInput> CloseHistory { get; } = new("git.history.close", "git_close_history",
        "Close a registered Git history tab in the attached workbench.", mutation: true,
        static (input, view) => view is IGitRegistration git && git.CloseHistory(input.ViewKey)
            ? new("ok", "Git history tab closed.") : new("view_closed", "The history tab is not open in this workbench."));

    public Feature<GitBranchOperations.CreateInput> CreateBranch { get; } = new("git.branch.create", "git_create_branch",
        "Create a local branch from a reviewed commit and optionally switch to it.",
        GitBranchOperations.CreateAsync, typeof(GitBranchOperations.BranchResult), mutation: true);

    public Feature<GitBranchOperations.SwitchInput> SwitchBranch { get; } = new("git.branch.switch", "git_switch_branch",
        "Switch to an existing local branch with its expected head.",
        GitBranchOperations.SwitchAsync, typeof(GitBranchOperations.BranchResult), mutation: true);

    internal GitFeatures(Func<GitSelection, CancellationToken, Task<GitStatus?>>? readStatus = null)
    {
        Status = new("git.status.read", "git_get_status", "Read the selected worktree's staged and working status.",
            (input, view, token) => StatusAsync(input, view, readStatus ?? NativeGit.ReadStatusAsync, token), typeof(GitStatus));
    }

    public Feature<InitializeInput> Initialize { get; } = new("git.repository.initialize", "git_initialize_repository",
        "Initialize an absolute directory and select it as a worktree in the current workspace.",
        static (input, view, token) => ProvisionAsync(input.Destination, source: null, view, token), typeof(GitProvisionedWorkspace), mutation: true);

    public Feature<CloneInput> Clone { get; } = new("git.repository.clone", "git_clone_repository",
        "Clone a Git source into an absolute destination and select the resulting workspace root.",
        static (input, view, token) => string.IsNullOrWhiteSpace(input.Source)
            ? Task.FromResult(new FeatureOutcome("git_invalid_source", "Choose a Git remote URL or an existing repository path."))
            : ProvisionAsync(input.Destination, input.Source, view, token),
        typeof(GitProvisionedWorkspace), mutation: true);

    private static async Task<FeatureOutcome> ProvisionAsync(string destination, string? source, IWorkbenchRegistration view, CancellationToken token)
    {
        if (view is not IGitRegistration origin || view is not IExplorerRegistration { HasExplorer: true, CanChangeExplorer: true })
            return new("explorer_unavailable", "The workspace cannot accept a new Git root.");
        var selectedBeforeSetup = origin.SelectedGit;

        PreparedGitRepository prepared;
        try
        {
            prepared = await Task.Run(() => PreparedGitRepository.PrepareAsync(destination, source, token), token).ConfigureAwait(false);
        }
        catch (PreparedGitRepository.Failure error) { return new(error.Code, error.Message); }
        catch (Win32Exception) { return new("git_unavailable", "Git could not be started."); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return new("git_invalid_destination", "Choose a valid absolute destination folder.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new("git_setup_failed", "Git could not prepare the destination.");
        }

        return new FeatureOutcome("ok", "Git workspace prepared.")
        {
            PreparedResource = prepared,
            KeepCapturedTarget = true,
            Commit = current =>
            {
                if (current is not IGitRegistration git || current is not IExplorerRegistration { HasExplorer: true, CanChangeExplorer: true })
                    return new("explorer_unavailable", "The workspace cannot accept a new Git root.");
                if (!ReferenceEquals(git.SelectedGit, selectedBeforeSetup))
                    return new("git_selection_changed", "The selected Git worktree changed during setup. Run the operation again with the current selection.");
                try
                {
                    prepared.Install();
                    return FeatureOutcome.Success("Git workspace opened.", git.OpenGitWorkspace(prepared.Repository));
                }
                catch (PreparedGitRepository.Failure error) { return new(error.Code, error.Message); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    return new("git_install_failed", "Git could not install the checkout at the destination.");
                }
            },
        };
    }

    /// <summary>Looks up a native repository and optionally applies it to the still-current workbench.</summary>
    private static async Task<FeatureOutcome> LocateAsync(LocateInput input, IWorkbenchRegistration view, bool select, CancellationToken token)
    {
        if (view is not IGitRegistration git)
            return new("git_unavailable", "Git is unavailable in this workbench.");
        WorkspaceExplorer.SourceAddress scope;
        try { scope = git.Explorer.ResolvePath(new(input.RootId, input.RelativePath)); }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
        {
            return new("git_scope_invalid", "Choose a literal path inside an open workspace root.");
        }

        GitSelection? located;
        try { located = await NativeGit.DiscoverAsync(scope.RootPath, scope.Entry.RelativePath, token).ConfigureAwait(false); }
        catch (Win32Exception) { return new("git_unavailable", "Git could not be started."); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException or InvalidOperationException)
        {
            return Unavailable();
        }
        if (located is null)
            return Unavailable();
        if (!select)
            return FeatureOutcome.Success("Git worktree discovered.", located.Repository);

        var address = new WorkspaceExplorer.Address(scope.Entry.RootId, Path.GetRelativePath(scope.RootPath, located.ScopeRoot));
        return new FeatureOutcome("ok", "Git worktree selected.")
        {
            Commit = current =>
            {
                ((IGitRegistration)current).SelectGit(located, address);
                return FeatureOutcome.Success("Git worktree selected.", located.Repository);
            },
        };
    }

    /// <summary>Rechecks a saved worktree against the currently permitted workspace directory.</summary>
    internal static async Task<GitSelection?> RestoreSelectionAsync(WorkspaceExplorer explorer, GitWorkspaceLocation location, CancellationToken token)
    {
        try
        {
            var scope = explorer.ResolvePath(location.Address);
            var found = await NativeGit.DiscoverAsync(scope.RootPath, scope.Entry.RelativePath, token).ConfigureAwait(false);
            return found is not null && NativeGit.IsSameWorktree(found.Repository, location.Repository) ? found : null;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or
            InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Reads the captured status and rejects it if the worktree selection changed before completion.</summary>
    private static async Task<FeatureOutcome> StatusAsync(StatusInput input, IWorkbenchRegistration view,
        Func<GitSelection, CancellationToken, Task<GitStatus?>> readStatus, CancellationToken token)
    {
        if (view is not IGitRegistration { SelectedGit: { } selection } origin)
            return new("git_selection_required", "Select a Git worktree first.");
        var readOrder = origin.BeginGitStatusRead();
        GitStatus? status;
        try { status = await readStatus(selection, token).ConfigureAwait(false); }
        catch (Win32Exception) { return Publish(null, new("git_unavailable", "Git could not be started.")); }
        catch (Exception error) when (error is InvalidDataException or DecoderFallbackException)
        {
            return Publish(null, new("git_output_invalid", "Git did not return readable status records."));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Publish(null, Unavailable());
        }
        return Publish(status, status is null ? Unavailable() : null);

        FeatureOutcome Publish(GitStatus? observed, FeatureOutcome? failure) => new FeatureOutcome("ok", "Git status read.")
        {
            Commit = current =>
            {
                if (current is not IGitRegistration git || !ReferenceEquals(git.SelectedGit, selection))
                    return new("git_selection_changed", "The selected Git worktree changed while status was read.");

                git.DisplayGitStatus(observed, readOrder);
                return failure ?? (input.DisplayOnly ? new("ok", "Git status displayed.") : FeatureOutcome.Success("Git status read.", observed));
            },
        };
    }

    /// <summary>Returns the common result for a missing or unreadable worktree.</summary>
    private static FeatureOutcome Unavailable() => new("git_repository_unavailable", "The path has no available Git worktree.");
}
