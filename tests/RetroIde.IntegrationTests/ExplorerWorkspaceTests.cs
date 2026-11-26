using Avalonia.Headless.XUnit;
using RetroIde.Application;
using RetroIde.Application.Workspaces;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

/// <summary>Exercises stable workspace roots and lazy directory reads against local filesystem fixtures.</summary>
public sealed class ExplorerWorkspaceTests
{
    /// <summary>Folder-only and mixed projects retain distinct root-relative identities.</summary>
    /// <remarks>TEST-RETRO-012#ac-test-explorer-mixed-roots</remarks>
    [Fact]
    public async Task Same_name_files_remain_scoped_to_their_open_root()
    {
        using var fixture = new WorkspaceDirectories();
        var folder = fixture.Folder("plain");
        var project = fixture.Folder("project");
        File.WriteAllText(Path.Combine(folder, "main.asm"), "plain");
        File.WriteAllText(Path.Combine(project, "main.asm"), "project");

        var explorer = new WorkspaceExplorer(Guid.NewGuid());
        var plainRoot = explorer.AddRoot(folder, WorkspaceExplorer.RootKind.Folder);
        Assert.Equal(plainRoot, Assert.Single(explorer.Roots));
        var projectRoot = explorer.AddRoot(project, WorkspaceExplorer.RootKind.Project);
        Assert.NotEqual(plainRoot.Id, projectRoot.Id);
        Assert.Equal(2, explorer.Roots.Count);

        var token = TestContext.Current.CancellationToken;
        var plainFile = Assert.Single((await explorer.ReadChildrenAsync(new(plainRoot.Id, ""), token)).Entries);
        var projectFile = Assert.Single((await explorer.ReadChildrenAsync(new(projectRoot.Id, ""), token)).Entries);
        Assert.Equal("main.asm", plainFile.Address.RelativePath);
        Assert.Equal(plainFile.Address.RelativePath, projectFile.Address.RelativePath);
        Assert.Equal(plainRoot.Id, plainFile.Address.RootId);
        Assert.Equal(projectRoot.Id, projectFile.Address.RootId);
    }

    /// <summary>Rehydration retains persistent root identities and canonical root aliases.</summary>
    [Fact]
    public void Saved_roots_keep_their_identity()
    {
        using var fixture = new WorkspaceDirectories();
        var directory = fixture.Folder("work");
        var initial = new WorkspaceExplorer(Guid.NewGuid());
        var root = initial.AddRoot(directory, WorkspaceExplorer.RootKind.Folder);

        var restored = new WorkspaceExplorer(initial.WorkspaceId, initial.Roots);
        Assert.Equal(initial.WorkspaceId, restored.WorkspaceId);
        Assert.Equal(root, Assert.Single(restored.Roots));
        Assert.Equal(root.Id, restored.AddRoot(Path.Combine(directory, "."), WorkspaceExplorer.RootKind.Folder).Id);
        Assert.Single(restored.Roots);
    }

