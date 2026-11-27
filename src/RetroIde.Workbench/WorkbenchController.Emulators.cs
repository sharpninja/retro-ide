using Avalonia.Threading;
using RetroIde.Application;
using RetroIde.Application.Emulators;
using RetroIde.Application.Workspaces;

namespace RetroIde.Workbench;

public sealed partial class WorkbenchController
{
    private EmulatorsViewModel? _emulatorsView;

    internal void RegisterEmulators(WorkbenchViewModel workbench, EmulatorsViewModel view)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_emulatorsView is not null || Resolve(workbench.ViewKey)?.View != workbench || view.WorkspaceId != _workspaceId)
            throw new InvalidOperationException("Emulators must register once in its live workbench.");
        if (!AddOrUpdateView(view))
            throw new InvalidOperationException("Could not register the Emulators pane.");
        _emulatorsView = view;
        view.ParentViewKey = workbench.ViewKey;
        view.CommandRequested += OnCommandRequested;
    }

    private void UnregisterEmulators(WorkbenchViewModel workbench)
    {
        if (_emulatorsView is not { } view || view.ParentViewKey != workbench.ViewKey)
            return;
        _emulatorsView = null;
        view.CommandRequested -= OnCommandRequested;
        view.ParentViewKey = null;
        RemoveView(view);
    }

    private void RouteEmulators(EmulatorsViewModel view, Registration registration, ViewCommandRequested request)
    {
        var tools = _registry.Emulators;
        _ = request.Payload switch
        {
            FocusToolInput { ToolId: EmulatorsViewModel.PaneId } focus => Route(_registry.FocusTool, focus),
            GetContextInput read => Route(tools.List, read) || Route(tools.Inventory, read),
            ExplorerFeatures.ReadRootsInput roots => Route(_registry.Explorer.ReadRoots, roots),
            EmulatorFeatures.ConfigureInput configure => Route(tools.Configure, configure),
            EmulatorFeatures.BucketInput bucket => Route(tools.Refresh, bucket) || Route(tools.Remove, bucket),
            EmulatorFeatures.BundleInput bundle => Route(tools.Install, bundle) || Route(tools.RemoveBundle, bundle),
            EmulatorFeatures.UpdateInput update => Route(tools.Update, update),
            EmulatorFeatures.ProjectInput project => Route(tools.Selection, project),
            EmulatorFeatures.SelectionInput pin => Route(tools.Select, pin) || Route(tools.Rollback, pin),
            _ => false,
        };

        bool Route<T>(Feature<T> feature, T input) where T : class
        {
            if (!string.Equals(feature.Id, request.CommandId, StringComparison.Ordinal))
                return false;
            request.Completion = DispatchToViewAsync(view, registration, feature, input, request);
            return true;
        }
    }
}
