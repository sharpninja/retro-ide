using RetroIde.Application.Workspaces;

namespace RetroIde.Workbench;

internal static class WorkbenchText
{
    internal const string FileMenu = "menu.file";
    internal const string ViewMenu = "menu.view";
    internal const string LanguageMenu = "menu.language";
    internal const string GitMenu = "menu.git";
    internal const string GitStaged = "git.status.group.staged";
    internal const string GitWorking = "git.status.group.working";
    internal const string GitConflicts = "git.status.group.conflicts";
    internal const string GitDiffTitle = "git.diff.title";
    internal const string GitDiffStaged = "git.diff.layer.staged";
    internal const string GitDiffWorking = "git.diff.layer.working";
    internal const string GitBadgeTracked = "explorer.git.tracked";
    internal const string GitBadgeUntracked = "explorer.git.untracked";
    internal const string GitBadgeIgnored = "explorer.git.ignored";
    internal const string GitBadgeModified = "explorer.git.modified";
    internal const string GitBadgeStaged = "explorer.git.staged";
    internal const string GitBadgeStagedModified = "explorer.git.staged-modified";
    internal const string GitBadgeConflicted = "explorer.git.conflicted";
    internal const string GitInitAction = "git.setup.initialize";
    internal const string GitCloneAction = "git.setup.clone";
    internal const string GitSetupSource = "git.setup.source";
    internal const string GitSetupDestination = "git.setup.destination";
    internal const string GitSetupBrowse = "git.setup.browse";
    internal const string GitSetupCancel = "git.setup.cancel";
    internal const string SearchQuery = "editor.search.query";
    internal const string SearchReplacement = "editor.search.replacement";
    internal const string SearchMatchCase = "editor.search.match-case";
    internal const string FocusSelector = "toolbar.focus.selector";
    internal const string NoEmulator = "status.no-emulator";
    internal const string ExplorerLoading = "explorer.branch.loading";
    internal const string ExplorerUnavailable = "explorer.branch.unavailable";

    private const string FolderRoot = "explorer.root.folder";
    private const string ProjectRoot = "explorer.root.project";
    private const string UnavailableRoot = "explorer.root.unavailable";

    internal static IReadOnlyList<string> Required { get; } =
        [FileMenu, ViewMenu, LanguageMenu, SearchQuery, SearchReplacement, SearchMatchCase, FocusSelector, NoEmulator,
            ExplorerLoading, ExplorerUnavailable, GitMenu, GitStaged, GitWorking, GitConflicts, GitDiffStaged, GitDiffWorking,
            GitInitAction, GitCloneAction, GitSetupSource, GitSetupDestination, GitSetupBrowse, GitSetupCancel,
            GitBadgeTracked, GitBadgeUntracked, GitBadgeIgnored, GitBadgeModified, GitBadgeStaged, GitBadgeStagedModified, GitBadgeConflicted];

    internal static string ExplorerRoot(WorkbenchLocalization localization, WorkspaceExplorer.RootKind kind, string path, bool unavailable)
    {
        var title = localization.Text(kind == WorkspaceExplorer.RootKind.Project ? ProjectRoot : FolderRoot,
            new Dictionary<string, object>(StringComparer.Ordinal) { ["path"] = path });
        return unavailable
            ? localization.Text(UnavailableRoot, new Dictionary<string, object>(StringComparer.Ordinal) { ["root"] = title })
            : title;
    }

    internal static string PaneTitle(string id) => $"pane.{id}.title";

    internal static string GitDiffName(WorkbenchLocalization localization, string path, string layer) =>
        localization.Text(GitDiffTitle, new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["path"] = path,
            ["layer"] = localization.Text(string.Equals(layer, "staged", StringComparison.Ordinal) ? GitDiffStaged : GitDiffWorking),
        });

    internal static string PaneDescription(string id) => $"pane.{id}.description";
}
