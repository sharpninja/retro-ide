using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RetroIde.Application.Workspaces;

namespace RetroIde.Workbench;

internal sealed class ExplorerTreeNode : ObservableObject
{
    private readonly WorkbenchLocalization _localization;
    private string _name;
    private WorkspaceExplorer.RootKind? _rootKind;
    private bool _isExpanded;
    private bool? _rootAvailable;
    private string? _gitBadgeSlug;

    public ExplorerTreeNode(WorkspaceExplorer.Address? address, string name, bool isDirectory, WorkbenchLocalization localization,
        WorkspaceExplorer.RootKind? rootKind = null)
    {
        Address = address;
        _name = name;
        IsDirectory = isDirectory;
        _localization = localization;
        _rootKind = rootKind;
        if (isDirectory)
            Children.Add(new(null, WorkbenchText.ExplorerLoading, isDirectory: false, localization));
    }

    public WorkspaceExplorer.Address? Address { get; }
    public bool IsDirectory { get; }
    public ObservableCollection<ExplorerTreeNode> Children { get; } = [];
    public string Label => _rootKind is { } kind
        ? WorkbenchText.ExplorerRoot(_localization, kind, _name, unavailable: _rootAvailable == false)
        : Address is null ? _localization.Text(_name) : _name;

    public string GitBadge => _gitBadgeSlug is { } slug ? _localization.Text(slug) : string.Empty;
    public bool HasGitBadge => _gitBadgeSlug is not null;

    internal void RefreshLabel()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(GitBadge));
    }

    internal void SetGitBadge(string? slug)
    {
        if (SetProperty(ref _gitBadgeSlug, slug, nameof(GitBadge)))
            OnPropertyChanged(nameof(HasGitBadge));
    }

    internal void UpdateCaption(string name, WorkspaceExplorer.RootKind? rootKind = null)
    {
        if (!string.Equals(_name, name, StringComparison.Ordinal) || _rootKind != rootKind)
        {
            _name = name;
            _rootKind = rootKind;
            RefreshLabel();
        }
    }

    internal void SetRootAvailability(bool available)
    {
        if (_rootAvailable != available)
        {
            _rootAvailable = available;
            OnPropertyChanged(nameof(Label));
        }
    }
    public bool IsExpanded { get => _isExpanded; internal set => SetProperty(ref _isExpanded, value); }
    internal long ReadVersion { get; private set; }
    internal CancellationTokenSource? ActiveRead { get; private set; }

    internal void BeginRead(CancellationTokenSource read)
    {
        InvalidateRead();
        ActiveRead = read;
    }

    internal void EndRead(CancellationTokenSource read)
    {
        if (ReferenceEquals(ActiveRead, read))
            ActiveRead = null;
    }

    internal void InvalidateRead()
    {
        ReadVersion++;
        ActiveRead?.Cancel();
        ActiveRead = null;
    }
}
