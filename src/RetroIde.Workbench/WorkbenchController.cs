using Avalonia.Threading;
using GPS.SimpleMVC.Controllers;
using RetroIde.Application;
using RetroIde.Application.Workspaces;
using RetroIde.Application.Localization;

namespace RetroIde.Workbench;

/// <summary>Owns workbench view registrations and the shared typed dispatch entry point.</summary>
/// <remarks>TEST-RETRO-007#ac-test-simplemvc-views; TEST-RETRO-007#ac-test-view-result-routing</remarks>
public sealed class WorkbenchController : SimpleControllerBase, IWorkbenchDispatcher
{
    private readonly Guid _workspaceId;
    private readonly DockLayout _layout;
    private readonly InteractionBlocks _interaction;
    private readonly FeatureRegistry _registry;
    private readonly WorkspaceExplorer _workspaceExplorer;
    private readonly WorkbenchLocalization _localization;
    private ExplorerViewModel? _explorerView;
    private WorkspaceWatchSet? _explorerWatch;
    private DocumentController? _documents;
    private readonly WorkbenchUnitOfWork _unitOfWork;
    private readonly Dictionary<Guid, Registration> _views = [];
    private readonly CancellationTokenSource _shutdown = new();
    private bool _disposed;

    internal WorkbenchController(WorkspaceExplorer explorer, DockLayout layout, InteractionBlocks interaction, FeatureRegistry registry, WorkbenchLocalization localization)
    {
        _workspaceExplorer = explorer;
        _localization = localization;
        _workspaceId = explorer.WorkspaceId;
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
        view.SetLanguageRoute(_registry.Localization.SetLanguage.Id);
        view.Update(_layout.ActiveToolId, _layout.Revision);
    }

