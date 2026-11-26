using RetroIde.Application.Workspaces;

namespace RetroIde.Application.Git;

/// <summary>Reports an installed worktree and its stable workspace root.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos. Return one workspace admission receipt for Init and Clone.</remarks>
internal sealed record GitProvisionedWorkspace(WorkspaceExplorer.Root Root, GitWorktree Repository);
