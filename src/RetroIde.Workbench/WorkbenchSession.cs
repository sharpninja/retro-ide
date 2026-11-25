using RetroIde.Application;

namespace RetroIde.Workbench;

internal interface IWorkbenchWorkspaceContext
{
    Guid WorkspaceId { get; }
}

/// <summary>Composes one workbench and its live registration independently of emulator sessions.</summary>
/// <remarks>TEST-RETRO-001#verify-ac-behavior-desktop-2</remarks>
public sealed class WorkbenchSession : IDisposable
{
    /// <summary>Creates a workbench and applies a saved layout if it is compatible.</summary>
    public WorkbenchSession(Guid workspaceId, WorkbenchLayoutState? savedLayout = null)
        : this(new StandaloneWorkspace(workspaceId), savedLayout)
    {
    }

    internal WorkbenchSession(IWorkbenchWorkspaceContext workspace, WorkbenchLayoutState? savedLayout)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(workspace.WorkspaceId, Guid.Empty);
        Interaction = new InteractionBlocks();
        Layout = new DockLayout(workspace, savedLayout, Interaction);
        Registry = new FeatureRegistry(new ViewContextContract(typeof(WorkbenchViewModel)));
        View = new WorkbenchViewModel(workspace.WorkspaceId) { FocusTarget = "registers" };
        Controller = new WorkbenchController(workspace.WorkspaceId, Layout, Interaction, Registry);
        Controller.Register(View);
    }

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
        Layout.Dispose();
        Interaction.Dispose();
    }

    private sealed record StandaloneWorkspace(Guid WorkspaceId) : IWorkbenchWorkspaceContext;
}
