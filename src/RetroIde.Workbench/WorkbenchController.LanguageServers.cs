using Avalonia.Threading;
using RetroIde.Application;
using RetroIde.Application.LanguageServers;
using RetroIde.Application.Workspaces;

namespace RetroIde.Workbench;

public sealed partial class WorkbenchController
{
    internal LanguageServerWorkspace LanguageServers { get; }

    internal Task DrainLanguageServersAsync() => LanguageServers.DisposeAsync().AsTask();

    internal void ReportLanguageFailure(Exception error, string action) => RuntimeFailures.Report(_logger, error, action);

    private LanguageServersViewModel? _languageServersView;
    private ProblemsViewModel? _problemsView;
    private int _languageUpdatePending;

    internal void RegisterLanguageViews(WorkbenchViewModel workbench, LanguageServersViewModel servers, ProblemsViewModel problems)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_languageServersView is not null || _problemsView is not null || Resolve(workbench.ViewKey)?.View != workbench ||
            servers.WorkspaceId != workbench.WorkspaceId || problems.WorkspaceId != workbench.WorkspaceId)
            throw new InvalidOperationException("Language tools must register once in their live workbench.");

        Attach(servers);
        Attach(problems);
        _languageServersView = servers;
        _problemsView = problems;
        _localization.Changed += OnLanguagePaneLocaleChanged;
        RefreshLanguagePanes();

        void Attach(LanguageToolViewModel view)
        {
            if (!AddOrUpdateView(view))
                throw new InvalidOperationException("Could not register a language tool.");
            view.ParentViewKey = workbench.ViewKey;
            view.CommandRequested += OnCommandRequested;
        }
    }

    private void UnregisterLanguageViews(WorkbenchViewModel workbench)
    {
        if (_languageServersView?.ParentViewKey == workbench.ViewKey)
        {
            Detach(_languageServersView);
            _languageServersView = null;
            _localization.Changed -= OnLanguagePaneLocaleChanged;
        }
        if (_problemsView?.ParentViewKey == workbench.ViewKey)
        {
            Detach(_problemsView);
            _problemsView = null;
        }

        void Detach(LanguageToolViewModel view)
        {
            view.CommandRequested -= OnCommandRequested;
            view.ParentViewKey = null;
            RemoveView(view);
        }
    }

    private void QueueLanguageStateUpdate()
    {
        if (Interlocked.Exchange(ref _languageUpdatePending, 1) != 0)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _languageUpdatePending, 0);
            if (_disposed)
                return;
            try { RefreshLanguagePanes(); }
            catch (Exception error) when (RuntimeFailures.CanRecover(error))
            {
                ReportLanguageFailure(error, "Refresh language tool views");
            }
        }, DispatcherPriority.Background);
    }

    private void RefreshLanguagePanes()
    {
        _documents?.RefreshLanguageCapabilities();
        if (_languageServersView is null && _problemsView is null)
            return;
        var roots = _workspaceExplorer.Roots.ToArray();
        _languageServersView?.Update(roots, LanguageServers.List());
        _languageServersView?.UpdateServerEdits(LanguageServers.ServerEditPreviews());
        _problemsView?.Update(LanguageServers.Problems(), roots);
    }

    private void OnLanguagePaneLocaleChanged(object? sender, EventArgs args)
    {
        _languageServersView?.RefreshLabels();
        _problemsView?.RefreshLabels();
    }

    private void RouteLanguageTool(LanguageToolViewModel view, Registration registration, ViewCommandRequested request)
    {
        var language = _registry.Language;
        _ = request.Payload switch
        {
            FocusToolInput { ToolId: LanguageServersViewModel.PaneId } focus when view == _languageServersView =>
                Route(_registry.FocusTool, focus),
            FocusToolInput { ToolId: ProblemsViewModel.PaneId } focus when view == _problemsView =>
                Route(_registry.FocusTool, focus),
            LanguageServerFeatures.ConfigureInput configure when view == _languageServersView => Route(language.Configure, configure, useLiveWorkbench: true),
            LanguageServerFeatures.RootInput root when view == _languageServersView => Route(language.Restart, root, useLiveWorkbench: true) ||
                Route(language.Stop, root, useLiveWorkbench: true) || Route(language.Remove, root, useLiveWorkbench: true) || Route(language.Output, root),
            GetContextInput list when view == _languageServersView => Route(language.List, list),
            LanguageServerFeatures.DecideServerEditInput edit when view == _languageServersView =>
                Route(language.ApplyServerEdit, edit, useLiveWorkbench: true) || Route(language.DeclineServerEdit, edit, useLiveWorkbench: true),
            LanguageServerFeatures.OpenLocationInput open when view == _problemsView => Route(language.OpenLocation, open),
            _ => false,
        };

        bool Route<T>(Feature<T> feature, T input, bool useLiveWorkbench = false) where T : class
        {
            if (!string.Equals(feature.Id, request.CommandId, StringComparison.Ordinal))
                return false;

            // A lifecycle command captures its root and profile explicitly, independent of the next focused pane.
            request.Completion = DispatchToViewAsync(view, registration, feature, input, request,
                refreshOrigin: useLiveWorkbench ? registration : null);
            return true;
        }
    }

    private sealed partial record Registration
    {
        public LanguageServerWorkspace LanguageServers => Controller.LanguageServers;

        public LanguageDocument? ReadLanguageDocument(DocumentContext context) =>
            Controller._documents?.ReadLanguageDocument(context);

        public FeatureOutcome ApplyLanguageEdits(LanguageBufferEdit[] documents) =>
            Controller._documents?.ApplyLanguageEdits(documents) ?? new("view_closed", "The source editor is unavailable.");

        public void ReportLanguageFailure(Exception error, string action) =>
            Controller.ReportLanguageFailure(error, action);
    }
}
