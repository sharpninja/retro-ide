namespace RetroIde.Application.Git;

/// <summary>Represents a Git path without losing its staged, working-tree, or rename state.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos. Retain literal paths from the NUL-delimited protocol.</remarks>
/// <param name="Path">The checkout-relative path, written exactly as Git returned it.</param>
/// <param name="OriginalPath">The source path for a rename or copy.</param>
/// <param name="Index">The staged status code.</param>
/// <param name="Worktree">The working-tree status code.</param>
/// <param name="IsUnmerged">Whether Git reports the path as unmerged.</param>
/// <param name="Submodule">Git's four-character submodule status for a tracked entry.</param>
internal sealed record GitStatusEntry(string Path, string? OriginalPath, string Index, string Worktree, bool IsUnmerged, string? Submodule);
