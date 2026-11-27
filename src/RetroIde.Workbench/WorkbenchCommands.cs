using System.Globalization;
using Avalonia.Input;
using RetroIde.Application;
using RetroIde.Application.Localization;
using RetroIde.Application.Workspaces;

namespace RetroIde.Workbench;

internal static class WorkbenchCommands
{
    public static readonly CommandDescriptor FocusTool = Create(FeatureRegistry.FocusToolId, "Ctrl+Shift+F");
    public static readonly CommandDescriptor OpenFolder = Create("workspace.open-folder", "Ctrl+Shift+O");
    public static readonly CommandDescriptor OpenProject = Create("workspace.open-project", "Ctrl+Alt+O");
    public static readonly CommandDescriptor Save = Create("document.save", "Ctrl+S");
    public static readonly CommandDescriptor Reveal = Create("document.reveal", "Ctrl+Alt+R");
    public static readonly CommandDescriptor Search = Create("document.search", "Ctrl+F");
    public static readonly CommandDescriptor Next = Create("document.next", "F3");
    public static readonly CommandDescriptor Previous = Create("document.previous", "Shift+F3");
    public static readonly CommandDescriptor Replace = Create("document.replace", "Ctrl+Alt+H");
    public static readonly CommandDescriptor ReplaceAll = Create("document.replace-all", "Ctrl+Alt+Shift+H");
    public static readonly CommandDescriptor Undo = Create("document.undo", "Ctrl+Z");
    public static readonly CommandDescriptor Redo = Create("document.redo", "Ctrl+Y");
    public static readonly CommandDescriptor Close = Create("document.close", "Ctrl+W");
    public static readonly CommandDescriptor Discard = Create("document.discard", "Ctrl+Alt+Shift+W");
    public static readonly CommandDescriptor OpenSourceControl = Create("git.source-control.focus", "Ctrl+Alt+G");
    public static readonly CommandDescriptor SelectRepository = Create("git.repository.select", "Ctrl+Alt+Shift+G");
    public static readonly CommandDescriptor RefreshGitStatus = Create("git.status.read", "Ctrl+Alt+Shift+R");
    public static readonly CommandDescriptor InitializeRepository = Create("git.repository.initialize", "Ctrl+Alt+I");
    public static readonly CommandDescriptor CloneRepository = Create("git.repository.clone", "Ctrl+Alt+Shift+C");
    public static readonly CommandDescriptor OpenGitDiff = Create("git.diff.open", "Ctrl+Alt+D");
    public static readonly CommandDescriptor StageGitFile = Create("git.file.stage", "Ctrl+Alt+S");
    public static readonly CommandDescriptor UnstageGitFile = Create("git.file.unstage", "Ctrl+Alt+Shift+S");
    public static readonly CommandDescriptor RefreshGitDiff = Create("git.diff.refresh", "Ctrl+Alt+Shift+D");
    public static readonly CommandDescriptor OpenGitBlame = Create("git.blame.open", "Ctrl+Alt+F9");
    public static readonly CommandDescriptor RefreshGitBlame = Create("git.blame.refresh", "Ctrl+Alt+Shift+F9");
    public static readonly CommandDescriptor StageGitHunks = Create("git.hunks.stage", "Ctrl+Alt+K");
    public static readonly CommandDescriptor UnstageGitHunks = Create("git.hunks.unstage", "Ctrl+Alt+Shift+K");
    public static readonly CommandDescriptor CreateCommit = Create("git.commit.create", "Ctrl+Alt+C");
    public static readonly CommandDescriptor AmendCommit = Create("git.commit.amend", "Ctrl+Alt+M");
    public static readonly CommandDescriptor ContinueGitOperation = Create("git.operation.continue", "Ctrl+Alt+Shift+Enter");
    public static readonly CommandDescriptor AbortGitOperation = Create("git.operation.abort", "Ctrl+Alt+Shift+Escape");
    public static readonly CommandDescriptor SkipGitOperation = Create("git.operation.skip", "Ctrl+Alt+Shift+J");
    public static readonly CommandDescriptor StartGitOperation = Create("git.operation.start", "Ctrl+Alt+Shift+I");
    public static readonly CommandDescriptor ResetToGitCommit = Create("git.reset.commit", "Ctrl+Alt+Shift+X");
    public static readonly CommandDescriptor OpenGitStashes = Create("git.stash.open", "Ctrl+Alt+Shift+U");
    public static readonly CommandDescriptor RefreshGitStashes = Create("git.stash.list", "Alt+Shift+U");
    public static readonly CommandDescriptor InspectGitStash = Create("git.stash.inspect", "Alt+Shift+I");
    public static readonly CommandDescriptor CreateGitStash = Create("git.stash.create", "Alt+Shift+S");
    public static readonly CommandDescriptor ApplyGitStash = Create("git.stash.apply", "Alt+Shift+A");
    public static readonly CommandDescriptor PopGitStash = Create("git.stash.pop", "Alt+Shift+P");
    public static readonly CommandDescriptor DropGitStash = Create("git.stash.drop", "Alt+Shift+D");
    public static readonly CommandDescriptor OpenGitPatches = Create("git.patch.open", "Ctrl+Alt+Shift+Y");
    public static readonly CommandDescriptor ExportGitPatch = Create("git.patch.export", "Alt+Shift+E");
    public static readonly CommandDescriptor ReviewGitPatch = Create("git.patch.review", "Alt+Shift+O");
    public static readonly CommandDescriptor ApplyGitPatch = Create("git.patch.apply", "Alt+Shift+Y");
    public static readonly CommandDescriptor OpenGitSubmodules = Create("git.submodule.open", "Ctrl+Shift+F7");
    public static readonly CommandDescriptor RefreshGitSubmodules = Create("git.submodule.list", "Alt+Shift+F7");
    public static readonly CommandDescriptor InitGitSubmodule = Create("git.submodule.init", "Ctrl+Shift+F8");
    public static readonly CommandDescriptor UpdateGitSubmodule = Create("git.submodule.update", "Alt+Shift+F8");
    public static readonly CommandDescriptor SyncGitSubmodule = Create("git.submodule.sync", "Ctrl+Shift+F9");
    public static readonly CommandDescriptor OpenGitHistory = Create("git.history.open", "Ctrl+Alt+L");
    public static readonly CommandDescriptor CurrentGitHistory = Create("git.history.current", "Ctrl+Alt+Shift+L");
    public static readonly CommandDescriptor ShowGitRefHistory = Create("git.history.ref", "Ctrl+Alt+Shift+B");
    public static readonly CommandDescriptor PreviousGitHistoryPage = Create("git.history.previous-page", "Alt+PageUp");
    public static readonly CommandDescriptor NextGitHistoryPage = Create("git.history.next-page", "Alt+PageDown");
    public static readonly CommandDescriptor InspectGitCommit = Create("git.history.commit-details", "Ctrl+Alt+Enter");
    public static readonly CommandDescriptor CreateGitBranch = Create("git.branch.create", "Ctrl+Alt+B");
    public static readonly CommandDescriptor SwitchGitBranch = Create("git.branch.switch", "Ctrl+Alt+Shift+V");
    public static readonly CommandDescriptor RenameGitBranch = Create("git.branch.rename", "Ctrl+Alt+Shift+N");
    public static readonly CommandDescriptor DeleteGitBranch = Create("git.branch.delete", "Ctrl+Alt+Delete");
    public static readonly CommandDescriptor CreateGitTag = Create("git.tag.create", "Ctrl+Alt+F8");
    public static readonly CommandDescriptor InspectGitTag = Create("git.tag.read", "Ctrl+Alt+Shift+F8");
    public static readonly CommandDescriptor DeleteGitTag = Create("git.tag.delete", "Ctrl+Alt+Shift+F7");
    public static readonly CommandDescriptor RefreshGitWorktrees = Create("git.worktree.list", "Alt+Shift+F6");
    public static readonly CommandDescriptor AddGitWorktree = Create("git.worktree.add", "Ctrl+Shift+F6");
    public static readonly CommandDescriptor OpenGitWorktree = Create("git.worktree.open", "Alt+Shift+F10");
    public static readonly CommandDescriptor LockGitWorktree = Create("git.worktree.lock", "Ctrl+Shift+F10");
    public static readonly CommandDescriptor UnlockGitWorktree = Create("git.worktree.unlock", "Alt+Shift+F11");
    public static readonly CommandDescriptor RemoveGitWorktree = Create("git.worktree.remove", "Ctrl+Shift+F11");
    public static readonly CommandDescriptor ShowGitRemotes = Create("git.remotes.open", "Ctrl+Alt+Shift+T");
    public static readonly CommandDescriptor RefreshGitRemotes = Create("git.remotes.read", "Alt+Shift+R");
    public static readonly CommandDescriptor AddGitRemote = Create("git.remote.add", "Ctrl+Alt+A");
    public static readonly CommandDescriptor EditGitRemote = Create("git.remote.url", "Ctrl+Alt+E");
    public static readonly CommandDescriptor RenameGitRemote = Create("git.remote.rename", "Ctrl+Alt+N");
    public static readonly CommandDescriptor RemoveGitRemote = Create("git.remote.remove", "Ctrl+Alt+Shift+Delete");
    public static readonly CommandDescriptor FetchGitRemote = Create("git.remote.fetch", "Ctrl+Alt+F");
    public static readonly CommandDescriptor PushGitRemote = Create("git.remote.push", "Ctrl+Alt+P");
    public static readonly CommandDescriptor PullGitRemote = Create("git.remote.pull", "Ctrl+Alt+Shift+P");
    public static readonly CommandDescriptor SetGitUpstream = Create("git.branch.upstream", "Ctrl+Alt+U");

