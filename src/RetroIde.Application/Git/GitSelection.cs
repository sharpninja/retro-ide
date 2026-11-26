namespace RetroIde.Application.Git;

/// <summary>Captures the selected worktree and the authorized filesystem scope for status reads.</summary>
/// <remarks>2026-11-26 · TR-RETRO-GIT-013#ac-git-machine-state. Capture authorization at selection and reuse it for status.</remarks>
/// <param name="Repository">The selected checkout and repository identity.</param>
/// <param name="ScopeRoot">The maximum directory scope available to an attached client.</param>
internal sealed record GitSelection(GitWorktree Repository, string ScopeRoot);
