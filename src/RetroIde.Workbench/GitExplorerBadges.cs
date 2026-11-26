using RetroIde.Application.Git;

namespace RetroIde.Workbench;

/// <summary>Projects native Git status onto rows inside the selected worktree and its authorized directory.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos. Use full path identity to keep linked worktree changes independent.</remarks>
internal sealed class GitExplorerBadges
{
    private static readonly StringComparer NativePaths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly string _worktree;
    private readonly string _scope;
    private readonly Dictionary<string, string> _badges = new(NativePaths);
    private readonly HashSet<string> _ignoredDirectories = new(NativePaths);

    internal GitExplorerBadges(GitSelection selection, GitStatus status)
    {
        _worktree = selection.Repository.WorktreePath;
        _scope = selection.ScopeRoot;
        foreach (var path in status.TrackedPaths)
            _badges[NativePath(path)] = WorkbenchText.GitBadgeTracked;

        foreach (var entry in status.Entries)
        {
            var key = NativePath(entry.Path);
            _badges[key] = BadgeForEntry(entry);
            if (string.Equals(entry.Index, "!", StringComparison.Ordinal) && entry.Path.EndsWith('/'))
                _ignoredDirectories.Add(key);
        }
    }

    internal string? Read(string path)
    {
        if (!NativeGit.ContainsPath(_scope, path))
            return null;
        var relative = Path.GetRelativePath(_worktree, path);
        if (_badges.TryGetValue(relative, out var badge))
            return badge;

        for (var parent = Path.GetDirectoryName(relative); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
            if (_ignoredDirectories.Contains(parent))
                return WorkbenchText.GitBadgeIgnored;

        return null;
    }

    private static string NativePath(string path) => path.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar);

    private static string BadgeForEntry(GitStatusEntry entry) => entry switch
    {
        { IsUnmerged: true } => WorkbenchText.GitBadgeConflicted,
        { Index: "?" } => WorkbenchText.GitBadgeUntracked,
        { Index: "!" } => WorkbenchText.GitBadgeIgnored,
        { Index: ".", Worktree: "." } => WorkbenchText.GitBadgeTracked,
        { Index: "." } => WorkbenchText.GitBadgeModified,
        { Worktree: "." } => WorkbenchText.GitBadgeStaged,
        _ => WorkbenchText.GitBadgeStagedModified,
    };
}
