using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace RetroIde.Workbench;

/// <summary>Hosts real Dock groups and keeps the workbench model synchronized with Dock selection.</summary>
/// <remarks>TEST-RETRO-001#verify-ac-behavior-desktop-1; TEST-RETRO-001#verify-ac-behavior-desktop-2</remarks>
public sealed class DockLayout : IDisposable
{
    private readonly IWorkbenchWorkspaceContext _workspace;
    private readonly WorkbenchLocalization _localization;
    private readonly Dictionary<string, ShellTool> _tools;
    private readonly Dictionary<string, ShellDocument> _documents;
    private readonly HashSet<ShellDocument> _transientDocuments = [];
    private readonly DockBranch[] _floating;
    private bool _suppress;
    private bool _shown;
    private bool _disposed;

    internal DockLayout(IWorkbenchWorkspaceContext workspace, WorkbenchLayoutState? state, InteractionBlocks interaction,
        ExplorerViewModel explorer, SourceControlViewModel sourceControl, WorkbenchLocalization localization)
    {
        _workspace = workspace;
        _localization = localization;
        Factory = new Factory
        {
            DefaultHostWindowLocator = () => new HostWindow(),
        };
        _tools = new[]
        {
            new ShellTool("explorer", "Project Explorer", "Open a project or folder to browse files.", "left", interaction) { Explorer = explorer },
            new ShellTool(SourceControlViewModel.PaneId, "Source Control", "No Git worktree selected.", "left", interaction) { SourceControl = sourceControl },
            new ShellTool("registers", "Registers", "No debugger session connected.", "right", interaction),
            new ShellTool("call-stack", "Call Stack", "No debugger session connected.", "right", interaction),
            new ShellTool("output", "Output", "Workbench ready.", "bottom", interaction),
            new ShellTool("problems", "Problems", "No diagnostics.", "bottom", interaction),
            new ShellTool("serial", "Serial Console", "No session connected.", "bottom", interaction),
            new ShellTool("breakpoints", "Breakpoints", "No breakpoints.", "bottom", interaction),
            new ShellTool("mcp-activity", "MCP Activity", "No agent activity.", "bottom", interaction),
        }.ToDictionary(tool => tool.Id, StringComparer.Ordinal);
        _documents = new[]
        {
            new ShellDocument("start", "Start Page", "Open an emulator session to begin.", interaction),
            new ShellDocument("emulation", "Emulation", "No emulator session connected.", interaction),
            new ShellDocument("disassembly", "Disassembly", "No debugger session connected.", interaction),
            new ShellDocument("test-results", "Test Results", "No test run selected.", interaction),
        }.ToDictionary(document => document.Id, StringComparer.Ordinal);

        foreach (var tool in _tools.Values)
            tool.Localization = localization;
        foreach (var document in _documents.Values)
            document.Localization = localization;
        RefreshLocale(null, EventArgs.Empty);
        localization.Changed += RefreshLocale;

        state = RestoreCompatibleLayout(state);
        _floating = state?.Floating ?? [];
        Root = CreateRoot(Build(state?.Main.Content ?? DefaultTopology()), state?.Main.Pinned ?? []);
        Factory.InitLayout(Root);

        _suppress = true;
        ActiveToolId = state?.ActiveTool;
        if (ActiveToolId is { } toolId)
            Focus(toolId);
        _suppress = false;
        Revision = 0;
        Factory.FocusedDockableChanged += (_, args) =>
        {
            if (_suppress)
                return;

            if (args.Dockable is ShellDocument document)
                ActivateDocumentBranch(document);
            else if (args.Dockable is ShellTool tool && !string.Equals(ActiveToolId, tool.Id, StringComparison.Ordinal))
                Publish(tool.Id);
        };
        Factory.DockableMoved += (_, _) =>
        {
            if (!_suppress)
                Publish(ActiveToolId);
        };
        Factory.WindowClosed += (_, _) => Dispatcher.UIThread.Post(RestoreLostPanes, DispatcherPriority.Background);
    }

    /// <summary>Gets the Dock package factory used by both primary and floating controls.</summary>
    public Factory Factory { get; }

    /// <summary>Gets the primary Dock root.</summary>
    public IRootDock Root { get; }

    /// <summary>Gets the workbench's last active tool.</summary>
    public string? ActiveToolId { get; private set; }

