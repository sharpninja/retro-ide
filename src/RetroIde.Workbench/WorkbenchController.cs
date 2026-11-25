using Avalonia.Threading;
using GPS.SimpleMVC.Controllers;
using RetroIde.Application;

namespace RetroIde.Workbench;

/// <summary>Owns workbench view registrations and the shared typed dispatch entry point.</summary>
/// <remarks>TEST-RETRO-007#ac-test-simplemvc-views; TEST-RETRO-007#ac-test-view-result-routing</remarks>
public sealed class WorkbenchController : SimpleControllerBase, IWorkbenchDispatcher
{
    private readonly Guid _workspaceId;
    private readonly DockLayout _layout;
    private readonly InteractionBlocks _interaction;
    private readonly FeatureRegistry _registry;
    private readonly WorkbenchUnitOfWork _unitOfWork;
    private readonly Dictionary<Guid, Registration> _views = [];
    private readonly CancellationTokenSource _shutdown = new();
    private bool _disposed;

    internal WorkbenchController(Guid workspaceId, DockLayout layout, InteractionBlocks interaction, FeatureRegistry registry)
    {
        _workspaceId = workspaceId;
        _registry = registry;
        _layout = layout;
        _interaction = interaction;
        _unitOfWork = new WorkbenchUnitOfWork(registry.Context, Resolve);
        Initialize();
        _layout.Changed += RefreshViews;
        _interaction.Changed += RefreshViews;
    }

    /// <inheritdoc />
    public override bool Initialize() => true;

    internal int DispatchCount { get; private set; }

    /// <summary>Registers the ViewModel directly with SimpleMVC and subscribes once.</summary>
    public void Register(WorkbenchViewModel view)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (view.WorkspaceId != _workspaceId)
            throw new ArgumentException("The view belongs to a different workspace.", nameof(view));
        if (Resolve(view.ViewKey)?.View == view)
            return;
        if (view.RegisteredOnce)
            throw new InvalidOperationException("A closed ViewModel cannot be registered again.");

        if (!AddOrUpdateView(view))
            throw new InvalidOperationException("Could not register the workbench view.");

        view.RegisteredOnce = true;
        var grant = new ActorGrant("workbench", view.WorkspaceId, view.ViewKey, canMutate: true);
        _views.Add(view.ViewKey, new Registration(view, grant, this));
        view.CommandRequested += OnCommandRequested;
        view.SetFocusRoute(_registry.FocusTool.Id, CanFocus);
        view.Update(_layout.ActiveToolId, _layout.Revision);
    }

    /// <summary>Detaches command events and invalidates the registration's authority.</summary>
    public void Unregister(WorkbenchViewModel view)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_views.Remove(view.ViewKey, out var registration))
            return;

        registration.Grant.Revoke();
        view.CommandRequested -= OnCommandRequested;
        RemoveView(view);
        view.SetFocusRoute(null, null);
    }

    /// <inheritdoc />
    public async Task<OperationResult> DispatchAsync<TInput>(Feature<TInput> feature, TInput input, WorkbenchAddress? context,
        ActorGrant actor, Guid operationId, CancellationToken cancellationToken) where TInput : class
    {
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                DispatchCount++;
                return _unitOfWork.Execute(feature, input, context, actor, operationId, cancellationToken);
            }, DispatcherPriority.Background, _shutdown.Token).GetTask().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException && _disposed)
        {
            return OperationResult.Failure(operationId, "view_closed", "The workbench has closed.");
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing || _disposed)
        {
            base.Dispose(disposing);
            return;
        }

        Dispatcher.UIThread.VerifyAccess();
        _disposed = true;
        _shutdown.Cancel();
        _shutdown.Dispose();

        _layout.Changed -= RefreshViews;
        _interaction.Changed -= RefreshViews;
        foreach (var registration in _views.Values.ToArray())
            Unregister(registration.View);
        base.Dispose(disposing);
    }

    private bool CanFocus(string toolId) => !_interaction.IsBlocked(toolId) && _layout.CanFocus(toolId);

    private Registration? Resolve(Guid viewKey)
    {
        return _views.TryGetValue(viewKey, out var registration) &&
            ReferenceEquals(registration.View, GetView<IView>(viewKey)) ? registration : null;
    }

    private void RefreshViews(object? sender, EventArgs args)
    {
        foreach (var registration in _views.Values.ToArray())
            registration.View.Update(_layout.ActiveToolId, _layout.Revision);
    }

    private void OnCommandRequested(object? sender, ViewCommandRequested request)
    {
        if (request.Completion is null && Resolve(request.Context.ViewKey) is { } registration &&
            ReferenceEquals(sender, registration.View) && string.Equals(request.CommandId, _registry.FocusTool.Id, StringComparison.Ordinal))
            request.Completion = DispatchToViewAsync(registration, request);
    }

    private async Task DispatchToViewAsync(Registration origin, ViewCommandRequested request)
    {
        var result = await DispatchAsync(_registry.FocusTool, request.Input, request.Context, origin.Grant,
            request.RequestId, request.CancellationToken).ConfigureAwait(false);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ReferenceEquals(Resolve(origin.View.ViewKey), origin))
                origin.View.Receive(result);
        }, DispatcherPriority.Background).GetTask().ConfigureAwait(false);
    }

    private sealed record Registration(WorkbenchViewModel View, ActorGrant Grant, WorkbenchController Controller) : IWorkbenchRegistration
    {
        public WorkbenchAddress Address => View.Address;
        public object ContextSource => View;
        public bool TryFocus(string toolId) => Controller.CanFocus(toolId) && Controller._layout.Focus(toolId);
    }
}
