using System.Collections;
using System.Reflection;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class Phase02ExplorerWatcherContractTests
{
    /// <summary>Watches real external changes through the Project Explorer command and tree model.</summary>
    /// <remarks>2026-11-26 · TEST-RETRO-012#ac-test-explorer-changes. Observe the filesystem watcher without triggering manual refresh.</remarks>
    [AvaloniaFact]
    public async Task Explorer_refreshes_only_the_changed_available_branches()
    {
        using var files = new WatchFiles();
        using var session = new WorkbenchSession(Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var explorer = typeof(WorkbenchSession).GetProperty("ExplorerView", Access)?.GetValue(session);
        Assert.True(explorer is not null, "The live workbench does not expose the Project Explorer for external change monitoring.");
        await VerifyChangesAsync(new LiveWatch(explorer, files, timeout.Token), files);
    }

    private const BindingFlags Access = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class LiveWatch(object explorer, WatchFiles files, CancellationToken token) : IBranchWatch
    {
        private readonly Dictionary<string, object> _nodes = new(StringComparer.Ordinal);

        public async Task PrepareAsync()
        {
            var roots = Assert.IsAssignableFrom<IEnumerable>(Read(explorer, "TreeRoots"));
            var openFolder = Assert.IsAssignableFrom<IAsyncRelayCommand>(Read(explorer, "OpenFolderCommand"));
            var openProject = Assert.IsAssignableFrom<IAsyncRelayCommand>(Read(explorer, "OpenProjectCommand"));
            await openFolder.ExecuteAsync(files.First).WaitAsync(token);
            var main = Assert.Single(roots.Cast<object>());
            _nodes.Add(files.First, main);
            await openProject.ExecuteAsync(files.Other).WaitAsync(token);
            _nodes.Add(files.Other, Assert.Single(roots.Cast<object>(), node => !ReferenceEquals(main, node)));

            await SetExpandedAsync(files.First, expanded: true);
            var children = Items(main);
            while (children.Length != 2)
            {
                await Task.Delay(20, token);
                children = Items(main);
            }
            RegisterChild(files.Changed);
            RegisterChild(files.Sibling);
            await SetExpandedAsync(files.Changed, expanded: true);
            await SetExpandedAsync(files.Sibling, expanded: true);
            await SetExpandedAsync(files.Other, expanded: true);
            await ObserveAsync(files.Changed, ["old.asm"], available: true);
            await ObserveAsync(files.Sibling, ["stable.asm"], available: true);
            await ObserveAsync(files.Other, ["other.asm"], available: true);

            void RegisterChild(string path) => _nodes.Add(path, Assert.Single(children, node =>
                string.Equals(Path.GetFileName(path), Label(node), StringComparison.Ordinal)));
        }

        public async Task SetExpandedAsync(string path, bool expanded)
        {
            var type = Assert.IsAssignableFrom<Type>(explorer.GetType().GetNestedType("ExpansionIntent", BindingFlags.Public | BindingFlags.NonPublic));
            var intent = Activator.CreateInstance(type, Access, binder: null, [_nodes[path], expanded], culture: null);
            var expand = Assert.IsAssignableFrom<IAsyncRelayCommand>(Read(explorer, "SetExpansionCommand"));
            await expand.ExecuteAsync(intent).WaitAsync(token);
            while (Capture(path).Expanded != expanded)
                await Task.Delay(20, token);
        }

        public async Task<BranchState> ObserveAsync(string path, string[] entries, bool available)
        {
            var snapshot = Capture(path);
            while (snapshot.Available != available || !snapshot.Entries.SequenceEqual(entries, StringComparer.Ordinal))
            {
                await Task.Delay(20, token);
                snapshot = Capture(path);
            }
            return snapshot;
        }

        public BranchState Capture(string path)
        {
            var node = _nodes[path];
            var entries = Items(node).Select(Label).Order(StringComparer.Ordinal).ToArray();
            var available = !Label(node).EndsWith("(unavailable)", StringComparison.Ordinal);
            return new BranchState(node, Assert.IsType<long>(Read(node, "ReadVersion")),
                Assert.IsType<bool>(Read(node, "IsExpanded")), available, entries);
        }

        private static object[] Items(object node) => Assert.IsAssignableFrom<IEnumerable>(Read(node, "Children")).Cast<object>()
            .Where(child => Read(child, "Address") is not null).ToArray();

        private static string Label(object node) => Assert.IsType<string>(Read(node, "Label"));

        private static object? Read(object source, string property) =>
            Assert.IsAssignableFrom<PropertyInfo>(source.GetType().GetProperty(property, Access)).GetValue(source);
    }
}
