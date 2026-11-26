using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

/// <summary>Holds a worktree-scoped, read-only patch in a tab owned by the document controller.</summary>
internal sealed partial class GitDiffViewModel : ObservableObject, IView
{
    private readonly WorkbenchViewModel _workbench;
    private readonly GitFeatures _git;
    private readonly InteractionBlocks _interaction;
    private readonly GitWorktree _repository;
    private string? _preimage;
    private int[] _selected = [];
    private bool _applying;

    internal GitDiffViewModel(WorkbenchViewModel workbench, GitFeatures git, InteractionBlocks interaction, GitFileOperations.DiffResult diff)
    {
        _workbench = workbench;
        _git = git;
        _interaction = interaction;
        _repository = diff.Repository;
        Identity = (diff.Repository.WorktreePath, diff.Path, diff.Layer);
        Buffer = new TextDocument(diff.Patch);
        SetReview(diff);
    }

    public Guid ViewKey { get; } = Guid.NewGuid();
    public Guid WorkspaceId => _workbench.WorkspaceId;
    public event EventHandler<ViewCommandRequested>? CommandRequested;

    [ObservableProperty]
    public partial OperationResult? LastResult { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHunks))]
    public partial GitHunkPatch.Hunk[] Hunks { get; private set; }

    public bool HasHunks => Hunks.Length > 0;

    internal WorkbenchLocalization Localization => _workbench.Localization ?? WorkbenchLocalization.English;
    internal (string Worktree, string Path, string Layer) Identity { get; }
    internal string Scope => $"git-diff-{ViewKey:N}";
    internal TextDocument Buffer { get; }
    internal string Title => Describe(Path.GetFileName(Identity.Path));
    internal bool IsStaged => string.Equals(Identity.Layer, "staged", StringComparison.Ordinal);

    public void Receive(OperationResult result)
    {
        Dispatcher.UIThread.VerifyAccess();
        LastResult = result;
    }

    internal GitFileOperations.DiffTab Capture() => new(ViewKey, WorkspaceId, _repository, Identity.Path, Identity.Layer, _preimage, Hunks);

    internal void DisplayPatch(GitFileOperations.DiffResult result)
    {
        if (!string.Equals(Buffer.Text, result.Patch, StringComparison.Ordinal))
            Buffer.Text = result.Patch;
        SetReview(result);
    }

    private void SetReview(GitFileOperations.DiffResult result)
    {
        if (!string.Equals(_preimage, result.Preimage, StringComparison.Ordinal))
            _selected = [];
        _preimage = result.Preimage;
        Hunks = result.Hunks;
        NotifyActions();
    }

    internal void SetSelectedHunks(IEnumerable<GitHunkPatch.Hunk> selected)
    {
        _selected = [.. selected.Select(hunk => hunk.Id)];
        NotifyActions();
    }

    internal string Describe(string path) => WorkbenchText.GitDiffName(Localization, path, Identity.Layer);

    private bool CanRefresh() => !_applying;
    private bool CanApplyHunks() => !_applying && _preimage is not null && _selected.Length > 0;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync(CancellationToken token) =>
        RequestAsync(_git.OpenDiff, new GitFileOperations.DiffInput(Identity.Worktree, Identity.Path, Identity.Layer), token);

    [RelayCommand(CanExecute = nameof(CanApplyHunks))]
    private async Task ApplyHunksAsync(CancellationToken token)
    {
        if (!CanApplyHunks())
            return;
        var input = new GitFileOperations.HunksInput(Identity.Worktree, Identity.Path, _preimage!, _selected);
        _applying = true;
        NotifyActions();
        _interaction.Begin(Scope);
        try
        {
            var result = await RequestAsync(IsStaged ? _git.UnstageHunks : _git.StageHunks, input, token).ConfigureAwait(true);
            if (result is { Code: "ok" })
                await RefreshAsync(token).ConfigureAwait(true);
        }
        finally
        {
            _applying = false;
            _interaction.End(Scope);
            NotifyActions();
        }
    }

    private void NotifyActions()
    {
        ApplyHunksCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private Task CloseAsync(CancellationToken token) => RequestAsync(_git.CloseDiff, new GitFileOperations.CloseDiffInput(ViewKey), token);

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
