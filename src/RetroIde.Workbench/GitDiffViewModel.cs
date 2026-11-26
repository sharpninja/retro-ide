using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

/// <summary>Holds a worktree-scoped, read-only patch in a tab owned by the document controller.</summary>
internal sealed partial class GitDiffViewModel(
    WorkbenchViewModel workbench, Feature<GitFileOperations.CloseDiffInput> close, GitFileOperations.DiffResult diff) : ObservableObject, IView
{
    public Guid ViewKey { get; } = Guid.NewGuid();
    public Guid WorkspaceId => workbench.WorkspaceId;
    public event EventHandler<ViewCommandRequested>? CommandRequested;

    [ObservableProperty]
    public partial OperationResult? LastResult { get; private set; }

    internal WorkbenchLocalization Localization => workbench.Localization ?? WorkbenchLocalization.English;
    internal (string Worktree, string Path, string Layer) Identity => (diff.Repository.WorktreePath, diff.Path, diff.Layer);
    internal TextDocument Buffer { get; } = new(diff.Patch);
    internal string Title => Describe(Path.GetFileName(diff.Path));

    public void Receive(OperationResult result)
    {
        Dispatcher.UIThread.VerifyAccess();
        LastResult = result;
    }

    internal GitFileOperations.DiffTab Capture() => new(ViewKey, WorkspaceId, diff.Repository, diff.Path, diff.Layer);

    internal void DisplayPatch(string patch)
    {
        if (!string.Equals(Buffer.Text, patch, StringComparison.Ordinal))
            Buffer.Text = patch;
    }

    internal string Describe(string path) => WorkbenchText.GitDiffName(Localization, path, diff.Layer);

    [RelayCommand]
    private async Task CloseAsync(CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        var request = new ViewCommandRequested(Guid.NewGuid(), close.Id, workbench.Address, ViewKey,
            new GitFileOperations.CloseDiffInput(ViewKey), cancellationToken);
        CommandRequested?.Invoke(this, request);
        if (request.Completion is { } completion)
            await completion.ConfigureAwait(true);
    }
}