    /// <summary>Gets the current workbench selection revision.</summary>
    public long Revision { get; private set; }

    /// <summary>Gets the selected document in the main host.</summary>
    public string? SelectedDocumentId => FindSelectedDocument(Root)?.Id;

    internal DocumentViewModel? CurrentSource => FindSelectedDocument(Root)?.Source;

    internal IEnumerable<ShellTool> Tools => _tools.Values;

    internal event EventHandler? Changed;

    /// <summary>Resolves an allowlisted tool.</summary>
    public Tool Tool(string id) => _tools[id];

    /// <summary>Indicates whether a live tool can receive focus.</summary>
    public bool CanFocus(string id) => _tools.TryGetValue(id, out var tool) && Factory.FindRoot(tool) is not null;

    /// <summary>Activates a tool's actual tab and marks it as the Dock root's focused item.</summary>
    public bool Focus(string id)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!CanFocus(id))
            return false;

        var tool = _tools[id];
        var wasSuppressed = _suppress;
        _suppress = true;
        try
        {
            if (Factory.IsDockablePinned(tool))
                Factory.PreviewPinnedDockable(tool);
            if (tool.Owner is not IDock owner)
                return false;

            Factory.SetActiveDockable(tool);
            Factory.SetFocusedDockable(owner, tool);
            Factory.ActivateWindow(tool);
            if (!IsFocused(id))
                return false;
        }
        finally { _suppress = wasSuppressed; }

        var publishedRevision = Publish(id);
        return Revision == publishedRevision && string.Equals(ActiveToolId, id, StringComparison.Ordinal) && IsFocused(id);
    }

    /// <summary>Checks the selected tab and focus state in the Dock model.</summary>
    public bool IsFocused(string id) =>
        _tools.TryGetValue(id, out var tool) && Factory.FindRoot(tool)?.FocusedDockable == tool &&
        tool.Owner is IDock { IsActive: true } owner && owner.ActiveDockable == tool;

    /// <summary>Selects a shell document within its current Dock group.</summary>
    public void SelectDocument(string id) => Factory.SetActiveDockable(_documents[id]);

    internal void AddTransientDocument(ShellDocument document)
    {
        _transientDocuments.Add(document);
        Factory.AddDockable(FindDocumentGroup(), document);
    }

    internal void ForgetTransientDocument(ShellDocument document) => _transientDocuments.Remove(document);

    internal void ActivateDocument(ShellDocument document)
    {
        if (IsDetached(document))
            Factory.AddDockable(FindDocumentGroup(), document);
        if (document.Owner is not IDock owner)
            return;

        Factory.SetActiveDockable(document);
        Factory.SetFocusedDockable(owner, document);
        ActivateDocumentBranch(document);
        Factory.ActivateWindow(document);
        AdvanceWorkspaceRevision();
    }

    private IDocumentDock FindDocumentGroup()
    {
        if (FindSelectedDocument(Root)?.Owner is IDocumentDock active)
            return active;
        if (Descendants(Root).OfType<IDocumentDock>().FirstOrDefault() is { } existing)
            return existing;

        var created = Factory.CreateDocumentDock();
        created.Id = "documents";
        Factory.SplitToDock(Root.VisibleDockables!.OfType<IDock>().First(), created, DockOperation.Right);
        return created;
    }

    internal void MoveToolToGroup(string id, string group)
    {
        var destination = Descendants(Root).OfType<IToolDock>().First(d => string.Equals(d.Id, group, StringComparison.Ordinal));
        var tool = _tools[id];
        if (tool.Owner is IDock source && !ReferenceEquals(source, destination))
            Factory.MoveDockable(source, destination, tool, destination.VisibleDockables?.LastOrDefault());
    }

    internal void ShowFloatingLayouts()
    {
        if (_shown)
            return;
        _shown = true;

        var previous = ActiveToolId;
        _suppress = true;
        try
        {
            foreach (var branch in _floating)
            {
                var root = CreateRoot(Build(branch.Content), branch.Pinned);
                var window = Factory.CreateDockWindow();
                window.Layout = root;
                window.Title = "Retro IDE";
                window.Width = Math.Clamp(branch.Width ?? 420, 240, 1200);
                window.Height = Math.Clamp(branch.Height ?? 300, 180, 900);
                root.Window = window;
                Factory.AddWindow(Root, window);
                window.Present(isDialog: false);
            }
            if (previous is not null)
                Focus(previous);
        }
        finally { _suppress = false; }
    }

    /// <summary>Captures only topology, tab selection, auto-hidden panes, and relative presentation sizes.</summary>
    public WorkbenchLayoutState Capture()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_shown)
            RestoreLostPanes();
        var floating = Root.Windows?.Where(window => window.Layout is { } layout && HasPersistablePanes(layout))
            .Select(window =>
            {
                window.Save();
                return CaptureBranch(window.Layout!) with { Width = Size(window.Width), Height = Size(window.Height) };
            }).ToArray();
        return new WorkbenchLayoutState(1, CaptureBranch(Root), _shown ? floating ?? [] : _floating, ActiveToolId);
    }

    private static bool HasPersistablePanes(IRootDock root) =>
        Descendants(root).Any(child => child is ShellTool or ShellDocument { IsTransient: false }) ||
        (root.LeftPinnedDockables?.Count ?? 0) + (root.RightPinnedDockables?.Count ?? 0) +
        (root.TopPinnedDockables?.Count ?? 0) + (root.BottomPinnedDockables?.Count ?? 0) > 0;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _localization.Changed -= RefreshLocale;
        if (Root.ExitWindows.CanExecute(null))
            Root.ExitWindows.Execute(null);
    }

    private void RefreshLocale(object? sender, EventArgs args)
    {
        foreach (var tool in _tools.Values)
            tool.Title = _localization.Text(WorkbenchText.PaneTitle(tool.Id));
        foreach (var document in _documents.Values)
            document.Title = _localization.Text(WorkbenchText.PaneTitle(document.Id));
    }

    private WorkbenchLayoutState? RestoreCompatibleLayout(WorkbenchLayoutState? layout)
    {
        if (layout is not { Version: 1, Main: not null, Floating.Length: <= 12 } || layout.Main.Content is null)
            return null;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (!VisitBranch(layout.Main) || !layout.Floating.All(VisitBranch) ||
            (layout.ActiveTool is { } tool && (!_tools.ContainsKey(tool) || !ids.Contains(tool))))
            return null;

        var paneCount = _tools.Count + _documents.Count;
        if (ids.Count == paneCount)
            return layout;

        // Existing layouts predate Source Control. Keep their topology and add one auto-hidden pane.
        if (ids.Count == paneCount - 1 && !ids.Contains(SourceControlViewModel.PaneId))
            return layout with { Main = layout.Main with { Pinned = [.. layout.Main.Pinned, new(SourceControlViewModel.PaneId, "Left", "left")] } };

        return null;

        bool VisitBranch(DockBranch branch) =>
            branch is { Pinned: not null, Content.Kind: "vertical" or "horizontal" or "tool-tabs" or "document-tabs" } &&
            Visit(branch.Content, 0) && branch.Pinned.All(item =>
                item is { Tool: not null, Alignment: "Left" or "Right" or "Top" or "Bottom" } &&
                _tools.ContainsKey(item.Tool) && ids.Add(item.Tool));

        bool Visit(DockNode? node, int depth)
        {
            if (node is null || depth > 24)
                return false;
            return node.Kind switch
            {
                "tool" => node.Id is not null && _tools.ContainsKey(node.Id) && ids.Add(node.Id),
                "document" => node.Id is not null && _documents.ContainsKey(node.Id) && ids.Add(node.Id),
                "horizontal" or "vertical" =>
                    node.Children is { Length: <= 24 } && node.Children.All(child =>
                        child is { Kind: not ("tool" or "document") } && Visit(child, depth + 1)),
                "tool-tabs" or "document-tabs" =>
                    node.Children is { Length: <= 24 } && node.Children.All(child =>
                        child is { Kind: "tool" or "document" } && Visit(child, depth + 1)),
                "splitter" => true,
                _ => false,
            };
        }
    }

    private IRootDock CreateRoot(IDockable content, PinnedTool[] pins)
    {
        var root = Factory.CreateRootDock();
        root.Id = "root";
        root.Context = _workspace;
        root.IsCollapsable = false;
        root.FloatingWindowHostMode = DockFloatingWindowHostMode.Native;
        root.VisibleDockables = Factory.CreateList(content);
        root.ActiveDockable = content;
        root.DefaultDockable = content;
        RestorePinned(root, pins);
        return root;
    }

    private IDockable Build(DockNode node)
    {
        switch (node.Kind)
        {
            case "tool": return _tools[node.Id!];
            case "document": return _documents[node.Id!];
            case "splitter": return new ProportionalDockSplitter { CanResize = true, ResizePreview = true };
        }

        IDock dock = node.Kind switch
        {
            "tool-tabs" => new ToolDock { Alignment = ParseAlignment(node.Alignment), GripMode = GripMode.Visible },
            "document-tabs" => new DocumentDock { EnableWindowDrag = true },
            _ => new ProportionalDock
            {
                Orientation = string.Equals(node.Kind, "horizontal", StringComparison.Ordinal) ? Orientation.Horizontal : Orientation.Vertical,
            },
        };
        dock.Id = GroupId(node.Id) ?? string.Empty;
        dock.IsCollapsable = dock is IToolDock or IProportionalDock;
        if (node.Proportion is > 0 and <= 1)
        {
            dock.Proportion = node.Proportion.Value;
            dock.CollapsedProportion = node.Proportion.Value;
        }
        dock.VisibleDockables = Factory.CreateList(node.Children!.Select(Build).ToArray());
        dock.ActiveDockable = dock.VisibleDockables.FirstOrDefault(child => string.Equals(child.Id, node.Active, StringComparison.Ordinal))
            ?? dock.VisibleDockables.FirstOrDefault(child => child is not ISplitter);
        return dock;
    }

    private static DockBranch CaptureBranch(IRootDock root)
    {
        var groupIds = CaptureDockGroupIds(root);
        var pinned = PinList(root.LeftPinnedDockables, Alignment.Left)
            .Concat(PinList(root.RightPinnedDockables, Alignment.Right))
            .Concat(PinList(root.TopPinnedDockables, Alignment.Top))
            .Concat(PinList(root.BottomPinnedDockables, Alignment.Bottom)).ToArray();
        var body = root.VisibleDockables!.First(child => child is IDock);
        return new DockBranch(CaptureNode(body), pinned);

        IEnumerable<PinnedTool> PinList(IList<IDockable>? dockables, Alignment alignment) =>
            (dockables ?? []).OfType<ShellTool>().Select(tool => new PinnedTool(tool.Id, alignment.ToString(),
                (tool.OriginalOwner as IToolDock ?? tool.Owner as IToolDock) is { } owner ? groupIds.GetValueOrDefault(owner) : null));

        string? CapturedId(IDockable? item) => item switch
        {
            IDock dock => groupIds.GetValueOrDefault(dock),
            ShellDocument { IsTransient: true } => null,
            _ => GroupId(item?.Id),
        };

        DockNode CaptureNode(IDockable item) => item switch
        {
            ShellTool tool => new("tool", tool.Id),
            ShellDocument document => new("document", document.Id),
            ISplitter => new("splitter"),
            IProportionalDock split => CaptureGroup(split, split.Orientation == Orientation.Horizontal ? "horizontal" : "vertical"),
            IToolDock tools => CaptureGroup(tools, "tool-tabs", tools.Alignment.ToString()),
            IDocumentDock documents => CaptureGroup(documents, "document-tabs"),
            IDock fallback => CaptureGroup(fallback, "tool-tabs"),
            _ => throw new InvalidOperationException("Unsupported workbench dockable."),
        };

        DockNode CaptureGroup(IDock dock, string kind, string? alignment = null) =>
            new(kind, CapturedId(dock), CaptureProportion(dock),
                CapturedId(dock.ActiveDockable), (dock.VisibleDockables ?? [])
                    .Where(child => child is not ShellDocument { IsTransient: true }).Select(CaptureNode).ToArray(), alignment);
    }

    private static double? CaptureProportion(IDock dock)
    {
        // The current proportions are redistributed while a sibling is auto-hidden.
        // Save the original split sizes so the hidden pane can reclaim its space.
        var proportion = dock.Owner is IProportionalDock parent && dock.CollapsedProportion is > 0 and <= 1 &&
            parent.VisibleDockables?.Any(child => child is IDock { IsEmpty: true, IsCollapsable: true }) == true
                ? dock.CollapsedProportion : dock.Proportion;
        return proportion is > 0 and <= 1 ? proportion : null;
    }

    private static Dictionary<IDock, string> CaptureDockGroupIds(IRootDock root)
    {
        var docks = Descendants(root).OfType<IDock>().ToArray();
        var existingIds = docks.Select(dock => dock.Id).ToHashSet(StringComparer.Ordinal);
        var assignedIds = new HashSet<string>(StringComparer.Ordinal);
        var groupIds = new Dictionary<IDock, string>(ReferenceEqualityComparer.Instance);
        var suffix = 0;

        // Dock can leave newly split groups unnamed or copy an existing ID. Give
        // each group a unique saved ID for pins and persisted active selections.
        foreach (var group in docks)
        {
            var id = GroupId(group.Id);
            if (id is null || !assignedIds.Add(id))
            {
                do { id = FormattableString.Invariant($"dock-group-{++suffix}"); }
                while (!existingIds.Add(id));
            }
            groupIds.Add(group, id);
        }

        return groupIds;
    }

    private void RestorePinned(IRootDock root, PinnedTool[] pins)
    {
        var owners = Descendants(root).OfType<IToolDock>().ToArray();
        // Dock initializes pinned tools from the root's edge lists. A missing original
        // dock is valid: Dock can find or create an aligned destination when unpinning.
        foreach (var edge in pins.GroupBy(pin => ParseAlignment(pin.Alignment)))
        {
            var pinned = Factory.CreateList<IDockable>();
            foreach (var pin in edge)
            {
                var tool = _tools[pin.Tool];
                tool.OriginalOwner = owners.FirstOrDefault(owner => owner.Alignment == edge.Key &&
                    string.Equals(owner.Id, pin.Group, StringComparison.Ordinal));
                pinned.Add(tool);
            }

            switch (edge.Key)
            {
                case Alignment.Left: root.LeftPinnedDockables = pinned; break;
                case Alignment.Right: root.RightPinnedDockables = pinned; break;
                case Alignment.Top: root.TopPinnedDockables = pinned; break;
                case Alignment.Bottom: root.BottomPinnedDockables = pinned; break;
            }
        }
    }

    private void RestoreLostPanes()
    {
        if (_disposed)
            return;

        foreach (var tool in _tools.Values.Where(IsDetached))
        {
            var owner = Descendants(Root).OfType<IToolDock>()
                .FirstOrDefault(group => string.Equals(group.Id, tool.DefaultGroup, StringComparison.Ordinal))
                ?? CreateRecoveryGroup(tool.DefaultGroup);
            Factory.AddDockable(owner, tool);
            owner.ActiveDockable ??= tool;
        }

        foreach (var document in _documents.Values.Concat(_transientDocuments).Where(IsDetached))
        {
            var owner = FindDocumentGroup();
            Factory.AddDockable(owner, document);
            owner.ActiveDockable ??= document;
        }
    }

    private bool IsDetached(IDockable dockable) =>
        Factory.FindRoot(dockable) is not { } root || (!ReferenceEquals(root, Root) && root.Window?.Owner is null);

    private IToolDock CreateRecoveryGroup(string id)
    {
        var (alignment, operation) = id switch
        {
            "left" => (Alignment.Left, DockOperation.Left),
            "bottom" => (Alignment.Bottom, DockOperation.Bottom),
            _ => (Alignment.Right, DockOperation.Right),
        };
        var owner = Factory.CreateToolDock();
        owner.Id = id;
        owner.Alignment = alignment;
        var anchor = Descendants(Root).OfType<IDocumentDock>().FirstOrDefault() as IDock
            ?? Root.VisibleDockables!.OfType<IDock>().First();
        Factory.SplitToDock(anchor, owner, operation);
        return owner;
    }

    internal void AdvanceWorkspaceRevision() => Publish(ActiveToolId);

    private long Publish(string? id)
    {
        ActiveToolId = id;
        if (_suppress)
            return Revision;

        var publishedRevision = ++Revision;
        Changed?.Invoke(this, EventArgs.Empty);
        return publishedRevision;
    }

    private static double? Size(double size) => double.IsFinite(size) && size > 0 ? size : null;

    private static string? GroupId(string? id) =>
        id is { Length: > 0 and <= 128 } && id.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
            ? id : null;

    private static Alignment ParseAlignment(string? alignment) => alignment switch
    {
        "Left" or "Unset" => Alignment.Left,
        "Top" => Alignment.Top,
        "Bottom" => Alignment.Bottom,
        _ => Alignment.Right,
    };

    private static void ActivateDocumentBranch(ShellDocument document)
    {
        // Dock updates the focused document without necessarily selecting its containing split.
        for (IDockable child = document; child.Owner is IDock owner; child = owner)
        {
            if (!ReferenceEquals(owner.ActiveDockable, child))
                owner.ActiveDockable = child;
            if (owner is IRootDock)
                break;
        }
    }

    private static ShellDocument? FindSelectedDocument(IDock dock)
    {
        var active = dock.ActiveDockable;
        if (active is ShellDocument document)
            return document;
        if (active is IDock activeGroup && FindSelectedDocument(activeGroup) is { } selected)
            return selected;

        // A tool can take focus while the document groups retain their selected tabs.
        foreach (var group in (dock.VisibleDockables ?? []).OfType<IDock>())
        {
            if (!ReferenceEquals(group, active) && FindSelectedDocument(group) is { } fallback)
                return fallback;
        }

        return null;
    }

    private static IEnumerable<IDockable> Descendants(IDock dock)
    {
        foreach (var child in dock.VisibleDockables ?? [])
        {
            yield return child;
            if (child is IDock branch)
            {
                foreach (var descendant in Descendants(branch))
                    yield return descendant;
            }
        }
    }

    internal static IEnumerable<string> RequiredPaneText() => PaneIds(DefaultTopology())
        .SelectMany(id => new[] { WorkbenchText.PaneTitle(id), WorkbenchText.PaneDescription(id) });

    private static IEnumerable<string> PaneIds(DockNode node)
    {
        if (node.Kind is "tool" or "document" && node.Id is { } id)
            yield return id;
        foreach (var child in node.Children ?? [])
            foreach (var pane in PaneIds(child))
                yield return pane;
    }

    private static DockNode DefaultTopology() => new("vertical", "main", Children:
    [
        new("horizontal", "columns", 0.73, Children:
        [
            new("tool-tabs", "left", 0.20, "explorer", [new("tool", "explorer"), new("tool", SourceControlViewModel.PaneId)], "Left"),
            new("splitter"),
            new("document-tabs", "documents", 0.56, "start",
                [new("document", "start"), new("document", "emulation"), new("document", "disassembly"), new("document", "test-results")]),
            new("splitter"),
            new("tool-tabs", "right", 0.24, "registers", [new("tool", "registers"), new("tool", "call-stack")], "Right"),
        ]),
        new("splitter"),
        new("tool-tabs", "bottom", 0.27, "output",
            [new("tool", "output"), new("tool", "problems"), new("tool", "serial"), new("tool", "breakpoints"), new("tool", "mcp-activity")], "Bottom"),
    ]);
}

