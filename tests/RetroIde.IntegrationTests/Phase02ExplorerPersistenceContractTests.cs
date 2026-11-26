using System.Text.Json;
using Xunit;
using Xunit.Sdk;

namespace RetroIde.IntegrationTests;

/// <summary>Checks workspace reopen and switches during an outstanding tree listing.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-012#ac-test-explorer-persistence. Share persistence and ownership assertions between the reference and workbench.</remarks>
public sealed partial class Phase02ExplorerPersistenceContractTests
{
    /// <summary>Exercises the persistence contract and verifies that each independent fault is detected.</summary>
    [Theory]
    [InlineData("drop-root")]
    [InlineData("drop-expansion")]
    [InlineData("drop-selection")]
    [InlineData("return-old-listing")]
    [InlineData("load-old-workspace")]
    public async Task Reference_detects_persistence_and_workspace_ownership_faults(string fault)
    {
        using var files = new PersistenceFiles();
        await VerifyPersistenceAsync(new ReferencePersistence(files), files);
        await Assert.ThrowsAnyAsync<XunitException>(() => VerifyPersistenceAsync(new ReferencePersistence(files, fault), files));
    }

    private static async Task VerifyPersistenceAsync(IPersistence workbench, PersistenceFiles files)
    {
        var initial = await workbench.SeedAndCloseAsync();
        Assert.NotEqual(Guid.Empty, initial.Workspace);
        Assert.NotEqual(Guid.Empty, initial.View);
        Assert.Equal(2, initial.Roots.Count);
        Assert.All(initial.Roots, root => Assert.True(root.Expanded));
        var folder = Assert.Single(initial.Roots, root => root.Kind == "folder");
        var project = Assert.Single(initial.Roots, root => root.Kind == "project");
        Assert.NotEqual(folder.Id, project.Id);
        Assert.Equal(files.Folder, folder.Path);
        Assert.Equal(files.Project, project.Path);
        Assert.Equal(new FileAddress(folder.Id, "restore.asm"), initial.Selected);

        var reopened = await workbench.ReadReopenedAsync();
        Assert.Equal(initial.Workspace, reopened.Workspace);
        Assert.NotEqual(initial.View, reopened.View);
        Assert.Equal(initial.Roots.OrderBy(root => root.Id), reopened.Roots.OrderBy(root => root.Id));
        Assert.Equal(initial.Selected, reopened.Selected);

        var switched = await workbench.SwitchWhilePendingAsync();
        Assert.Equal(reopened.Workspace, switched.OldWorkspace);
        Assert.NotEqual(reopened.Workspace, switched.CurrentWorkspace);
        Assert.NotEqual(Guid.Empty, switched.PendingOperation);
        Assert.Equal(switched.PendingOperation, switched.ResultOperation);
        Assert.Equal("view_closed", switched.OldResultCode);
        Assert.False(switched.OldResultHasEntries);
        Assert.Equal(files.Replacement, switched.CurrentRoot);
        Assert.Equal("current.asm", Assert.Single(switched.CurrentEntries));
    }

    private interface IPersistence
    {
        Task<WorkspaceState> SeedAndCloseAsync();
        Task<WorkspaceState> ReadReopenedAsync();
        Task<SwitchResult> SwitchWhilePendingAsync();
    }

    private sealed record RootState(Guid Id, string Kind, string Path, bool Expanded);
    private sealed record FileAddress(Guid RootId, string RelativePath);
    private sealed record WorkspaceState(Guid Workspace, Guid View, IReadOnlyList<RootState> Roots, FileAddress? Selected);
    private sealed record SwitchResult(Guid OldWorkspace, Guid CurrentWorkspace, Guid PendingOperation, Guid ResultOperation,
        string OldResultCode, bool OldResultHasEntries, string CurrentRoot, IReadOnlyList<string> CurrentEntries);

    private sealed class PersistenceFiles : IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("retro-ide-explorer-persistence-");

        public PersistenceFiles()
        {
            Folder = _root.CreateSubdirectory("folder").FullName;
            Project = _root.CreateSubdirectory("project").FullName;
            Replacement = _root.CreateSubdirectory("replacement").FullName;
            File.WriteAllText(Path.Combine(Folder, "restore.asm"), "old workspace");
            File.WriteAllText(Path.Combine(Project, "project.asm"), "project source");
            File.WriteAllText(Path.Combine(Replacement, "current.asm"), "current workspace");
        }

        public string Folder { get; }
        public string Project { get; }
        public string Replacement { get; }
        public string Store => Path.Combine(_root.FullName, "workspace.json");
        public void Dispose() => _root.Delete(recursive: true);
    }

    private sealed class ReferencePersistence(PersistenceFiles files, string? fault = null) : IPersistence
    {
        private WorkspaceState? _reopened;

        public async Task<WorkspaceState> SeedAndCloseAsync()
        {
            var folder = new RootState(Guid.NewGuid(), "folder", files.Folder, Expanded: true);
            var project = new RootState(Guid.NewGuid(), "project", files.Project, Expanded: true);
            var selected = new FileAddress(folder.Id, Path.GetFileName(Assert.Single(Directory.GetFiles(folder.Path))));
            var state = new WorkspaceState(Guid.NewGuid(), Guid.NewGuid(), [folder, project], selected);
            var persisted = fault switch
            {
                "drop-root" => state with { Roots = [folder] },
                "drop-expansion" => state with { Roots = [folder with { Expanded = false }, project] },
                "drop-selection" => state with { Selected = null },
                _ => state,
            };
            await File.WriteAllTextAsync(files.Store, JsonSerializer.Serialize(persisted), TestContext.Current.CancellationToken);
            return state;
        }

        public async Task<WorkspaceState> ReadReopenedAsync()
        {
            var json = await File.ReadAllTextAsync(files.Store, TestContext.Current.CancellationToken);
            var saved = Assert.IsType<WorkspaceState>(JsonSerializer.Deserialize<WorkspaceState>(json));
            _reopened = saved with { View = Guid.NewGuid() };
            return _reopened;
        }

        public async Task<SwitchResult> SwitchWhilePendingAsync()
        {
            var previous = Assert.IsType<WorkspaceState>(_reopened);
            var pending = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var operation = Guid.NewGuid();
            var replacement = Guid.NewGuid();
            var root = fault == "load-old-workspace" ? previous.Roots[0].Path : files.Replacement;
            var currentEntries = Directory.GetFiles(root).Select(Path.GetFileName).OfType<string>().ToArray();
            pending.SetResult(Directory.GetFiles(previous.Roots[0].Path));
            var entries = await pending.Task;
            var returnRetired = fault == "return-old-listing";
            return new SwitchResult(previous.Workspace, replacement, operation, operation,
                returnRetired ? "ok" : "view_closed", returnRetired && entries.Length != 0, root, currentEntries);
        }
    }
}
