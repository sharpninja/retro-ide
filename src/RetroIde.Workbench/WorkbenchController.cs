using Avalonia.Threading;
using GPS.SimpleMVC.Controllers;
using RetroIde.Application;
using RetroIde.Application.Git;
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
    private SourceControlViewModel? _sourceControlView;
    private WorkspaceExplorer.Address? _explorerSelection;
    private WorkspaceWatchSet? _explorerWatch;
    private DocumentController? _documents;
    private readonly WorkbenchUnitOfWork _unitOfWork;
    private readonly Dictionary<Guid, Registration> _views = [];
    private readonly CancellationTokenSource _shutdown = new();
    private Task _retiredGitReads = Task.CompletedTask;
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
        _retiredGitReads = CombineDrains(_retiredGitReads, registration.StopGitMonitor(closing: true));
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
        if (_sourceControlView is { } git && git.ParentViewKey == view.ViewKey)
        {
            _sourceControlView = null;
            git.CommandRequested -= OnCommandRequested;
            git.ParentViewKey = null;
            git.SetAvailability(null, available: false);
            RemoveView(git);
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

    internal void RegisterSourceControl(SourceControlViewModel view, WorkbenchViewModel workbench)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sourceControlView is not null || Resolve(workbench.ViewKey)?.View != workbench || view.WorkspaceId != _workspaceId)
            throw new InvalidOperationException("Source Control must register once in its live workbench.");

        if (!AddOrUpdateView(view))
            throw new InvalidOperationException("Could not register the Source Control view.");

        _sourceControlView = view;
        view.ParentViewKey = workbench.ViewKey;
        view.CommandRequested += OnCommandRequested;
        RefreshSourceControl();
    }

    internal async Task RestoreGitAsync(WorkbenchViewModel view)
    {
        if (Resolve(view.ViewKey) is not { } origin || _workspaceExplorer.Snapshot().GitSelection is not { } saved)
            return;

        var token = _shutdown.Token;
        try
        {
            var restored = await GitFeatures.RestoreSelectionAsync(_workspaceExplorer, saved, token).ConfigureAwait(true);
            if (restored is not null && !token.IsCancellationRequested &&
                ReferenceEquals(Resolve(view.ViewKey), origin) && origin.SelectedGit is null)
                origin.SelectGit(restored, saved.Address);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    internal Task DrainGitRefreshAsync(WorkbenchViewModel view) =>
        CombineDrains(_retiredGitReads, Resolve(view.ViewKey)?.StopGitMonitor(closing: true) ?? Task.CompletedTask);

    private static Task CombineDrains(Task first, Task second) =>
        first.IsCompletedSuccessfully ? second : second.IsCompletedSuccessfully ? first : Task.WhenAll(first, second);

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
        _explorerSelection = state.Selection;
        _explorerView?.ReplaceState(state);
        _explorerWatch?.UpdateRoots(state.Roots);
        RefreshSourceControl();
    }

    private void RefreshSourceControl() =>
        _sourceControlView?.SetAvailability(_explorerSelection, !_interaction.IsBlocked(SourceControlViewModel.PaneId));

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
        RefreshSourceControl();
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
        else if (_sourceControlView is { } git && ReferenceEquals(sender, git) &&
            request.OriginViewKey == git.ViewKey && git.ParentViewKey == registration.View.ViewKey &&
            ReferenceEquals(GetView<IView>(git.ViewKey), git))
        {
            request.Completion = request.Payload switch
            {
                FocusToolInput { ToolId: SourceControlViewModel.PaneId } focus when string.Equals(request.CommandId, _registry.FocusTool.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(git, registration, _registry.FocusTool, focus, request),
                GitFeatures.LocateInput locate when string.Equals(request.CommandId, _registry.Git.Select.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(git, registration, _registry.Git.Select, locate, request),
                GitFeatures.StatusInput status when string.Equals(request.CommandId, _registry.Git.Status.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(git, registration, _registry.Git.Status, status, request),
                GitOperationRecovery.ReadInput op when string.Equals(request.CommandId, _registry.Git.Operation.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(git, registration, _registry.Git.Operation, op, request),
                GitOperationRecovery.RecoverInput resume when string.Equals(request.CommandId, _registry.Git.ContinueOperation.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(git, registration, _registry.Git.ContinueOperation, resume, request),
                GitOperationRecovery.RecoverInput abort when string.Equals(request.CommandId, _registry.Git.AbortOperation.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(git, registration, _registry.Git.AbortOperation, abort, request),
                GitOperationRecovery.RecoverInput skip when string.Equals(request.CommandId, _registry.Git.SkipOperation.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(git, registration, _registry.Git.SkipOperation, skip, request),
                GitFeatures.InitializeInput initialize when string.Equals(request.CommandId, _registry.Git.Initialize.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(git, registration, _registry.Git.Initialize, initialize, request, refreshOrigin: registration),
                GitFeatures.CloneInput clone when string.Equals(request.CommandId, _registry.Git.Clone.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(git, registration, _registry.Git.Clone, clone, request, refreshOrigin: registration),
                GitFileOperations.DiffInput diff when string.Equals(request.CommandId, _registry.Git.OpenDiff.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(git, registration, _registry.Git.OpenDiff, diff, request),
                GitFileOperations.FileInput stage when string.Equals(request.CommandId, _registry.Git.Stage.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(git, registration, _registry.Git.Stage, stage, request),
                GitFileOperations.FileInput unstage when string.Equals(request.CommandId, _registry.Git.Unstage.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(git, registration, _registry.Git.Unstage, unstage, request),
                GitCommitOperations.StateInput state when string.Equals(request.CommandId, _registry.Git.CommitState.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(git, registration, _registry.Git.CommitState, state, request),
                GitCommitOperations.CommitInput commit when string.Equals(request.CommandId, _registry.Git.Commit.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(git, registration, _registry.Git.Commit, commit, request),
                GitCommitOperations.CommitInput amend when string.Equals(request.CommandId, _registry.Git.Amend.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(git, registration, _registry.Git.Amend, amend, request),
                GitHistoryOperations.HistoryInput history when string.Equals(request.CommandId, _registry.Git.OpenHistory.Id, StringComparison.Ordinal) =>
                    DispatchToViewAsync(git, registration, _registry.Git.OpenHistory, history, request),
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

    /// <summary>Represents the live workbench registration and its own native Git selection.</summary>
    /// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos; TEST-RETRO-015#ac-test-mcp-same-instance. Publish selection revisions through the existing workbench layout.</remarks>
    private sealed record Registration(WorkbenchViewModel View, ActorGrant Grant, WorkbenchController Controller) : IWorkbenchRegistration,
        IExplorerRegistration, ILocalizationRegistration, IGitRegistration
    {
        private GitStatusMonitor? _gitMonitor;
        private Task _drainingMonitors = Task.CompletedTask;
        private bool _gitMonitoringClosed;
        private long _statusReads;
        private long _displayedRead;

        public WorkbenchAddress Address => View.Address;
        public object ContextSource => View;
        public bool TryFocus(string toolId) => Controller.CanFocus(toolId) && Controller._layout.Focus(toolId);
        public WorkspaceExplorer Explorer => Controller._workspaceExplorer;

        /// <inheritdoc />
        public GitSelection? SelectedGit { get; private set; }

        /// <inheritdoc />
        public void SelectGit(GitSelection selection, WorkspaceExplorer.Address address)
        {
            SelectedGit = selection;
            Controller._layout.AdvanceWorkspaceRevision();
            if (Controller._sourceControlView is { } pane && pane.ParentViewKey == View.ViewKey)
            {
                Explorer.RememberGitSelection(new GitWorkspaceLocation(address, selection.Repository));
                pane.SetScope(selection.Repository);
                Controller._explorerView?.SetGitBadges(null);
                _ = StopGitMonitor();
                if (!_gitMonitoringClosed)
                {
                    _gitMonitor = new GitStatusMonitor(selection, token =>
                        Controller.DispatchAsync(Controller._registry.Git.Status, new GitFeatures.StatusInput { DisplayOnly = true },
                            null, Grant, Guid.NewGuid(), token));
                }
            }
        }

        internal Task StopGitMonitor(bool closing = false)
        {
            _gitMonitoringClosed |= closing;
            if (_gitMonitor is { } monitor)
            {
                _gitMonitor = null;
                monitor.Dispose();
                _drainingMonitors = CombineDrains(_drainingMonitors, monitor.Completion);
            }
            return _drainingMonitors;
        }

        /// <inheritdoc />
        public long BeginGitStatusRead() => ++_statusReads;

        /// <inheritdoc />
        public void DisplayGitStatus(GitStatus? status, long readOrder)
        {
            if (readOrder < _displayedRead)
                return;
            _displayedRead = readOrder;
            if (Controller._sourceControlView is { } pane && pane.ParentViewKey == View.ViewKey && SelectedGit is { } scope)
            {
                pane.DisplayStatus(status);
                Controller._explorerView?.SetGitBadges(status is null ? null : new GitExplorerBadges(scope, status));
            }
        }

        /// <inheritdoc />
        public GitProvisionedWorkspace OpenGitWorkspace(GitWorktree repository)
        {
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var root = Explorer.Roots.FirstOrDefault(item => comparer.Equals(item.Path, repository.WorktreePath))
                ?? Explorer.AddRoot(repository.WorktreePath, WorkspaceExplorer.RootKind.Folder);
            var address = new WorkspaceExplorer.Address(root.Id, "");
            Explorer.Select(address);
            SelectGit(new GitSelection(repository, root.Path), address);
            Controller.PublishExplorerState();
            return new GitProvisionedWorkspace(root, repository);
        }

        public GitFileOperations.DiffTab? ShowDiff(GitFileOperations.DiffResult diff) => Controller._documents?.ShowDiff(diff);

        public bool CloseDiff(Guid viewKey) => Controller._documents?.CloseDiff(viewKey) == true;

        public GitHistoryOperations.HistoryTab? ShowHistory(GitHistoryOperations.HistoryPage page, GitHistoryOperations.RefState references) =>
            Controller._documents?.ShowHistory(page, references);

        public bool CloseHistory(Guid viewKey) => Controller._documents?.CloseHistory(viewKey) == true;

        public IGitBranchSwitch? BeginBranchSwitch(GitWorktree repository, string[] paths) =>
            Controller._documents?.BeginBranchSwitch(repository, paths);

        public Task<GitIntegrationLease> BeginIntegrationAsync(GitSelection selection, string[]? paths, CancellationToken token) =>
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(Controller.Resolve(View.ViewKey), this) || !ReferenceEquals(SelectedGit, selection))
                    return new GitIntegrationLease(null, "git_selection_changed");
                return new GitIntegrationLease(Controller._documents?.BeginBranchSwitch(selection.Repository, paths));
            }).GetTask();

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