internal sealed class ShellTool : Tool
{
    public ShellTool(string id, string title, string message, string defaultGroup, InteractionBlocks interaction)
    {
        Id = id;
        Title = title;
        Message = message;
        DefaultGroup = defaultGroup;
        Interaction = interaction;
        CanClose = false;
        CanFloat = true;
        CanPin = true;
    }

    internal ExplorerViewModel? Explorer { get; init; }
    internal SourceControlViewModel? SourceControl { get; init; }
    internal WorkbenchLocalization Localization { get; set; } = WorkbenchLocalization.English;
    public string Message { get; }
    public string DefaultGroup { get; }
    public InteractionBlocks Interaction { get; }
}

internal sealed class ShellDocument : Document
{
    public ShellDocument(string id, string title, string message, InteractionBlocks interaction)
    {
        Id = id;
        Title = title;
        Message = message;
        Interaction = interaction;
        CanClose = false;
        CanFloat = false;
    }

    internal DocumentViewModel? Source { get; init; }
    internal GitDiffViewModel? GitDiff { get; init; }
    internal GitHistoryViewModel? GitHistory { get; init; }
    internal GitBlameViewModel? GitBlame { get; init; }
    internal TerminalViewModel? GitTerminal { get; init; }
    internal bool IsTransient => Source is not null || GitDiff is not null || GitHistory is not null || GitBlame is not null || GitTerminal is not null;
    internal WorkbenchLocalization Localization { get; set; } = WorkbenchLocalization.English;
    public string Message { get; }
    public InteractionBlocks Interaction { get; }
}
