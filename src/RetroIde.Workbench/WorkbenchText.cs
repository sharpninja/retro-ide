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
            ExplorerLoading, ExplorerUnavailable, GitMenu, GitStaged, GitWorking, GitConflicts];

    internal static string ExplorerRoot(WorkbenchLocalization localization, WorkspaceExplorer.RootKind kind, string path, bool unavailable)
    {
        var title = localization.Text(kind == WorkspaceExplorer.RootKind.Project ? ProjectRoot : FolderRoot,
            new Dictionary<string, object>(StringComparer.Ordinal) { ["path"] = path });
        return unavailable
            ? localization.Text(UnavailableRoot, new Dictionary<string, object>(StringComparer.Ordinal) { ["root"] = title })
            : title;
    }

    internal static string PaneTitle(string id) => $"pane.{id}.title";

    internal static string PaneDescription(string id) => $"pane.{id}.description";
}