    // A document key binding can be active inside a main or floating workbench window.
    // All commands in this catalog can share that input route, so check them together.
    private static readonly CommandDescriptor[] Commands =
        [FocusTool, OpenFolder, OpenProject, Save, Reveal, Search, Next, Previous, Replace, ReplaceAll, Undo, Redo, Close, Discard,
            OpenSourceControl, SelectRepository, RefreshGitStatus, InitializeRepository, CloneRepository, OpenGitDiff, StageGitFile, UnstageGitFile,
            RefreshGitDiff, OpenGitBlame, RefreshGitBlame, StageGitHunks, UnstageGitHunks, CreateCommit, AmendCommit, ContinueGitOperation, AbortGitOperation, SkipGitOperation,
            StartGitOperation, ResetToGitCommit, OpenGitStashes, RefreshGitStashes, InspectGitStash, CreateGitStash, ApplyGitStash,
            PopGitStash, DropGitStash, OpenGitPatches, ExportGitPatch, ReviewGitPatch, ApplyGitPatch,
            OpenGitSubmodules, RefreshGitSubmodules, InitGitSubmodule, UpdateGitSubmodule, SyncGitSubmodule,
            OpenGitHistory, CurrentGitHistory,
            ShowGitRefHistory, PreviousGitHistoryPage, NextGitHistoryPage, InspectGitCommit, CreateGitBranch, SwitchGitBranch,
            CreateGitTag, InspectGitTag, DeleteGitTag,
            RefreshGitWorktrees, AddGitWorktree, OpenGitWorktree, LockGitWorktree, UnlockGitWorktree, RemoveGitWorktree,
            RenameGitBranch, DeleteGitBranch, ShowGitRemotes, RefreshGitRemotes, AddGitRemote, EditGitRemote,
            RenameGitRemote, RemoveGitRemote, FetchGitRemote, PushGitRemote, PullGitRemote, SetGitUpstream];

