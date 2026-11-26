using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

/// <summary>Owns the graph and inspection state inside one worktree's Git history document.</summary>
internal sealed partial class GitHistoryViewModel : ObservableObject, IView
{
    private readonly WorkbenchViewModel _workbench;
    private readonly GitFeatures _git;
    private readonly InteractionBlocks _interaction;

    internal GitHistoryViewModel(WorkbenchViewModel workbench, GitFeatures git, InteractionBlocks interaction, GitHistoryOperations.HistoryPage page,
        GitHistoryOperations.RefState references)
    {
        _workbench = workbench;
        _git = git;
        _interaction = interaction;
        Display(page, references);
    }

    public Guid ViewKey { get; } = Guid.NewGuid();
    public Guid WorkspaceId => _workbench.WorkspaceId;
    public event EventHandler<ViewCommandRequested>? CommandRequested;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand), nameof(PreviousPageCommand), nameof(CreateBranchCommand))]
    public partial GitHistoryOperations.HistoryPage Page { get; private set; }
    [ObservableProperty]
    public partial GitHistoryOperations.RefState References { get; private set; }
    [ObservableProperty]
    public partial GitHistoryGraph.Row[] Rows { get; private set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowReferenceCommand), nameof(SwitchBranchCommand), nameof(RenameBranchCommand), nameof(DeleteBranchCommand), nameof(SetUpstreamCommand),
        nameof(InspectTagCommand), nameof(DeleteTagCommand))]
    public partial GitHistoryOperations.Reference? SelectedReference { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InspectCommand), nameof(StartOperationCommand), nameof(ResetToCommitCommand), nameof(CreateTagCommand))]
    public partial GitHistoryGraph.Row? SelectedCommit { get; set; }
    [ObservableProperty]
    public partial GitHistoryOperations.CommitDetails? Details { get; private set; }
    [ObservableProperty]
    public partial OperationResult? LastResult { get; private set; }

    internal WorkbenchLocalization Localization => _workbench.Localization ?? WorkbenchLocalization.English;
    internal string Title => Localization.Text(WorkbenchText.GitHistoryTitle);
    internal string Scope => $"git-history-{ViewKey:N}";

    internal void Display(GitHistoryOperations.HistoryPage page, GitHistoryOperations.RefState references)
    {
        SelectedCommit = null;
        SelectedReference = null;
        Details = null;
        References = references;
        Page = page;
        Rows = GitHistoryGraph.Build(page.Commits);
    }

    internal GitHistoryOperations.HistoryTab Capture() => new(ViewKey, WorkspaceId, Page, References);

    public void Receive(OperationResult result)
    {
        Dispatcher.UIThread.VerifyAccess();
        LastResult = result;
    }

    private bool CanShowReference() => SelectedReference is not null;
    private bool CanInspect() => SelectedCommit is not null;
    private bool CanNextPage() => Page?.HasMore == true;
    private bool CanPreviousPage() => Page?.Offset > 0;

    [RelayCommand]
    private Task CurrentHistoryAsync(CancellationToken token) =>
        RequestAsync(_git.OpenHistory, new GitHistoryOperations.HistoryInput(Page.Repository.WorktreePath, Limit: Page.Limit), token);

    [RelayCommand(CanExecute = nameof(CanShowReference))]
    private Task ShowReferenceAsync(CancellationToken token) => SelectedReference is { } selected
        ? RequestAsync(_git.OpenHistory, new GitHistoryOperations.HistoryInput(Page.Repository.WorktreePath, selected.Name, Limit: Page.Limit), token)
        : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanNextPage))]
    private Task NextPageAsync(CancellationToken token) => MoveByAsync(Page.Limit, token);

    [RelayCommand(CanExecute = nameof(CanPreviousPage))]
    private Task PreviousPageAsync(CancellationToken token) => MoveByAsync(-Page.Limit, token);

    private Task MoveByAsync(int delta, CancellationToken token) => RequestAsync(_git.OpenHistory,
        new GitHistoryOperations.HistoryInput(Page.Repository.WorktreePath, Page.RefName, Page.Anchor, Math.Max(0, Page.Offset + delta), Page.Limit), token);

    [RelayCommand(CanExecute = nameof(CanInspect), AllowConcurrentExecutions = true)]
    private async Task InspectAsync(CancellationToken token)
    {
        if (SelectedCommit is not { } selected)
            return;
        Details = null;
        var result = await RequestAsync(_git.CommitDetails,
            new GitHistoryOperations.DetailsInput(Page.Repository.WorktreePath, selected.Commit.Id), token).ConfigureAwait(true);
        if (ReferenceEquals(SelectedCommit, selected) && result is { Code: "ok", Data: { } data } && !token.IsCancellationRequested)
            Details = data.Deserialize<GitHistoryOperations.CommitDetails>(WireJson.Options);
    }

    [RelayCommand]
    private Task CloseAsync(CancellationToken token) => RequestAsync(_git.CloseHistory, new GitHistoryOperations.CloseHistoryInput(ViewKey), token);

    private async Task<OperationResult?> RequestAsync<T>(Feature<T> feature, T input, CancellationToken token) where T : class
    {
        Dispatcher.UIThread.VerifyAccess();
        var request = new ViewCommandRequested(Guid.NewGuid(), feature.Id, _workbench.Address, ViewKey, input, token);
        CommandRequested?.Invoke(this, request);
        if (request.Completion is { } completion)
            await completion.ConfigureAwait(true);
        return request.Result;
    }
}
