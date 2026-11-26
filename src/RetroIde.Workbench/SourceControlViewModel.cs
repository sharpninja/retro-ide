using System.Text.Json;
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
    private (string Path, string Layer)? _file;

    public Guid ViewKey { get; } = Guid.NewGuid();

    public Guid WorkspaceId => workbench.WorkspaceId;

    internal Guid? ParentViewKey { get; set; }

    internal WorkbenchLocalization Localization => workbench.Localization ?? WorkbenchLocalization.English;

    internal Func<bool, CancellationToken, Task<GitSetupDialog.Choice?>>? PromptSetupAsync { get; set; }
    internal Func<GitCommitOperations.CommitState, bool, CancellationToken, Task<string?>>? PromptCommitAsync { get; set; }

    public event EventHandler<ViewCommandRequested>? CommandRequested;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshStatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand), nameof(AmendCommand))]
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
        SelectFile(null, "working");
        LastResult = null;
    }

    internal void DisplayStatus(GitStatus? status)
    {
        Dispatcher.UIThread.VerifyAccess();
        Status = status;
        if (_file is { } current && !(string.Equals(current.Layer, "staged", StringComparison.Ordinal) ? Staged : Working.Concat(Conflicts))
            .Any(entry => string.Equals(entry.Path, current.Path, StringComparison.Ordinal)))
            SelectFile(null, current.Layer);
    }

    internal void SelectFile(GitStatusEntry? entry, string layer)
    {
        _file = entry is null ? null : (entry.Path, layer);
        NotifyFileCommands();
    }

    internal void ClearFile(GitStatusEntry entry, string layer)
    {
        if (_file == (entry.Path, layer))
            SelectFile(null, layer);
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
        CommitCommand.NotifyCanExecuteChanged();
        AmendCommand.NotifyCanExecuteChanged();
        NotifyFileCommands();
    }

    private void NotifyFileCommands()
    {
        OpenDiffCommand.NotifyCanExecuteChanged();
        StageFileCommand.NotifyCanExecuteChanged();
        UnstageFileCommand.NotifyCanExecuteChanged();
    }

    private bool CanOpenPane() => _available;
    private bool CanSelectRepository() => _available && _explorerSelection is not null;
    private bool CanRefreshStatus() => _available && Repository is not null;
    private bool CanOpenDiff() => CanRefreshStatus() && _file is not null;
    private bool CanStageFile() => CanOpenDiff() && _file is { Layer: "working" };
    private bool CanUnstageFile() => CanOpenDiff() && _file is { Layer: "staged" };

    [RelayCommand(CanExecute = nameof(CanOpenDiff))]
    private Task OpenDiffAsync(CancellationToken cancellationToken) =>
        Repository is { } repo && _file is { } file
            ? RequestAsync(registry.Git.OpenDiff, new GitFileOperations.DiffInput(repo.WorktreePath, file.Path, file.Layer), cancellationToken)
            : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanStageFile))]
    private Task StageFileAsync(CancellationToken cancellationToken) =>
        UpdateIndexAsync(registry.Git.Stage, "working", cancellationToken);

    [RelayCommand(CanExecute = nameof(CanUnstageFile))]
    private Task UnstageFileAsync(CancellationToken cancellationToken) =>
        UpdateIndexAsync(registry.Git.Unstage, "staged", cancellationToken);

    private async Task UpdateIndexAsync(Feature<GitFileOperations.FileInput> feature, string layer, CancellationToken cancellationToken)
    {
        if (Repository is not { } repo || _file is not { } file || !string.Equals(file.Layer, layer, StringComparison.Ordinal))
            return;

        interaction.Begin(PaneId);
        try
        {
            await RequestAsync(feature, new GitFileOperations.FileInput(repo.WorktreePath, file.Path), cancellationToken).ConfigureAwait(true);
        }
        finally { interaction.End(PaneId); }
    }

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

    [RelayCommand(CanExecute = nameof(CanRefreshStatus))]
    private Task CommitAsync(CancellationToken cancellationToken) => CommitOrAmendAsync(amend: false, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanRefreshStatus))]
    private Task AmendAsync(CancellationToken cancellationToken) => CommitOrAmendAsync(amend: true, cancellationToken);

    private async Task CommitOrAmendAsync(bool amend, CancellationToken token)
    {
        if (PromptCommitAsync is not { } prompt || Repository is not { } repository)
            return;

        await OpenPaneAsync(token).ConfigureAwait(true);
        var result = await RequestAsync(registry.Git.CommitState,
            new GitCommitOperations.StateInput(repository.WorktreePath), token).ConfigureAwait(true);
        if (result is not { Code: "ok", Data: { } data } ||
            data.Deserialize<GitCommitOperations.CommitState>(WireJson.Options) is not { } state ||
            token.IsCancellationRequested || ParentViewKey is null)
            return;

        var message = await prompt(state, amend, token).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(message) || token.IsCancellationRequested || ParentViewKey is null)
            return;

        interaction.Begin(PaneId);
        try
        {
            await RequestAsync(amend ? registry.Git.Amend : registry.Git.Commit,
                new GitCommitOperations.CommitInput(repository.WorktreePath, state.Preimage, message), token).ConfigureAwait(true);
        }
        finally { interaction.End(PaneId); }
    }

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

    private async Task<OperationResult?> RequestAsync<TInput>(Feature<TInput> feature, TInput input, CancellationToken cancellationToken) where TInput : class
    {
        Dispatcher.UIThread.VerifyAccess();
        var request = new ViewCommandRequested(Guid.NewGuid(), feature.Id, workbench.Address, ViewKey, input, cancellationToken);
        CommandRequested?.Invoke(this, request);
        if (request.Completion is { } completion)
            await completion.ConfigureAwait(true);
        return request.Result;
    }
}
