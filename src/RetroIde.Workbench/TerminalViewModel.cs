using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Application.Terminals;

namespace RetroIde.Workbench;

internal sealed partial class TerminalViewModel(WorkbenchViewModel workbench, TerminalFeatures features, RepositoryTerminal terminal)
    : ObservableObject, IView
{
    public Guid ViewKey => terminal.Id;
    public Guid WorkspaceId => workbench.WorkspaceId;
    public event EventHandler<ViewCommandRequested>? CommandRequested;

    [ObservableProperty]
    public partial OperationResult? LastResult { get; private set; }

    internal RepositoryTerminal Terminal => terminal;
    internal string Scope => $"git-terminal-{ViewKey:N}";
    internal WorkbenchLocalization Localization => workbench.Localization ?? WorkbenchLocalization.English;
    internal string Title => WorkbenchText.GitTerminalName(Localization, Path.GetFileName(terminal.Repository.WorktreePath));

    public void Receive(OperationResult result)
    {
        Dispatcher.UIThread.VerifyAccess();
        LastResult = result;
    }

    internal TerminalFeatures.Session Capture()
    {
        var exit = terminal.ExitResult;
        return new(ViewKey, WorkspaceId, terminal.Repository.WorktreePath, terminal.ProcessId, terminal.IsRunning, exit?.Code, exit?.Signal);
    }

    [RelayCommand]
    private Task CloseAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        var request = new ViewCommandRequested(Guid.NewGuid(), features.Close.Id, workbench.Address, ViewKey,
            new TerminalFeatures.SessionInput(ViewKey), token);
        CommandRequested?.Invoke(this, request);
        return request.Completion ?? Task.CompletedTask;
    }
}
