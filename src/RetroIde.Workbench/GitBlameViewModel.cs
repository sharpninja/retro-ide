using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal sealed partial class GitBlameViewModel(WorkbenchViewModel workbench, GitFeatures git, GitBlameOperations.Blame initial)
    : ObservableObject, IView
{
    public Guid ViewKey { get; } = Guid.NewGuid();
    public Guid WorkspaceId => workbench.WorkspaceId;
    public event EventHandler<ViewCommandRequested>? CommandRequested;

    [ObservableProperty]
    public partial GitBlameOperations.Blame Blame { get; private set; } = initial;
    [ObservableProperty]
    public partial OperationResult? LastResult { get; private set; }

    internal string Scope => $"git-blame-{ViewKey:N}";
    internal WorkbenchLocalization Localization => workbench.Localization ?? WorkbenchLocalization.English;
    internal string Title => WorkbenchText.GitBlameName(Localization, Path.GetFileName(Blame.Source.Path));

    public void Receive(OperationResult result)
    {
        Dispatcher.UIThread.VerifyAccess();
        LastResult = result;
    }

    internal GitBlameOperations.Tab Capture() => new(ViewKey, WorkspaceId, Blame);

    internal void Display(GitBlameOperations.Blame blame) => Blame = blame;

    [RelayCommand]
    private Task RefreshAsync(CancellationToken token) => RequestAsync(git.OpenBlame, Blame.Source, token);

    [RelayCommand]
    private Task CloseAsync(CancellationToken token) => RequestAsync(git.CloseBlame, new GitBlameOperations.CloseInput(ViewKey), token);

    private Task RequestAsync<T>(Feature<T> feature, T input, CancellationToken token) where T : class
    {
        Dispatcher.UIThread.VerifyAccess();
        var request = new ViewCommandRequested(Guid.NewGuid(), feature.Id, workbench.Address, ViewKey, input, token);
        CommandRequested?.Invoke(this, request);
        return request.Completion ?? Task.CompletedTask;
    }
}
