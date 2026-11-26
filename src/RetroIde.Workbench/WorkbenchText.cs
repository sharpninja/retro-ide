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
    internal const string GitAbortExplanation = "git.operation.abort-explanation";
    internal const string GitStartCheckout = "git.operation.start.checkout";
    internal const string GitStartTarget = "git.operation.start.target";
    internal const string GitStartKind = "git.operation.start.kind";
    internal const string GitStartMerge = "git.operation.start.merge";
    internal const string GitStartRebase = "git.operation.start.rebase";
    internal const string GitStartCherryPick = "git.operation.start.cherry-pick";
    internal const string GitStartRevert = "git.operation.start.revert";
    internal const string GitStartApply = "git.operation.start.apply";
    internal const string GitMainline = "git.operation.start.mainline";
    internal const string GitDetachedHead = "git.operation.start.detached";
    private const string GitMergeOperation = "git.operation.merge";
    private const string GitRebaseOperation = "git.operation.rebase";
    private const string GitCherryPickOperation = "git.operation.cherry-pick";
    private const string GitRevertOperation = "git.operation.revert";
    private const string GitOtherOperation = "git.operation.other";
    internal const string GitDiffTitle = "git.diff.title";
    internal const string GitDiffStaged = "git.diff.layer.staged";
    internal const string GitDiffWorking = "git.diff.layer.working";
    internal const string GitDiffHunks = "git.diff.hunks";
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
    internal const string GitCommitMessage = "git.commit.message";
    internal const string GitAmendNotice = "git.commit.amend-notice";
    internal const string GitNoCommit = "git.commit.no-head";
    internal const string GitNoStagedChanges = "git.commit.no-staged-changes";
    internal const string GitHistoryTitle = "git.history.title";
    internal const string GitHistoryRefs = "git.history.refs";
    internal const string GitHistoryGraph = "git.history.graph";
    internal const string GitHistoryWorktrees = "git.history.worktrees";
    internal const string GitBranchName = "git.branch.name";
    internal const string GitBranchCheckout = "git.branch.checkout";
    internal const string GitRemoteTitle = "git.remotes.title";
    internal const string GitRemoteName = "git.remotes.name";
    internal const string GitRemoteUrl = "git.remotes.url";
    internal const string GitRemotePush = "git.remotes.push-url";
    internal const string GitRemoteClose = "git.remotes.close";
    internal const string GitSyncLocalBranch = "git.remotes.local-branch";
    internal const string GitSyncRemoteBranch = "git.remotes.remote-branch";
    internal const string GitSyncStrategy = "git.remotes.strategy";
    internal const string GitSyncFastForward = "git.remotes.fast-forward";
    internal const string GitSyncMerge = "git.remotes.merge";
    internal const string GitSyncRebase = "git.remotes.rebase";
    internal const string GitSyncForceWithLease = "git.remotes.force-with-lease";
    internal const string GitSyncExpectedRemoteHead = "git.remotes.expected-remote-head";
    internal const string GitUpstreamClear = "git.upstream.clear";
    private const string GitDivergence = "git.remotes.divergence";
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
            ExplorerLoading, ExplorerUnavailable, GitMenu, GitStaged, GitWorking, GitConflicts, GitDiffStaged, GitDiffWorking, GitDiffHunks,
            GitAbortExplanation, GitMergeOperation, GitRebaseOperation, GitCherryPickOperation, GitRevertOperation, GitOtherOperation,
            GitStartCheckout, GitStartTarget, GitStartKind, GitStartMerge, GitStartRebase, GitStartCherryPick, GitStartRevert,
            GitStartApply, GitMainline, GitDetachedHead,
            GitInitAction, GitCloneAction, GitSetupSource, GitSetupDestination, GitSetupBrowse, GitSetupCancel,
            GitCommitMessage, GitAmendNotice, GitNoCommit, GitNoStagedChanges,
            GitHistoryTitle, GitHistoryRefs, GitHistoryGraph, GitHistoryWorktrees,
            GitBranchName, GitBranchCheckout,
            GitRemoteTitle, GitRemoteName, GitRemoteUrl, GitRemotePush, GitRemoteClose, GitUpstreamClear,
            GitSyncLocalBranch, GitSyncRemoteBranch, GitSyncStrategy, GitSyncFastForward, GitSyncMerge, GitSyncRebase,
            GitSyncForceWithLease, GitSyncExpectedRemoteHead,
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

    internal static string GitOperation(WorkbenchLocalization localization, string kind) => localization.Text(kind switch
    {
        "merge" => GitMergeOperation,
        "rebase" => GitRebaseOperation,
        "cherry-pick" => GitCherryPickOperation,
        "revert" => GitRevertOperation,
        _ => GitOtherOperation,
    });

    internal static string GitRemoteDivergence(WorkbenchLocalization localization, int ahead, int behind) =>
        localization.Text(GitDivergence, new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["ahead"] = ahead, ["behind"] = behind,
        });

    internal static string GitDiffName(WorkbenchLocalization localization, string path, string layer) =>
        localization.Text(GitDiffTitle, new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["path"] = path,
            ["layer"] = localization.Text(string.Equals(layer, "staged", StringComparison.Ordinal) ? GitDiffStaged : GitDiffWorking),
        });

    internal static string PaneDescription(string id) => $"pane.{id}.description";
}