    /// <summary>Detaches command events and invalidates the registration's authority.</summary>
    public void Unregister(WorkbenchViewModel view)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_views.Remove(view.ViewKey, out var registration))
            return;

        registration.Grant.Revoke();
        if (_explorerView is { } explorer && explorer.ParentViewKey == view.ViewKey)
        {
            _explorerView = null;
            _documents?.DetachViews();
            _documents = null;
            _explorerWatch?.Dispose();
            _explorerWatch = null;
            explorer.CommandRequested -= OnCommandRequested;
            explorer.ParentViewKey = null;
            explorer.CancelPendingReads();
            explorer.SetRootEligibility(false);
            RemoveView(explorer);
        }
        view.CommandRequested -= OnCommandRequested;
        RemoveView(view);
        view.SetFocusRoute(null, null);
        view.SetLanguageRoute(null);
    }

    internal void RegisterExplorer(ExplorerViewModel view, WorkbenchViewModel workbench, DocumentController documents)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_explorerView is not null || Resolve(workbench.ViewKey)?.View != workbench || view.WorkspaceId != _workspaceId)
            throw new InvalidOperationException("The Explorer must register once in its live workbench.");

        if (!AddOrUpdateView(view))
            throw new InvalidOperationException("Could not register the Project Explorer view.");

        _explorerView = view;
        _documents = documents;
        view.ParentViewKey = workbench.ViewKey;
        view.CommandRequested += OnCommandRequested;
        view.SetRootEligibility(CanChangeExplorer());
        _explorerWatch = new WorkspaceWatchSet(changes => Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(_explorerView, view))
                view.RefreshBranches(changes);
        }, DispatcherPriority.Background));
        PublishExplorerState();
    }

    /// <inheritdoc />
    public Task<OperationResult> DispatchAsync<TInput>(Feature<TInput> feature, TInput input, WorkbenchAddress? context,
        ActorGrant actor, Guid operationId, CancellationToken cancellationToken) where TInput : class =>
        DispatchAsync(feature, input, context, actor, operationId, cancellationToken, refreshOrigin: null);

    private async Task<OperationResult> DispatchAsync<TInput>(Feature<TInput> feature, TInput input, WorkbenchAddress? context,
        ActorGrant actor, Guid operationId, CancellationToken cancellationToken, Registration? refreshOrigin) where TInput : class
    {
        try
        {
            var dispatch = Dispatcher.UIThread.InvokeAsync<Task<OperationResult>>(() =>
            {
                DispatchCount++;
                return _unitOfWork.ExecuteAsync(feature, input, refreshOrigin?.Address ?? context, actor, operationId, cancellationToken);
            }, DispatcherPriority.Background, _shutdown.Token);
            return await dispatch.GetTask().Unwrap().ConfigureAwait(false);
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

    private bool CanChangeExplorer() => !_interaction.IsBlocked("explorer");

    private void PublishExplorerState()
    {
        var state = _workspaceExplorer.Snapshot();
        _explorerView?.ReplaceState(state);
        _explorerWatch?.UpdateRoots(state.Roots);
    }

    private Registration? Resolve(Guid viewKey)
    {
        return _views.TryGetValue(viewKey, out var registration) &&
            ReferenceEquals(registration.View, GetView<IView>(viewKey)) ? registration : null;
    }

    private void RefreshViews(object? sender, EventArgs args)
    {
        foreach (var registration in _views.Values.ToArray())
            registration.View.Update(_layout.ActiveToolId, _layout.Revision);
        _explorerView?.SetRootEligibility(CanChangeExplorer());
    }

    private void OnCommandRequested(object? sender, ViewCommandRequested request)
    {
        if (request.Completion is not null || Resolve(request.Context.ViewKey) is not { } registration)
            return;

        if (ReferenceEquals(sender, registration.View) && request.OriginViewKey == registration.View.ViewKey)
        {
            request.Completion = request.Payload switch
            {
                FocusToolInput focus when string.Equals(request.CommandId, _registry.FocusTool.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(registration.View, registration, _registry.FocusTool, focus, request),
                LocalizationFeatures.SetLanguageInput language when string.Equals(request.CommandId, _registry.Localization.SetLanguage.Id, StringComparison.Ordinal) =>
                    // A local language choice targets the workbench and survives popup or Dock focus churn.
                    DispatchToViewAsync(registration.View, registration, _registry.Localization.SetLanguage, language, request,
                        refreshOrigin: registration),
                _ => null,
            };
        }
        else if (_explorerView is { } explorer && ReferenceEquals(sender, explorer) &&
            request.OriginViewKey == explorer.ViewKey && explorer.ParentViewKey == registration.View.ViewKey &&
            ReferenceEquals(GetView<IView>(explorer.ViewKey), explorer))
        {
            request.Completion = request.Payload switch
            {
                ExplorerFeatures.OpenRootInput root when string.Equals(request.CommandId, _registry.Explorer.OpenRoot.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(explorer, registration, _registry.Explorer.OpenRoot, root, request),
                ExplorerFeatures.ReadChildrenInput directory when string.Equals(request.CommandId, _registry.Explorer.ReadChildren.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(explorer, registration, _registry.Explorer.ReadChildren, directory, request),
                ExplorerFeatures.SetExpansionInput expansion when string.Equals(request.CommandId, _registry.Explorer.SetExpansion.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(explorer, registration, _registry.Explorer.SetExpansion, expansion, request),
                ExplorerFeatures.SelectEntryInput selection when string.Equals(request.CommandId, _registry.Explorer.SelectEntry.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(explorer, registration, _registry.Explorer.SelectEntry, selection, request),
                ExplorerFeatures.OpenEntryInput source when string.Equals(request.CommandId, _registry.Explorer.OpenEntry.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(explorer, registration, _registry.Explorer.OpenEntry, source, request),
                _ => null,
            };
        }
    }

    private async Task DispatchToViewAsync<TInput>(IView origin, Registration authorization, Feature<TInput> feature,
        TInput input, ViewCommandRequested request, Registration? refreshOrigin = null) where TInput : class
    {
        var result = await DispatchAsync(feature, input, feature.IsMutation ? request.Context : null, authorization.Grant, request.RequestId,
            request.CancellationToken, refreshOrigin).ConfigureAwait(false);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ReferenceEquals(Resolve(authorization.View.ViewKey), authorization) &&
                ReferenceEquals(GetView<IView>(request.OriginViewKey), origin))
            {
                request.Result = result;
                origin.Receive(result);
            }
        }, DispatcherPriority.Background).GetTask().ConfigureAwait(false);
    }

    internal Task<OperationResult> DispatchDocumentAsync<TInput>(Feature<TInput> feature, TInput input, ViewCommandRequested request)
        where TInput : class
    {
        var registration = Resolve(request.Context.ViewKey);
        if (registration is null || _documents?.Owns(request.OriginViewKey, request.Context.ViewKey) != true)
            return Task.FromResult(OperationResult.Failure(request.RequestId, "view_closed", "The document registration is no longer live."));

        return DispatchAsync(feature, input, request.Context, registration.Grant, request.RequestId, request.CancellationToken);
    }

    private sealed record Registration(WorkbenchViewModel View, ActorGrant Grant, WorkbenchController Controller) : IWorkbenchRegistration, IExplorerRegistration, ILocalizationRegistration
    {
        public WorkbenchAddress Address => View.Address;
        public object ContextSource => View;
        public bool TryFocus(string toolId) => Controller.CanFocus(toolId) && Controller._layout.Focus(toolId);
        public WorkspaceExplorer Explorer => Controller._workspaceExplorer;
        public bool HasExplorer => Controller._explorerView?.ParentViewKey == View.ViewKey;
        public bool CanChangeExplorer => Controller.CanChangeExplorer();
        public bool HasOpenDocument(WorkspaceExplorer.Address address) => Controller._documents?.HasDocument(address) == true;

        public DocumentContext? OpenOrActivateDocument(WorkspaceExplorer.Address address, WorkspaceExplorer.SourceFile? source, SourcePosition? position) =>
            Controller._documents?.OpenOrActivate(address, source, position);

        public DocumentContext? ReadDocumentContext(Guid viewKey) => Controller._documents?.ReadContext(viewKey);
        public FeatureOutcome FindInDocument(DocumentFeatures.FindInput input) =>
            Controller._documents?.Find(input) ?? new("view_closed", "The document has closed.");
        public FeatureOutcome ReplaceInDocument(DocumentFeatures.ReplaceInput input) =>
            Controller._documents?.Replace(input) ?? new("view_closed", "The document has closed.");
        public FeatureOutcome CloseDocument(DocumentFeatures.CloseInput input) =>
            Controller._documents?.Close(input) ?? new("view_closed", "The document has closed.");

        public FeatureOutcome EditDocument(DocumentFeatures.EditInput input) =>
            Controller._documents?.Edit(input) ?? new("view_closed", "The document has closed.");
        public FeatureOutcome ApplyDocumentHistory(DocumentContext document, bool redo) =>
            Controller._documents?.ApplyHistory(document, redo) ?? new("view_closed", "The document has closed.");

        public DocumentSaveSnapshot? CaptureDocumentSave(DocumentContext context) =>
            Controller._documents?.CaptureSave(context);

        public DocumentContext? CommitDocumentSave(DocumentSaveSnapshot snapshot, PreparedDocumentSave staged) =>
            Controller._documents?.CommitSave(snapshot, staged);

        public LocalizationFeatures.LanguageState Language => Controller._localization.State;

        public FeatureOutcome SetLanguage(string locale)
        {
            if (Controller._interaction.IsBlocked(InteractionBlocks.WorkbenchScope))
                return new("workbench_busy", "The workbench is currently blocked.");
            var prior = Controller._localization.DisplayCulture;
            var outcome = Controller._localization.Select(locale);
            if (!Equals(prior, Controller._localization.DisplayCulture))
                Controller._layout.AdvanceWorkspaceRevision();
            return outcome;
        }

        public void PublishExplorer()
        {
            Controller._layout.AdvanceWorkspaceRevision();
            Controller.PublishExplorerState();
        }
    }
}
