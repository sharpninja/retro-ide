using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RetroIde.Application;
using RetroIde.Application.Workspaces;

namespace RetroIde.Workbench;

internal interface IWorkbenchWorkspaceContext
{
    Guid WorkspaceId { get; }
}

/// <summary>Composes one workbench and its live registration independently of emulator sessions.</summary>
/// <remarks>TEST-RETRO-001#verify-ac-behavior-desktop-2</remarks>
public sealed class WorkbenchSession : IDisposable, IAsyncDisposable
{
    /// <summary>Creates a workbench and applies a saved layout if it is compatible.</summary>
    public WorkbenchSession(Guid workspaceId, WorkbenchLayoutState? savedLayout = null)
        : this(new StandaloneWorkspace(workspaceId), savedLayout)
    {
    }

    internal WorkbenchSession(WorkspaceExplorerStore explorerStore, WorkbenchLayoutState? savedLayout,
        WorkbenchLocalization? localization = null)
        : this(explorerStore, savedLayout, localization, logs: null)
    {
    }

    internal WorkbenchSession(WorkspaceExplorerStore explorerStore, WorkbenchLayoutState? savedLayout,
        WorkbenchLocalization? localization, ILoggerFactory? logs)
        : this(explorerStore.Load() ?? new WorkspaceExplorer(Guid.NewGuid()), explorerStore, savedLayout, localization, logs)
    {
    }

    private WorkbenchSession(WorkspaceExplorer explorer, WorkspaceExplorerStore store, WorkbenchLayoutState? savedLayout,
        WorkbenchLocalization? localization, ILoggerFactory? logs)
        : this(new StandaloneWorkspace(explorer.WorkspaceId), savedLayout, explorer, store, localization, logs: logs)
    {
    }

    internal WorkbenchSession(IWorkbenchWorkspaceContext workspace, WorkbenchLayoutState? savedLayout,
        WorkspaceExplorer? explorer = null, WorkspaceExplorerStore? store = null, WorkbenchLocalization? localization = null,
        Func<DocumentSaveSnapshot, CancellationToken, Task<PreparedDocumentSave>>? stageSave = null, ILoggerFactory? logs = null)
    {
        Logs = logs ?? NullLoggerFactory.Instance;
        Explorer = explorer ?? new WorkspaceExplorer(workspace.WorkspaceId);
        ExplorerStore = store;
        Localization = localization ?? new WorkbenchLocalization();
        Interaction = new InteractionBlocks();
        Registry = new FeatureRegistry(new ViewContextContract(typeof(WorkbenchViewModel)), DocumentViewModel.ContextContract, stageSave);
        View = new WorkbenchViewModel(workspace.WorkspaceId) { FocusTarget = "registers", Localization = Localization };
        ExplorerView = new ExplorerViewModel(View, Registry.Explorer);
        SourceControl = new SourceControlViewModel(View, Registry, Interaction);
        Layout = new DockLayout(workspace, savedLayout, Interaction, ExplorerView, SourceControl, Localization);
        Controller = new WorkbenchController(Explorer, Layout, Interaction, Registry, Localization, Logs.CreateLogger<WorkbenchController>());
        Documents = new DocumentController(View, Layout, Interaction, Registry, Controller);
        Controller.Register(View);
        Controller.RegisterExplorer(ExplorerView, View, Documents);
        Controller.RegisterSourceControl(SourceControl, View);
        GitRestoration = Controller.RestoreGitAsync(View);
    }

    internal WorkspaceExplorer Explorer { get; }
    internal ILoggerFactory Logs { get; }
    internal WorkbenchLocalization Localization { get; }
    internal ExplorerViewModel ExplorerView { get; }
    internal SourceControlViewModel SourceControl { get; }
    internal DocumentController Documents { get; }
    internal WorkspaceExplorerStore? ExplorerStore { get; }
    internal Task GitRestoration { get; }

    /// <summary>Gets the live Dock model and its integration.</summary>
    public DockLayout Layout { get; }

    /// <summary>Gets the workbench's interaction-block service.</summary>
    public InteractionBlocks Interaction { get; }

    /// <summary>Gets the registered workbench ViewModel.</summary>
    public WorkbenchViewModel View { get; }

    /// <summary>Gets the shared feature registry.</summary>
    public FeatureRegistry Registry { get; }

    /// <summary>Gets the registered owner and application entry point.</summary>
    public WorkbenchController Controller { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Controller.Dispose();
        Documents.Dispose();
        Layout.Dispose();
        Interaction.Dispose();
        Localization.Dispose();
    }

    /// <summary>Drains native background work before releasing the workbench and its views.</summary>
    public async ValueTask DisposeAsync()
    {
        try { await Task.WhenAll(Controller.DrainGitRefreshAsync(View), Documents.DrainTerminalsAsync()).ConfigureAwait(true); }
        finally { Dispose(); }
    }

    private sealed record StandaloneWorkspace(Guid WorkspaceId) : IWorkbenchWorkspaceContext;
}
