using RetroIde.Application.Workspaces;

namespace RetroIde.Application.Git;

/// <summary>Supplies the worktree selection and Explorer roots from an authorized live workbench registration.</summary>
/// <remarks>2026-11-26 · TR-RETRO-GIT-013#ac-git-machine-state; TEST-RETRO-015#ac-test-mcp-same-instance. Share selection between native views and attached clients.</remarks>
internal interface IGitRegistration
{
    /// <summary>Gets the open project and folder roots of the attached workbench.</summary>
    WorkspaceExplorer Explorer { get; }

    /// <summary>Gets the current worktree identity and read scope.</summary>
    GitSelection? SelectedGit { get; }

    /// <summary>Changes the worktree selection and publishes the resulting workbench revision.</summary>
    /// <param name="selection">The validated identity and scope to publish.</param>
    /// <param name="address">The authorized workspace-relative directory for later discovery.</param>
    void SelectGit(GitSelection selection, WorkspaceExplorer.Address address);

    /// <summary>Captures the order of the next status read for this registration.</summary>
    long BeginGitStatusRead();

    /// <summary>Publishes the latest completed read to its desktop pane.</summary>
    /// <param name="status">The validated status, or null if the checkout cannot be read.</param>
    /// <param name="readOrder">The captured read order.</param>
    void DisplayGitStatus(GitStatus? status, long readOrder);

    /// <summary>Opens a scoped, read-only Git diff in the attached document workbench.</summary>
    GitFileOperations.DiffTab? ShowDiff(GitFileOperations.DiffResult diff);

    /// <summary>Closes an existing Git diff tab by its registration key.</summary>
    bool CloseDiff(Guid viewKey);

    /// <summary>Opens or refreshes a scoped, read-only Git attribution tab.</summary>
    GitBlameOperations.Tab? ShowBlame(GitBlameOperations.Blame blame);

    /// <summary>Closes an existing Git attribution tab by its registration key.</summary>
    bool CloseBlame(Guid viewKey);

    /// <summary>Opens or updates the selected worktree's history tab.</summary>
    GitHistoryOperations.HistoryTab? ShowHistory(GitHistoryOperations.HistoryPage page, GitHistoryOperations.RefState references);

    /// <summary>Closes a registered Git history tab.</summary>
    bool CloseHistory(Guid viewKey);

    /// <summary>Protects open source buffers affected by a native branch switch.</summary>
    IGitBranchSwitch? BeginBranchSwitch(GitWorktree repository, string[] paths);

    /// <summary>Revalidates the captured worktree and protects the listed source paths, or all source paths when null.</summary>
    Task<GitIntegrationLease> BeginIntegrationAsync(GitSelection selection, string[]? paths, CancellationToken token);

    /// <summary>Registers the installed checkout as a workspace root and makes it the selected Git worktree.</summary>
    /// <param name="repository">The installed native checkout.</param>
    /// <returns>The registered root and worktree identity.</returns>
    GitProvisionedWorkspace OpenGitWorkspace(GitWorktree repository);
}

internal interface IGitBranchSwitch
{
    Task CompleteAsync(bool switched);
}

internal readonly record struct GitIntegrationLease(IGitBranchSwitch? Guard, string? ErrorCode = null);
