using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Application.Git;
using RetroIde.Application.Workspaces;

namespace RetroIde.Workbench;

/// <summary>Projects the selected worktree and routes Source Control commands through its registered MVC owner.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos; TEST-RETRO-015#ac-test-mcp-same-instance. Desktop and MCP use the same Git selection and status.</remarks>
internal sealed partial class SourceControlViewModel(WorkbenchViewModel workbench, FeatureRegistry registry, InteractionBlocks interaction) : ObservableObject, IView
{
    internal const string PaneId = "source-control";

    private WorkspaceExplorer.Address? _explorerSelection;
    private bool _available;

    public Guid ViewKey { get; } = Guid.NewGuid();

    public Guid WorkspaceId => workbench.WorkspaceId;

    internal Guid? ParentViewKey { get; set; }

    internal WorkbenchLocalization Localization => workbench.Localization ?? WorkbenchLocalization.English;

    internal Func<bool, CancellationToken, Task<GitSetupDialog.Choice?>>? PromptSetupAsync { get; set; }

    public event EventHandler<ViewCommandRequested>? CommandRequested;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshStatusCommand))]
    [NotifyPropertyChangedFor(nameof(HasRepository))]
    public partial GitWorktree? Repository { get; private set; }

    public bool HasRepository => Repository is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Staged), nameof(Working), nameof(Conflicts))]
    public partial GitStatus? Status { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErrorMessage))]
    public partial OperationResult? LastResult { get; private set; }

    public IEnumerable<GitStatusEntry> Staged => Status?.Entries.Where(entry => !entry.IsUnmerged && entry.Index is not ("." or "?" or "!")) ?? [];
    public IEnumerable<GitStatusEntry> Working => Status?.Entries.Where(entry => !entry.IsUnmerged && entry.Worktree is not ("." or "!")) ?? [];
    public IEnumerable<GitStatusEntry> Conflicts => Status?.Entries.Where(entry => entry.IsUnmerged) ?? [];
    public string? ErrorMessage => LastResult is { Code: not "ok" } result ? result.Summary : null;

    public void Receive(OperationResult result)
    {
        Dispatcher.UIThread.VerifyAccess();
        LastResult = result;
    }

    internal void SetScope(GitWorktree repository)
    {
        Dispatcher.UIThread.VerifyAccess();
        Status = null;
        Repository = repository;
        LastResult = null;
    }

    internal void DisplayStatus(GitStatus status)
    {
        Dispatcher.UIThread.VerifyAccess();
        Status = status;
    }

    internal void SetAvailability(WorkspaceExplorer.Address? selection, bool available)
    {
        if (_explorerSelection == selection && _available == available)
            return;
        _explorerSelection = selection;
        _available = available;
        OpenPaneCommand.NotifyCanExecuteChanged();
        SelectRepositoryCommand.NotifyCanExecuteChanged();
        RefreshStatusCommand.NotifyCanExecuteChanged();
        InitializeRepositoryCommand.NotifyCanExecuteChanged();
        CloneRepositoryCommand.NotifyCanExecuteChanged();
    }

    private bool CanOpenPane() => _available;
    private bool CanSelectRepository() => _available && _explorerSelection is not null;
    private bool CanRefreshStatus() => _available && Repository is not null;

    [RelayCommand(CanExecute = nameof(CanOpenPane))]
    private Task OpenPaneAsync(CancellationToken cancellationToken) =>
        RequestAsync(registry.FocusTool, new FocusToolInput(PaneId), cancellationToken);

    [RelayCommand(CanExecute = nameof(CanSelectRepository))]
    private async Task SelectRepositoryAsync(CancellationToken cancellationToken)
    {
        if (_explorerSelection is not { } selection)
            return;
        await RequestAsync(registry.Git.Select, new GitFeatures.LocateInput(selection.RootId, selection.RelativePath), cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanRefreshStatus))]
    private Task RefreshStatusAsync(CancellationToken cancellationToken) =>
        RequestAsync(registry.Git.Status, new GitFeatures.StatusInput(), cancellationToken);

    [RelayCommand(CanExecute = nameof(CanOpenPane))]
    private Task InitializeRepositoryAsync(CancellationToken cancellationToken) =>
        ProvisionAsync(clone: false, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanOpenPane))]
    private Task CloneRepositoryAsync(CancellationToken cancellationToken) =>
        ProvisionAsync(clone: true, cancellationToken);

    private async Task ProvisionAsync(bool clone, CancellationToken cancellationToken)
    {
        if (PromptSetupAsync is not { } prompt ||
            await prompt(clone, cancellationToken).ConfigureAwait(true) is not { } choice ||
            cancellationToken.IsCancellationRequested || ParentViewKey is null)
            return;

        await OpenPaneAsync(cancellationToken).ConfigureAwait(true);
        if (cancellationToken.IsCancellationRequested || ParentViewKey is null)
            return;

        interaction.Begin(PaneId);
        try
        {
            if (clone && choice.Source is { } source)
                await RequestAsync(registry.Git.Clone, new GitFeatures.CloneInput(source, choice.Destination), cancellationToken).ConfigureAwait(true);
            else if (!clone)
                await RequestAsync(registry.Git.Initialize, new GitFeatures.InitializeInput(choice.Destination), cancellationToken).ConfigureAwait(true);
        }
        finally { interaction.End(PaneId); }
    }

    private async Task RequestAsync<TInput>(Feature<TInput> feature, TInput input, CancellationToken cancellationToken) where TInput : class
    {
        Dispatcher.UIThread.VerifyAccess();
        var request = new ViewCommandRequested(Guid.NewGuid(), feature.Id, workbench.Address, ViewKey, input, cancellationToken);
        CommandRequested?.Invoke(this, request);
        if (request.Completion is { } completion)
            await completion.ConfigureAwait(true);
    }
}