    /// <summary>Reopening the same root with a different kind refreshes its visible label without losing its place.</summary>
    /// <remarks>TEST-RETRO-012#ac-test-explorer-mixed-roots</remarks>
    [AvaloniaFact]
    public async Task Reopening_a_root_updates_its_live_kind_without_replacing_the_row()
    {
        using var fixture = new WorkspaceDirectories();
        var directory = fixture.Folder("work");
        File.WriteAllText(Path.Combine(directory, "source.asm"), "source");
        using var session = new WorkbenchSession(Guid.NewGuid());
        var explorer = session.ExplorerView;
        await explorer.OpenFolderCommand.ExecuteAsync(directory);
        var root = Assert.Single(explorer.TreeRoots);
        var rootId = Assert.Single(explorer.Roots).Id;
        Assert.Equal($"Folder: {directory}", root.Label);
        await OpenBranchAsync(explorer, root, TestContext.Current.CancellationToken);
        var file = Assert.Single(root.Children);
        await explorer.SelectEntryCommand.ExecuteAsync(file);
        var displayed = new List<string>();
        root.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ExplorerTreeNode.Label))
                displayed.Add(root.Label);
        };

        await explorer.OpenProjectCommand.ExecuteAsync(directory);

        Assert.Equal(rootId, Assert.Single(explorer.Roots).Id);
        Assert.Equal(WorkspaceExplorer.RootKind.Project, Assert.Single(explorer.Roots).Kind);
        Assert.Same(root, Assert.Single(explorer.TreeRoots));
        Assert.Equal($"Project: {directory}", root.Label);
        Assert.Contains(root.Label, displayed);
        Assert.True(root.IsExpanded);
        Assert.Same(file, Assert.Single(root.Children));
        Assert.Same(file, explorer.SelectedNode);
    }

    /// <summary>A removed root and foreign or escaping address leave other roots available.</summary>
    /// <remarks>TEST-RETRO-012#ac-test-explorer-changes</remarks>
    [Fact]
    public async Task Listing_is_lazy_and_unavailable_roots_do_not_poison_other_branches()
    {
        using var fixture = new WorkspaceDirectories();
        var folder = fixture.Folder("folder");
        var removed = fixture.Folder("removed");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllText(Path.Combine(folder, "sub", "nested.asm"), "nested");
        var explorer = new WorkspaceExplorer(Guid.NewGuid());
        var intact = explorer.AddRoot(folder, WorkspaceExplorer.RootKind.Folder);
        var missing = explorer.AddRoot(removed, WorkspaceExplorer.RootKind.Project);
        Directory.Delete(removed);

        var token = TestContext.Current.CancellationToken;
        Assert.False((await explorer.ReadChildrenAsync(new(missing.Id, ""), token)).IsAvailable);
        Assert.False((await explorer.ReadChildrenAsync(new(Guid.NewGuid(), ""), token)).IsAvailable);
        Assert.False((await explorer.ReadChildrenAsync(new(intact.Id, ".."), token)).IsAvailable);
        Assert.False((await explorer.ReadChildrenAsync(new(intact.Id, removed), token)).IsAvailable);

        var listing = await explorer.ReadChildrenAsync(new(intact.Id, ""), token);
        Assert.True(listing.IsAvailable);
        Assert.Equal(explorer.WorkspaceId, listing.WorkspaceId);
        var subfolder = Assert.Single(listing.Entries);
        Assert.True(subfolder.IsDirectory);
        Assert.Equal("sub", subfolder.Name);
        var nested = Assert.Single((await explorer.ReadChildrenAsync(subfolder.Address, token)).Entries);
        Assert.Equal("nested.asm", nested.Name);
        Assert.False(nested.IsDirectory);
    }

    /// <summary>One on-disk snapshot keeps root identity, expansion and selection across a process restart.</summary>
    [Fact]
    public void Restores_the_explorer_snapshot_with_an_unavailable_root()
    {
        using var fixture = new WorkspaceDirectories();
        var directory = fixture.Folder("plain");
        var unavailable = fixture.Folder("unavailable");
        var path = Path.Combine(fixture.Folder("settings"), "workspace.json");
        var store = new WorkspaceExplorerStore(path);
        var original = new WorkspaceExplorer(Guid.NewGuid());
        var folder = original.AddRoot(directory, WorkspaceExplorer.RootKind.Folder);
        var project = original.AddRoot(unavailable, WorkspaceExplorer.RootKind.Project);
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        original.SetExpanded(new(folder.Id, Path.Combine("src", ".")), isExpanded: true);
        original.SetExpanded(new(project.Id, ""), isExpanded: true);
        original.Select(new(folder.Id, Path.Combine("src", "main.asm")));
        Directory.Delete(unavailable);
        store.Save(original.Snapshot(), TestContext.Current.CancellationToken);

        var reopened = Assert.IsType<WorkspaceExplorer>(new WorkspaceExplorerStore(path).Load());
        var state = reopened.Snapshot();
        Assert.Equal(original.WorkspaceId, state.WorkspaceId);
        Assert.Equal(new[] { folder, project }, state.Roots);
        Assert.Contains(new WorkspaceExplorer.Address(folder.Id, "src"), state.Expanded);
        Assert.Contains(new WorkspaceExplorer.Address(project.Id, ""), state.Expanded);
        Assert.Equal(new WorkspaceExplorer.Address(folder.Id, Path.Combine("src", "main.asm")), state.Selection);
    }

    /// <summary>Corrupt workspace state is optional and a cancelled save keeps the prior snapshot.</summary>
    [Fact]
    public void Invalid_state_and_cancelled_save_leave_a_recoverable_workspace()
    {
        using var fixture = new WorkspaceDirectories();
        var path = Path.Combine(fixture.Folder("settings"), "workspace.json");
        var store = new WorkspaceExplorerStore(path);
        var original = new WorkspaceExplorer(Guid.NewGuid());
        var root = original.AddRoot(fixture.Folder("root"), WorkspaceExplorer.RootKind.Folder);
        store.Save(original.Snapshot(), TestContext.Current.CancellationToken);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        original.Select(new(root.Id, ""));
        Assert.Throws<OperationCanceledException>(() => store.Save(original.Snapshot(), cancelled.Token));
        Assert.Null(Assert.IsType<WorkspaceExplorer>(store.Load()).Snapshot().Selection);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));

        store.Save(original.Snapshot() with { Selection = new(root.Id, "..") }, TestContext.Current.CancellationToken);
        Assert.Null(store.Load());
        File.WriteAllText(path, "{");
        Assert.Null(store.Load());
    }

    /// <summary>Completion after a registration closes cannot return the pending directory data.</summary>
    /// <remarks>TEST-RETRO-012#ac-test-explorer-persistence</remarks>
    [AvaloniaFact]
    public async Task Closed_registration_cannot_publish_pending_query_data()
    {
        using var session = new WorkbenchSession(Guid.NewGuid());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<FeatureOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = new Feature<GetContextInput>("test.explorer.pending", "test_explorer_pending", "Test a pending read.",
            (_, _, _) =>
            {
                started.SetResult();
                return finish.Task;
            }, typeof(string));
        var grant = new ActorGrant("test", session.View.WorkspaceId, session.View.ViewKey, canMutate: false);
        var operation = Guid.NewGuid();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var query = session.Controller.DispatchAsync(read, new GetContextInput(), null, grant, operation, timeout.Token);
        await started.Task.WaitAsync(timeout.Token);
        session.Controller.Unregister(session.View);
        using var workspace = new WorkspaceDirectories();
        using var replacement = new WorkbenchSession(Guid.NewGuid());
        var rootPath = workspace.Folder("replacement");
        File.WriteAllText(Path.Combine(rootPath, "current.asm"), "current workspace");
        await replacement.ExplorerView.OpenFolderCommand.ExecuteAsync(rootPath);
        var newRoot = Assert.Single(replacement.ExplorerView.TreeRoots);
        await OpenBranchAsync(replacement.ExplorerView, newRoot, timeout.Token);
        finish.SetResult(FeatureOutcome.Success("A detached read finished.", "must not be published"));

        var rejected = await query.WaitAsync(timeout.Token);
        Assert.Equal(operation, rejected.OperationId);
        Assert.Equal("view_closed", rejected.Code);
        Assert.Null(rejected.Data);
        Assert.NotEqual(session.View.WorkspaceId, replacement.View.WorkspaceId);
        Assert.Equal("current.asm", Assert.Single(newRoot.Children).Label);
    }

    /// <summary>Filesystem changes refresh affected branches while retaining unrelated nodes and roots.</summary>
    /// <remarks>TEST-RETRO-012#ac-test-explorer-changes</remarks>
    [AvaloniaFact]
    public async Task External_changes_refresh_only_affected_expanded_branches()
    {
        using var fixture = new WorkspaceDirectories();
        var first = fixture.Folder("first");
        var one = Directory.CreateDirectory(Path.Combine(first, "one")).FullName;
        var two = Directory.CreateDirectory(Path.Combine(first, "two")).FullName;
        var second = fixture.Folder("second");
        File.WriteAllText(Path.Combine(two, "stable.asm"), "stable");
        File.WriteAllText(Path.Combine(second, "other.asm"), "other");
        using var session = new WorkbenchSession(Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var token = timeout.Token;
        var explorer = session.ExplorerView;
        await explorer.OpenFolderCommand.ExecuteAsync(first);
        await explorer.OpenProjectCommand.ExecuteAsync(second);
        var roots = explorer.TreeRoots.ToArray();
        var firstRoot = roots[0];
        var secondRoot = roots[1];
        await OpenBranchAsync(explorer, firstRoot, token);
        await OpenBranchAsync(explorer, secondRoot, token);

        var firstBranch = firstRoot.Children.Single(node => string.Equals(node.Label, "one", StringComparison.Ordinal));
        var secondBranch = firstRoot.Children.Single(node => string.Equals(node.Label, "two", StringComparison.Ordinal));
        await OpenBranchAsync(explorer, firstBranch, token);
        await OpenBranchAsync(explorer, secondBranch, token);
        File.WriteAllText(Path.Combine(one, "start.asm"), "start");
        await UntilAsync(() => HasFile(firstBranch, "start.asm"), token);
        File.WriteAllText(Path.Combine(second, "ready.asm"), "ready");
        await UntilAsync(() => HasFile(secondRoot, "ready.asm"), token);
        await VerifyTargetedChangesAsync(firstBranch, secondBranch, secondRoot, one, two, token);

        Directory.Delete(second, recursive: true);
        await UntilAsync(() => secondRoot.Label.EndsWith("(unavailable)", StringComparison.Ordinal) &&
            secondRoot.Children.Count == 1 && secondRoot.Children[0].Address is null, token);
        Assert.Same(firstBranch, firstRoot.Children[0]);
        File.WriteAllText(Path.Combine(one, "while-unavailable.asm"), "works");
        await UntilAsync(() => HasFile(firstBranch, "while-unavailable.asm"), token);
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(second, "recovered.asm"), "works");
        await UntilAsync(() => HasFile(secondRoot, "recovered.asm"), token);
        Assert.DoesNotContain("(unavailable)", secondRoot.Label, StringComparison.Ordinal);
    }

    private static async Task VerifyTargetedChangesAsync(ExplorerTreeNode first, ExplorerTreeNode second,
        ExplorerTreeNode otherRoot, string fromFolder, string toFolder, CancellationToken token)
    {
        var firstRead = first.ReadVersion;
        var unaffectedRead = second.ReadVersion;
        var otherRootRead = otherRoot.ReadVersion;
        var stable = Assert.Single(second.Children);
        for (var i = 0; i < 20; i++)
            File.WriteAllText(Path.Combine(fromFolder, $"new-{i}.asm"), "new");

        await UntilAsync(() => first.Children.Count == 21, token);
        Assert.InRange(first.ReadVersion - firstRead, 1, 19);
        Assert.Equal(unaffectedRead, second.ReadVersion);
        Assert.Equal(otherRootRead, otherRoot.ReadVersion);
        Assert.Same(stable, Assert.Single(second.Children));

        File.Move(Path.Combine(fromFolder, "start.asm"), Path.Combine(toFolder, "renamed.asm"));
        await UntilAsync(() => !HasFile(first, "start.asm") && HasFile(second, "renamed.asm"), token);
        File.Delete(Path.Combine(toFolder, "renamed.asm"));
        await UntilAsync(() => !HasFile(second, "renamed.asm"), token);
        Assert.Same(stable, Assert.Single(second.Children));
    }

    /// <summary>Collapsing a parent defers I/O and reopening refreshes an expanded descendant.</summary>
    /// <remarks>TEST-RETRO-012#ac-test-explorer-changes</remarks>
    [AvaloniaFact]
    public async Task Collapsed_branches_refresh_lazily_when_reopened()
    {
        using var fixture = new WorkspaceDirectories();
        var folder = fixture.Folder("root");
        var nested = Directory.CreateDirectory(Path.Combine(folder, "nested")).FullName;
        using var session = new WorkbenchSession(Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var token = timeout.Token;
        var explorer = session.ExplorerView;
        await explorer.OpenFolderCommand.ExecuteAsync(folder);
        var root = Assert.Single(explorer.TreeRoots);
        await OpenBranchAsync(explorer, root, token);
        var branch = Assert.Single(root.Children);
        await OpenBranchAsync(explorer, branch, token);
        await explorer.SetExpansionCommand.ExecuteAsync(new ExplorerViewModel.ExpansionIntent(root, false));
        var readVersion = branch.ReadVersion;

        File.WriteAllText(Path.Combine(nested, "hidden.asm"), "deferred");
        Assert.False(branch.IsExpanded);
        Assert.Equal(readVersion, branch.ReadVersion);

        await OpenBranchAsync(explorer, root, token);
        await UntilAsync(() => HasFile(branch, "hidden.asm"), token);
        Assert.Same(branch, Assert.Single(root.Children));
        Assert.True(branch.IsExpanded);
    }

    /// <summary>A normal workbench close saves the live Explorer selection and restores it on reopen.</summary>
    /// <remarks>TEST-RETRO-012#ac-test-explorer-persistence</remarks>
    [AvaloniaFact]
    public async Task Reopen_restores_live_expansion_and_selection()
    {
        using var fixture = new WorkspaceDirectories();
        var rootPath = fixture.Folder("root");
        File.WriteAllText(Path.Combine(rootPath, "restore.asm"), "source");
        var store = new WorkspaceExplorerStore(Path.Combine(fixture.Folder("settings"), "workspace.json"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = timeout.Token;
        WorkspaceExplorer.Address address;
        Guid workspaceId;
        Guid viewId;
        using (var before = new WorkbenchSession(store, savedLayout: null))
        {
            var window = new WorkbenchWindow(before);
            window.Show();
            var explorer = before.ExplorerView;
            await explorer.OpenFolderCommand.ExecuteAsync(rootPath);
            var root = Assert.Single(explorer.TreeRoots);
            await OpenBranchAsync(explorer, root, token);
            var file = Assert.Single(root.Children);
            address = file.Address!.Value;
            await explorer.SelectEntryCommand.ExecuteAsync(file);
            Assert.Same(file, explorer.SelectedNode);
            workspaceId = before.View.WorkspaceId;
            viewId = before.View.ViewKey;

            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            window.Close();
            await closed.Task.WaitAsync(token);
        }

        using var reopened = new WorkbenchSession(store, savedLayout: null);
        await UntilAsync(() => reopened.ExplorerView.SelectedNode?.Address == address, token);
        Assert.Equal(workspaceId, reopened.View.WorkspaceId);
        Assert.NotEqual(viewId, reopened.View.ViewKey);
        var restored = Assert.Single(reopened.ExplorerView.TreeRoots);
        Assert.True(restored.IsExpanded);
        Assert.Same(Assert.Single(restored.Children), reopened.ExplorerView.SelectedNode);
    }

    private static async Task OpenBranchAsync(ExplorerViewModel view, ExplorerTreeNode node, CancellationToken token)
    {
        await view.SetExpansionCommand.ExecuteAsync(new ExplorerViewModel.ExpansionIntent(node, true));
        await UntilAsync(() => node.IsExpanded && node.Children.All(child => child.Address is not null), token);
    }

    private static bool HasFile(ExplorerTreeNode branch, string name) =>
        branch.Children.Any(child => child.Address is not null && string.Equals(child.Label, name, StringComparison.Ordinal));

    private static async Task UntilAsync(Func<bool> condition, CancellationToken token)
    {
        while (!condition())
            await Task.Delay(25, token);
    }

    private sealed class WorkspaceDirectories : IDisposable
    {
        private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("retroide-explorer-");
        public string Folder(string name) => Directory.CreateDirectory(Path.Combine(_temp.FullName, name)).FullName;
        public void Dispose() => _temp.Delete(recursive: true);
    }
}
