using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Application.Workspaces;

namespace RetroIde.Workbench;

internal sealed partial class ExplorerViewModel(WorkbenchViewModel workbench, ExplorerFeatures features) : ObservableObject, IView
{
    internal sealed record ExpansionIntent(ExplorerTreeNode Node, bool Expanded);

    private readonly SemaphoreSlim _mutations = new(1, 1);
    private IReadOnlyList<WorkspaceExplorer.Root> _roots = [];
    private bool _rootCommandsEnabled;
    private WorkspaceExplorer.Address? _selection;
    private ExplorerTreeNode? _selectedNode;
    private GitExplorerBadges? _gitBadges;
    private HashSet<WorkspaceExplorer.Address> _expanded = new(WorkspaceExplorer.AddressEquality);

    public Guid ViewKey { get; } = Guid.NewGuid();
    public Guid WorkspaceId => workbench.WorkspaceId;
    internal WorkbenchLocalization Localization => workbench.Localization ?? WorkbenchLocalization.English;
    public IReadOnlyList<WorkspaceExplorer.Root> Roots => _roots;
    public ObservableCollection<ExplorerTreeNode> TreeRoots { get; } = [];
    public ExplorerTreeNode? SelectedNode => _selectedNode;

    [ObservableProperty]
    public partial OperationResult? LastResult { get; private set; }

    public event EventHandler<ViewCommandRequested>? CommandRequested;

    internal Guid? ParentViewKey { get; set; }
    internal Func<CancellationToken, Task<string?>>? PickDirectoryAsync { get; set; }

    public void Receive(OperationResult result)
    {
        Dispatcher.UIThread.VerifyAccess();
        LastResult = result;
    }

    internal void ReplaceState(WorkspaceExplorer.State state)
    {
        Dispatcher.UIThread.VerifyAccess();
        _expanded = new HashSet<WorkspaceExplorer.Address>(state.Expanded, WorkspaceExplorer.AddressEquality);
        _selection = state.Selection;
        SetProperty(ref _roots, state.Roots, nameof(Roots));

        var visibleRoots = TreeRoots.ToDictionary(node => node.Address!.Value.RootId);
        foreach (var root in state.Roots)
        {
            if (!visibleRoots.Remove(root.Id, out var node))
            {
                node = new ExplorerTreeNode(new WorkspaceExplorer.Address(root.Id, ""), root.Path, isDirectory: true, Localization, root.Kind);
                TreeRoots.Add(node);
            }
            else
            {
                node.UpdateCaption(root.Path, root.Kind);
            }
            UpdateGitBadge(node, root.Path);
            RestoreExpansion(node);
        }
        foreach (var removed in visibleRoots.Values)
        {
            InvalidateSubtree(removed);
            TreeRoots.Remove(removed);
        }
        RefreshSelectedNode();
    }

    internal void SetGitBadges(GitExplorerBadges? badges)
    {
        Dispatcher.UIThread.VerifyAccess();
        _gitBadges = badges;
        foreach (var node in TreeRoots)
        {
            var rootPath = _roots.FirstOrDefault(root => root.Id == node.Address?.RootId)?.Path;
            UpdateGitBadges(node, rootPath);
        }
    }

    private void UpdateGitBadges(ExplorerTreeNode node, string? rootPath)
    {
        UpdateGitBadge(node, rootPath);
        foreach (var child in node.Children)
            UpdateGitBadges(child, rootPath);
    }

    private void UpdateGitBadge(ExplorerTreeNode node, string? rootPath) =>
        node.SetGitBadge(rootPath is not null && node.Address is { } address
            ? _gitBadges?.Read(Path.Combine(rootPath, address.RelativePath))
            : null);

    internal void RefreshBranches(IReadOnlyList<WorkspaceWatchSet.Change> changes)
    {
        Dispatcher.UIThread.VerifyAccess();
        var invalidated = changes.ToDictionary(change => change.Directory, WorkspaceExplorer.AddressEquality);
        foreach (var root in TreeRoots)
        {
            var address = root.Address!.Value;
            invalidated.TryGetValue(address, out var rootChange);
            if (rootChange?.RootAvailable is { } available)
                root.SetRootAvailability(available);
            foreach (var node in ExpandedDirectories(root))
            {
                if (rootChange?.Subtree == true || invalidated.ContainsKey(node.Address!.Value))
                    BrowseCommand.Execute(node);
            }
        }
    }