    internal static void ValidateBundledResources()
    {
        using var english = new WorkbenchLocalization(initial: CultureInfo.GetCultureInfo("en"));
        foreach (var slug in WorkbenchText.Required.Concat(DockLayout.RequiredPaneText()))
            if (string.IsNullOrWhiteSpace(english.Text(slug)))
                throw new InvalidDataException($"The required English resource '{slug}' is empty.");
        _ = WorkbenchText.ExplorerRoot(english, WorkspaceExplorer.RootKind.Folder, "example", unavailable: false);
        _ = WorkbenchText.GitDiffName(english, "example.asm", "staged");
        _ = WorkbenchText.GitBlameName(english, "example.asm");
        _ = WorkbenchText.GitRemoteDivergence(english, 0, 0);
        _ = WorkbenchText.ExplorerRoot(english, WorkspaceExplorer.RootKind.Project, "example", unavailable: true);
        Validate(Commands, english);
    }

    internal static void Validate(IReadOnlyList<CommandDescriptor> descriptors, WorkbenchLocalization english)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var defaults = new Dictionary<KeyGesture, string>();
        var windows = new Dictionary<KeyGesture, string>();
        var linux = new Dictionary<KeyGesture, string>();
        foreach (var descriptor in descriptors)
        {
            if (!LanguagePack.IsResourceSlug(descriptor.Id) || !ids.Add(descriptor.Id))
                throw new InvalidDataException($"Invalid or duplicate command ID '{descriptor.Id}'.");
            if (!LanguagePack.IsResourceSlug(descriptor.LabelSlug) || !LanguagePack.IsResourceSlug(descriptor.IconSlug))
                throw new InvalidDataException($"Command '{descriptor.Id}' requires valid text and image slugs.");
            if (string.IsNullOrWhiteSpace(english.Text(descriptor.LabelSlug)))
                throw new InvalidDataException($"Command '{descriptor.Id}' requires a nonempty English label.");
            english.Image(descriptor.IconSlug);

            AddGesture(descriptor, descriptor.DefaultGesture, "default", defaults);
            AddGesture(descriptor, descriptor.GestureFor(windows: true), "Windows", windows);
            AddGesture(descriptor, descriptor.GestureFor(windows: false), "Linux", linux);
        }
        if (english.Diagnostics is { Count: > 0 } diagnostics)
            throw new InvalidDataException("Bundled localization resources failed validation: " +
                string.Join(", ", diagnostics.Select(issue => $"{issue.Kind} '{issue.Slug}' ({issue.Code})").Order(StringComparer.Ordinal)));
    }

    private static void AddGesture(CommandDescriptor descriptor, string gesture, string platform, Dictionary<KeyGesture, string> assigned)
    {
        if (string.IsNullOrWhiteSpace(gesture))
            throw new InvalidDataException($"Command '{descriptor.Id}' requires a {platform} gesture.");
        KeyGesture parsed;
        try { parsed = KeyGesture.Parse(gesture); }
        catch (Exception error) when (error is ArgumentException or FormatException)
        {
            throw new InvalidDataException($"Command '{descriptor.Id}' has an invalid {platform} gesture '{gesture}'.", error);
        }
        if (parsed.Key == Key.None || !Enum.IsDefined(parsed.Key))
            throw new InvalidDataException($"Command '{descriptor.Id}' has an invalid {platform} gesture '{gesture}'.");
        if (!assigned.TryAdd(parsed, descriptor.Id))
            throw new InvalidDataException($"Commands '{assigned[parsed]}' and '{descriptor.Id}' share {platform} gesture '{parsed}'.");
    }

    private static CommandDescriptor Create(string id, string gesture) => new(id, $"command.{id}.label", $"command.{id}.icon", gesture);
}
