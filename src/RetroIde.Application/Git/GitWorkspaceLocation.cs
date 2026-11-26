using RetroIde.Application.Workspaces;

namespace RetroIde.Application.Git;

/// <summary>Persists a checkout identity and its workspace-relative discovery directory.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos. Keep Git selection independent of the current Explorer row.</remarks>
internal sealed record GitWorkspaceLocation(WorkspaceExplorer.Address Address, GitWorktree Repository);