    private static IEnumerable<ExplorerTreeNode> ExpandedDirectories(ExplorerTreeNode node)
    {
        if (!node.IsExpanded)
            yield break;
        yield return node;
        foreach (var child in node.Children)
            foreach (var directory in ExpandedDirectories(child))
                yield return directory;
    }

    private void RestoreExpansion(ExplorerTreeNode node)
    {
        if (node.Address is not { } address || !node.IsDirectory)
            return;

        var expanded = _expanded.Contains(address);
        if (node.IsExpanded != expanded)
        {
            if (expanded)
            {
                node.IsExpanded = true;
                BrowseCommand.Execute(node);
            }
            else
            {
                CollapseSubtree(node);
            }
        }

        if (expanded)
            foreach (var child in node.Children.ToArray())
                RestoreExpansion(child);
    }

    internal void CancelPendingReads()
    {
        foreach (var root in TreeRoots)
            InvalidateSubtree(root);
    }

    private static void InvalidateSubtree(ExplorerTreeNode node)
    {
        node.InvalidateRead();
        foreach (var child in node.Children)
            InvalidateSubtree(child);
    }

    private static void CollapseSubtree(ExplorerTreeNode node)
    {
        node.InvalidateRead();
        node.IsExpanded = false;
        foreach (var child in node.Children)
            CollapseSubtree(child);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SelectEntryAsync(ExplorerTreeNode? node)
    {
        if (node?.Address is { } address && !ReferenceEquals(node, SelectedNode))
            await MutateAsync(features.SelectEntry.Id, new ExplorerFeatures.SelectEntryInput(address.RootId, address.RelativePath),
                CancellationToken.None).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenEntryAsync(ExplorerTreeNode? node, CancellationToken token)
    {
        if (node is { IsDirectory: false, Address: { } address })
            await MutateAsync(features.OpenEntry.Id, new ExplorerFeatures.OpenEntryInput(address.RootId, address.RelativePath), token)
                .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenLocationAsync(ExplorerFeatures.OpenEntryInput? location, CancellationToken token)
    {
        if (location is not null)
            await MutateAsync(features.OpenEntry.Id, location, token).ConfigureAwait(true);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SetExpansionAsync(ExpansionIntent? intent)
    {
        if (intent is { Node: { IsDirectory: true, Address: { } address } })
            await MutateAsync(features.SetExpansion.Id, new ExplorerFeatures.SetExpansionInput(address.RootId, address.RelativePath, intent.Expanded),
                CancellationToken.None).ConfigureAwait(true);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task BrowseAsync(ExplorerTreeNode? node)
    {
        if (node is not { IsExpanded: true, Address: { } address })
            return;

        using var read = new CancellationTokenSource();
        node.BeginRead(read);
        try
        {
            var result = await SendAsync(features.ReadChildren.Id, new ExplorerFeatures.ReadChildrenInput(address.RootId, address.RelativePath),
                read.Token).ConfigureAwait(true);

            if (!ReferenceEquals(node.ActiveRead, read) || ParentViewKey is null ||
                result?.Data is not { } data || data.Deserialize<WorkspaceExplorer.Branch>(WireJson.Options) is not { } branch ||
                branch.WorkspaceId != WorkspaceId || !WorkspaceExplorer.AddressEquality.Equals(branch.Parent, address))
                return;

            if (address.RelativePath.Length == 0)
                node.SetRootAvailability(branch.IsAvailable);
            ApplyChildren(node, branch);
            RefreshSelectedNode();
        }
        finally
        {
            node.EndRead(read);
        }
    }

    private void ApplyChildren(ExplorerTreeNode parent, WorkspaceExplorer.Branch branch)
    {
        var previouslyVisible = parent.Children.Where(node => node.Address is not null)
            .ToDictionary(node => node.Address!.Value, WorkspaceExplorer.AddressEquality);
        var visible = new List<ExplorerTreeNode>();
        foreach (var entry in branch.Entries)
        {
            if (previouslyVisible.Remove(entry.Address, out var existing) && existing.IsDirectory == entry.IsDirectory)
            {
                existing.UpdateCaption(entry.Name);
                visible.Add(existing);
            }
            else
            {
                if (existing is not null)
                    InvalidateSubtree(existing);
                visible.Add(new ExplorerTreeNode(entry.Address, entry.Name, entry.IsDirectory, Localization));
            }
        }
        if (!branch.IsAvailable)
            visible.Add(new ExplorerTreeNode(null, WorkbenchText.ExplorerUnavailable, isDirectory: false, Localization));

        foreach (var removed in previouslyVisible.Values)
            InvalidateSubtree(removed);
        UpdateChildren(parent.Children, visible);
        var rootPath = _roots.FirstOrDefault(root => root.Id == branch.Parent.RootId)?.Path;
        foreach (var child in visible)
        {
            UpdateGitBadge(child, rootPath);
            RestoreExpansion(child);
        }
    }

    private static void UpdateChildren(ObservableCollection<ExplorerTreeNode> current, List<ExplorerTreeNode> desired)
    {
        var retained = new HashSet<ExplorerTreeNode>(desired);
        for (var i = current.Count - 1; i >= 0; i--)
            if (!retained.Contains(current[i]))
                current.RemoveAt(i);
        var existing = new HashSet<ExplorerTreeNode>(current);
        for (var i = 0; i < desired.Count; i++)
        {
            var item = desired[i];
            if (i < current.Count && ReferenceEquals(current[i], item))
                continue;
            if (existing.Contains(item))
                current.Move(current.IndexOf(item), i);
            else
                current.Insert(i, item);
        }
    }

    private void RefreshSelectedNode()
    {
        var selected = _selection is { } address ? FindVisible(TreeRoots, address) : null;
        SetProperty(ref _selectedNode, selected, nameof(SelectedNode));
    }

    private static ExplorerTreeNode? FindVisible(IEnumerable<ExplorerTreeNode> nodes, WorkspaceExplorer.Address address)
    {
        foreach (var node in nodes)
        {
            if (node.Address is not { } candidate || candidate.RootId != address.RootId)
                continue;
            if (WorkspaceExplorer.AddressEquality.Equals(candidate, address))
                return node;
            if (node.IsExpanded && FindVisible(node.Children, address) is { } selected)
                return selected;
        }
        return null;
    }

    internal void SetRootEligibility(bool enabled)
    {
        if (_rootCommandsEnabled == enabled)
            return;
        _rootCommandsEnabled = enabled;
        OpenFolderCommand.NotifyCanExecuteChanged();
        OpenProjectCommand.NotifyCanExecuteChanged();
    }

    private bool CanOpenRoot() => _rootCommandsEnabled;

    [RelayCommand(CanExecute = nameof(CanOpenRoot))]
    private Task OpenFolderAsync(string? path, CancellationToken token) => OpenRootAsync(path, "folder", token);

    [RelayCommand(CanExecute = nameof(CanOpenRoot))]
    private Task OpenProjectAsync(string? path, CancellationToken token) => OpenRootAsync(path, "project", token);

    private async Task OpenRootAsync(string? path, string kind, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!CanOpenRoot())
            return;

        if (path is null && PickDirectoryAsync is { } pick)
            path = await pick(token).ConfigureAwait(true);
        if (path is null || token.IsCancellationRequested)
            return;

        await MutateAsync(features.OpenRoot.Id, new ExplorerFeatures.OpenRootInput(path, kind), token).ConfigureAwait(true);
    }

    private async Task MutateAsync(string commandId, object input, CancellationToken token)
    {
        await _mutations.WaitAsync(token).ConfigureAwait(true);
        try
        {
            if (_rootCommandsEnabled)
                await SendAsync(commandId, input, token).ConfigureAwait(true);
        }
        finally
        {
            _mutations.Release();
        }
    }

    private async Task<OperationResult?> SendAsync(string commandId, object input, CancellationToken token)
    {
        var request = new ViewCommandRequested(Guid.NewGuid(), commandId, workbench.Address, ViewKey, input, token);
        CommandRequested?.Invoke(this, request);
        if (request.Completion is { } completion)
            await completion.ConfigureAwait(true);
        else if (ParentViewKey is null)
            Receive(OperationResult.Failure(request.RequestId, "view_closed", "The Project Explorer registration is no longer live."));
        return request.Result;
    }
}
