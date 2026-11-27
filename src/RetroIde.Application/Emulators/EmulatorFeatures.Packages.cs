using RetroIde.Application.Workspaces;
using Pin = RetroIde.Application.Emulators.EmulatorBundleStore.Pin;
using Project = RetroIde.Application.Workspaces.WorkspaceExplorer.Root;

namespace RetroIde.Application.Emulators;

internal sealed partial class EmulatorFeatures
{
    internal sealed record BundleInput(Pin Pin);
    internal sealed record UpdateInput(Pin Current, Pin Target);
    internal sealed record ProjectInput(Guid RootId);
    internal sealed record SelectionInput(Guid RootId, Pin Pin);

    internal Feature<GetContextInput> Inventory { get; } = new("emulator.bundles.inventory", "emulator_list_installed",
        "List verified emulator bundles installed for the current host.",
        static (_, view, token) => view is IEmulatorRegistration { Emulators: { } workspace }
            ? RunAsync(workspace, static (emulators, ct) => Task.Run(() => emulators.Installed(ct), ct), "Installed bundles read.", token, "bundle")
            : Task.FromResult(Unavailable()), typeof(EmulatorWorkspace.InstalledState[]));

    internal Feature<BundleInput> Install { get; } = new("emulator.bundle.install", "emulator_install_bundle",
        "Verify and install the emulator bundle defined by an exact bundle pin.",
        static (input, view, _) => Prepare(view, (workspace, token) => workspace.InstallAsync(input.Pin, token), "Emulator bundle installed.", "bundle"),
        typeof(EmulatorWorkspace.InstalledState), mutation: true, background: true);

    internal Feature<UpdateInput> Update { get; } = new("emulator.bundle.update", "emulator_update_bundle",
        "Install the reviewed current catalog release alongside the installed release.",
        static (input, view, _) => Prepare(view, (workspace, token) => workspace.UpdateAsync(input.Current, input.Target, token),
            "Emulator bundle update installed.", "bundle"), typeof(EmulatorWorkspace.InstalledState), mutation: true, background: true);

    internal Feature<BundleInput> RemoveBundle { get; } = new("emulator.bundle.remove", "emulator_remove_bundle",
        "Remove an exact installed bundle when no session holds a lease on it.",
        static (input, view, _) => Prepare(view, (workspace, token) => Task.Run(() => workspace.RemoveBundle(input.Pin, token), token),
            "Emulator bundle removed.", "bundle"), typeof(EmulatorWorkspace.RemovalState), mutation: true);

    internal Feature<ProjectInput> Selection { get; } = new("emulator.project.read", "emulator_get_project_bundle",
        "Read a project root's selected emulator bundle, previous pins, and resolved binding for the current host.",
        static (input, view, token) => view is not IEmulatorRegistration { Emulators: { } workspace } ? Task.FromResult(Unavailable()) :
            ResolveProject(view, input.RootId) is { } project
                ? RunAsync(workspace, (emulators, ct) => Task.Run(() => emulators.ReadSelection(project), ct), "Project selection read.", token, "project")
                : Task.FromResult(MissingProject()), typeof(EmulatorWorkspace.SelectionState));

    internal Feature<SelectionInput> Select { get; } = new("emulator.project.select", "emulator_select_bundle",
        "Select a verified installed emulator bundle for an open project root.",
        static (input, view, _) => ChangeSelection(input, view, rollback: false),
        typeof(EmulatorWorkspace.SelectionState), mutation: true);

    internal Feature<SelectionInput> Rollback { get; } = new("emulator.project.rollback", "emulator_rollback_bundle",
        "Restore an earlier compatible installed bundle from the open project's selection history.",
        static (input, view, _) => ChangeSelection(input, view, rollback: true),
        typeof(EmulatorWorkspace.SelectionState), mutation: true);

    private static Task<FeatureOutcome> ChangeSelection(SelectionInput input, IWorkbenchRegistration view, bool rollback)
    {
        if (ResolveProject(view, input.RootId) is not { } project)
            return Task.FromResult(MissingProject());
        return Prepare(view, (workspace, token) => workspace.SelectAsync(project, input.Pin, rollback, token),
            rollback ? "Project bundle restored." : "Project bundle selected.", "project", project);
    }

    private static Project? ResolveProject(IWorkbenchRegistration view, Guid id) =>
        view is IExplorerRegistration { HasExplorer: true } explorer
            ? explorer.Explorer.Roots.SingleOrDefault(root => root.Id == id && root.Kind == WorkspaceExplorer.RootKind.Project)
            : null;

    private static FeatureOutcome MissingProject() => new("emulator_project_missing", "Open the selected root as a project in this workbench.");
}
