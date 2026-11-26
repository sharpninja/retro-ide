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
    void SelectGit(GitSelection selection);

    /// <summary>Publishes the status of the current selection to its desktop pane.</summary>
    /// <param name="status">The validated worktree status to display.</param>
    void DisplayGitStatus(GitStatus status);

    /// <summary>Registers the installed checkout as a workspace root and makes it the selected Git worktree.</summary>
    /// <param name="repository">The installed native checkout.</param>
    /// <returns>The registered root and worktree identity.</returns>
    GitProvisionedWorkspace OpenGitWorkspace(GitWorktree repository);
}
