namespace RetroIde.Application.Git;

/// <summary>Identifies a checkout and its separate or shared Git administrative directories.</summary>
/// <remarks>2026-11-26 · TR-RETRO-GIT-013#ac-git-machine-state. Keep separate worktree identity even when object storage is shared.</remarks>
/// <param name="WorktreePath">The canonical working directory of the checkout.</param>
/// <param name="GitDirectory">The checkout's Git administrative directory.</param>
/// <param name="CommonDirectory">The administrative directory shared by related worktrees.</param>
internal sealed record GitWorktree(string WorktreePath, string GitDirectory, string CommonDirectory);
