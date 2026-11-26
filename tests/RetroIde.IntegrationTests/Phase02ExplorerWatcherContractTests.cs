using Xunit;
using Xunit.Sdk;

namespace RetroIde.IntegrationTests;

/// <summary>Checks how Project Explorer responds to file changes outside the IDE.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-012#ac-test-explorer-changes. Compare targeted updates, deferred reads and behavior with one root missing.</remarks>
public sealed partial class Phase02ExplorerWatcherContractTests
{
    /// <summary>Exercises the normal notification sequence and detects six independent faults.</summary>
    [Theory]
    [InlineData("miss-create")]
    [InlineData("miss-rename")]
    [InlineData("miss-delete")]
    [InlineData("read-unrelated")]
    [InlineData("read-collapsed")]
    [InlineData("block-other-root")]
    public async Task Reference_detects_watcher_and_availability_faults(string fault)
    {
        using var clean = new WatchFiles();
        await VerifyChangesAsync(new ReferenceWatch(clean), clean);
        using var broken = new WatchFiles();
        await Assert.ThrowsAnyAsync<XunitException>(() => VerifyChangesAsync(new ReferenceWatch(broken, fault), broken));
    }

    private static async Task VerifyChangesAsync(IBranchWatch watch, WatchFiles files)
    {
        await watch.PrepareAsync();
        Write(files.Changed, "ready.asm");
        Write(files.Other, "ready.asm");
        var before = await ObserveAsync(files.Changed, ["old.asm", "ready.asm"]);
        var sibling = watch.Capture(files.Sibling);
        var other = await ObserveAsync(files.Other, ["other.asm", "ready.asm"]);

        Write(files.Changed, "created.asm");
        var created = await ObserveAsync(files.Changed, ["created.asm", "old.asm", "ready.asm"]);
        Assert.True(created.Read > before.Read);
        Assert.Same(before.Identity, created.Identity);
        AssertStable(sibling, watch.Capture(files.Sibling));
        AssertStable(other, watch.Capture(files.Other));

        File.Move(Path.Combine(files.Changed, "old.asm"), Path.Combine(files.Sibling, "renamed.asm"));
        await ObserveAsync(files.Changed, ["created.asm", "ready.asm"]);
        var renamed = await ObserveAsync(files.Sibling, ["renamed.asm", "stable.asm"]);
        Assert.Same(sibling.Identity, renamed.Identity);
        Assert.True(renamed.Read > sibling.Read);
        File.Delete(Path.Combine(files.Sibling, "renamed.asm"));
        var removed = await ObserveAsync(files.Sibling, ["stable.asm"]);
        Assert.True(removed.Read > renamed.Read);

        await watch.SetExpandedAsync(files.First, expanded: false);
        var collapsed = watch.Capture(files.Changed);
        Assert.False(collapsed.Expanded);
        Write(files.Changed, "deferred.asm");
        Write(files.Other, "pulse.asm");
        await ObserveAsync(files.Other, ["other.asm", "pulse.asm", "ready.asm"]);
        var whileCollapsed = watch.Capture(files.Changed);
        AssertStable(collapsed, whileCollapsed);
        Assert.DoesNotContain("deferred.asm", whileCollapsed.Entries);
        await watch.SetExpandedAsync(files.First, expanded: true);
        var reopened = await ObserveAsync(files.Changed, ["created.asm", "deferred.asm", "ready.asm"]);
        Assert.Same(collapsed.Identity, reopened.Identity);
        Assert.True(reopened.Expanded);

        Directory.Delete(files.Other, recursive: true);
        await ObserveAsync(files.Other, [], available: false);
        Write(files.Changed, "still-usable.asm");
        var isolated = await ObserveAsync(files.Changed, ["created.asm", "deferred.asm", "ready.asm", "still-usable.asm"]);
        Assert.True(isolated.Read > reopened.Read);

        async Task<BranchState> ObserveAsync(string path, string[] expected, bool available = true)
        {
            var node = await watch.ObserveAsync(path, expected, available);
            Assert.Equal(available, node.Available);
            Assert.Equal(expected, node.Entries);
            return node;
        }
    }

    private static void AssertStable(BranchState before, BranchState after)
    {
        Assert.Equal(before.Read, after.Read);
        Assert.Same(before.Identity, after.Identity);
        Assert.Equal(before.Entries, after.Entries);
    }

    private static void Write(string directory, string name) => File.WriteAllText(Path.Combine(directory, name), name);

    private interface IBranchWatch
    {
        Task PrepareAsync();
        Task SetExpandedAsync(string path, bool expanded);
        Task<BranchState> ObserveAsync(string path, string[] entries, bool available);
        BranchState Capture(string path);
    }

    private sealed record BranchState(object Identity, long Read, bool Expanded, bool Available, string[] Entries);

    private sealed class WatchFiles : IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("retro-ide-explorer-watch-");

        public WatchFiles()
        {
            var first = _root.CreateSubdirectory("first");
            First = first.FullName;
            Changed = first.CreateSubdirectory("changed").FullName;
            Sibling = first.CreateSubdirectory("sibling").FullName;
            Other = _root.CreateSubdirectory("other").FullName;
            Write(Changed, "old.asm");
            Write(Sibling, "stable.asm");
            Write(Other, "other.asm");
        }

        public string First { get; }
        public string Changed { get; }
        public string Sibling { get; }
        public string Other { get; }
        public void Dispose() => _root.Delete(recursive: true);
    }

    private sealed class ReferenceWatch(WatchFiles files, string? fault = null) : IBranchWatch
    {
        private readonly Dictionary<string, BranchState> _branches = new(StringComparer.Ordinal);

        public Task PrepareAsync()
        {
            foreach (var path in new[] { files.Changed, files.Sibling, files.Other })
                _branches.Add(path, new BranchState(new object(), 1, Expanded: true, Available: true, ListFiles(path)));
            return Task.CompletedTask;
        }

        public Task SetExpandedAsync(string path, bool expanded)
        {
            Assert.Equal(files.First, path);
            foreach (var child in new[] { files.Changed, files.Sibling })
                _branches[child] = _branches[child] with { Expanded = expanded };
            return Task.CompletedTask;
        }

        public Task<BranchState> ObserveAsync(string path, string[] entries, bool available)
        {
            foreach (var branch in _branches.Keys.ToArray())
            {
                var state = _branches[branch];
                if (!state.Expanded && !Fault("read-collapsed"))
                    continue;
                var exists = Directory.Exists(branch);
                var observed = exists ? ListFiles(branch) : [];
                if (Fault("miss-create") && observed.Contains("created.asm", StringComparer.Ordinal) ||
                    Fault("miss-rename") && observed.Contains("renamed.asm", StringComparer.Ordinal) ||
                    Fault("miss-delete") && state.Entries.Contains("renamed.asm", StringComparer.Ordinal) ||
                    Fault("block-other-root") && !Directory.Exists(files.Other) && exists)
                    continue;
                if (exists != state.Available || !observed.SequenceEqual(state.Entries, StringComparer.Ordinal) ||
                    (Fault("read-unrelated") && entries.Contains("created.asm", StringComparer.Ordinal) && branch != path))
                    _branches[branch] = state with { Read = state.Read + 1, Available = exists, Entries = observed };
            }
            return Task.FromResult(Capture(path));
        }

        public BranchState Capture(string path) => _branches[path];
        private bool Fault(string name) => string.Equals(fault, name, StringComparison.Ordinal);
        private static string[] ListFiles(string path) => Directory.GetFiles(path).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToArray();
    }
}
