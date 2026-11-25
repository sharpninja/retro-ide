using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GPS.SimpleMVC.Views;
using RetroIde.Application;

namespace RetroIde.Workbench;

/// <summary>A directly registered SimpleMVC view with typed workbench command events.</summary>
/// <remarks>TEST-RETRO-007#ac-test-simplemvc-views; TEST-RETRO-007#ac-test-view-result-routing</remarks>
public interface IView : ISimpleView
{
    /// <summary>Raised when the view captures a new operation.</summary>
    event EventHandler<ViewCommandRequested>? CommandRequested;

    /// <summary>Receives a correlated completion on the UI thread.</summary>
    void Receive(OperationResult result);
}

/// <summary>Captures a typed focus request and its requesting view.</summary>
/// <remarks>TEST-RETRO-007#ac-test-view-result-routing</remarks>
public sealed class ViewCommandRequested(Guid requestId, string commandId, WorkbenchAddress context, FocusToolInput input,
    CancellationToken cancellationToken) : EventArgs
{
    /// <summary>Gets the operation correlation ID.</summary>
    public Guid RequestId { get; } = requestId;

    /// <summary>Gets the registered feature ID.</summary>
    public string CommandId { get; } = commandId;

    /// <summary>Gets the originating registration and captured revision.</summary>
    public WorkbenchAddress Context { get; } = context;

    /// <summary>Gets the selected tool argument.</summary>
    public FocusToolInput Input { get; } = input;

    /// <summary>Gets cancellation from the shared Toolkit command.</summary>
    public CancellationToken CancellationToken { get; } = cancellationToken;

    internal Task? Completion { get; set; }
}

/// <summary>Projects workbench state and owns the Toolkit command for this activation scope.</summary>
/// <remarks>TEST-RETRO-007#ac-test-toolkit-bindings; TEST-RETRO-015#ac-test-mcp-viewmodel-context</remarks>
public sealed partial class WorkbenchViewModel : ObservableObject, IView
{
    private Func<string, bool>? _canFocus;
    private string? _commandId;
    private string? _activeToolId;
    private long _workbenchRevision;

    /// <summary>Creates a ViewModel with a distinct registration identity.</summary>
    public WorkbenchViewModel(Guid workspaceId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(workspaceId, Guid.Empty);
        WorkspaceId = workspaceId;
    }

    /// <summary>Gets the identity of this live registration.</summary>
    [McpContext]
    public Guid ViewKey { get; } = Guid.NewGuid();

    /// <summary>Gets the current workspace's identity.</summary>
    [McpContext]
    public Guid WorkspaceId { get; }

    /// <summary>Gets the currently focused tool pane, if one has been focused.</summary>
    [McpContext]
    public string? ActiveToolId => _activeToolId;

    /// <summary>Gets the revision of the workbench selection state.</summary>
    [McpContext]
    public long WorkbenchRevision => _workbenchRevision;

    /// <summary>Gets or sets the target selected in the focus command bar.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FocusCommand))]
    public partial string? FocusTarget { get; set; }

    /// <summary>Gets the last correlated UI result.</summary>
    [ObservableProperty]
    public partial OperationResult? LastResult { get; private set; }

    /// <summary>Gets the current typed registration address.</summary>
    public WorkbenchAddress Address => new(ViewKey, WorkspaceId, ActiveToolId, WorkbenchRevision);

    /// <inheritdoc />
    public event EventHandler<ViewCommandRequested>? CommandRequested;

    /// <inheritdoc />
    public void Receive(OperationResult result)
    {
        Dispatcher.UIThread.VerifyAccess();
        LastResult = result;
    }

    internal bool RegisteredOnce { get; set; }

    internal void Update(string? activeTool, long revision)
    {
        if (revision < _workbenchRevision)
            return;

        var revisionChanged = _workbenchRevision != revision;
        var toolChanged = !string.Equals(_activeToolId, activeTool, StringComparison.Ordinal);
        if (revisionChanged)
            OnPropertyChanging(nameof(WorkbenchRevision));
        if (toolChanged)
            OnPropertyChanging(nameof(ActiveToolId));

        // A changing observer can synchronously issue a newer Dock selection.
        if (revision < _workbenchRevision)
            return;

        _workbenchRevision = revision;
        _activeToolId = activeTool;

        if (revisionChanged)
            OnPropertyChanged(nameof(WorkbenchRevision));
        if (toolChanged)
            OnPropertyChanged(nameof(ActiveToolId));
        FocusCommand.NotifyCanExecuteChanged();
    }

    internal void SetFocusRoute(string? commandId, Func<string, bool>? canFocus)
    {
        _commandId = commandId;
        _canFocus = canFocus;
        FocusCommand.NotifyCanExecuteChanged();
    }

    private bool CanFocus() => FocusTarget is { } target && _canFocus?.Invoke(target) == true;

    [RelayCommand(CanExecute = nameof(CanFocus), IncludeCancelCommand = true)]
    private async Task FocusAsync(CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!CanFocus() || FocusTarget is not { } target || _commandId is null)
            return;

        var request = new ViewCommandRequested(Guid.NewGuid(), _commandId, Address, new FocusToolInput(target), cancellationToken);
        CommandRequested?.Invoke(this, request);
        if (request.Completion is { } completion)
            await completion.ConfigureAwait(true);
        else
            Receive(OperationResult.Failure(request.RequestId, "view_closed", "The workbench registration is no longer live."));
    }
}
