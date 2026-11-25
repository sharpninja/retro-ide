using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using RetroIde.Application;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

/// <summary>Exercises shell-only layout and interaction lifecycles.</summary>
public sealed class ShellLifecycleTests
{
    /// <summary>TEST-RETRO-001#verify-ac-behavior-desktop-2</summary>
    [AvaloniaTheory]
    [InlineData("", "", Alignment.Right)]
    [InlineData("", "", Alignment.Unset)]
    [InlineData("right", "right", Alignment.Right)]
    [InlineData("right", "debug/tools #23", Alignment.Right)]
    [InlineData("right", "debug-tools-23", Alignment.Right)]
    public void Pinned_tool_roundtrips_its_surviving_dynamic_owner(string existingId, string dynamicId, Alignment alignment)
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var store = new LayoutStore(Path.Combine(folder.FullName, "layout.json"));
            var workspaceId = Guid.NewGuid();
            using (var before = new WorkbenchSession(workspaceId))
            {
                var factory = before.Layout.Factory;
                var existing = Assert.IsAssignableFrom<IToolDock>(before.Layout.Tool("registers").Owner);
                existing.Id = existingId;
                var split = factory.CreateToolDock();
                split.Id = dynamicId;
                split.Alignment = alignment;
                factory.SplitToDock(existing, split, DockOperation.Right);
                var output = before.Layout.Tool("output");
                var problems = before.Layout.Tool("problems");
                var source = Assert.IsAssignableFrom<IDock>(output.Owner);
                factory.MoveDockable(source, split, output, null);
                factory.MoveDockable(source, split, problems, output);
                Assert.Same(split, problems.Owner);
                factory.PinDockable(output);
                Assert.True(factory.IsDockablePinned(output));
                store.Save(before.Layout.Capture());
            }

            var saved = Assert.IsType<WorkbenchLayoutState>(store.Load());
            var pin = Assert.Single(saved.Main.Pinned);
            Assert.False(string.IsNullOrEmpty(pin.Group));
            if (string.Equals(dynamicId, "debug-tools-23", StringComparison.Ordinal))
                Assert.Equal(dynamicId, pin.Group);
            if (string.Equals(dynamicId, "debug/tools #23", StringComparison.Ordinal))
                Assert.DoesNotContain(dynamicId, File.ReadAllText(store.Path), StringComparison.Ordinal);
            using var after = new WorkbenchSession(workspaceId, saved);
            var restored = after.Layout.Tool("output");
            var splitOwner = after.Layout.Tool("problems").Owner;
            Assert.Same(splitOwner, restored.OriginalOwner);
            Assert.Equal(pin, Assert.Single(after.Layout.Capture().Main.Pinned));
            after.Layout.Factory.UnpinDockable(restored);
            Assert.Same(splitOwner, restored.Owner);
        }
        finally { folder.Delete(recursive: true); }
    }

    /// <summary>TEST-RETRO-001#verify-ac-behavior-desktop-2; TEST-RETRO-001#verify-ac-contract-desktop-2; TEST-RETRO-007#ac-test-cqrs-unit-of-work</summary>
    [AvaloniaFact]
    public async Task Layout_reopens_with_tool_and_document_selection_while_session_survives()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var store = new LayoutStore(Path.Combine(directory.FullName, "workbench.json"));
            var adapter = new RunningAdapterFixture(Guid.NewGuid());
            var original = adapter.Advance();
            using (var before = adapter.OpenWorkbench())
            {
                Assert.Same(adapter, before.Layout.Root.Context);
                Assert.Equal(adapter.WorkspaceId, before.View.WorkspaceId);
                var actor = new ActorGrant("fixture-query", adapter.WorkspaceId, before.View.ViewKey, canMutate: false);
                var address = before.View.Address;
                var read = await before.Controller.DispatchAsync(before.Registry.GetContext, new GetContextInput(),
                    null, actor, Guid.NewGuid(), default);
                Assert.Equal("ok", read.Code);
                Assert.Equal(original, adapter.State);
                Assert.Equal(["launch", "advance", "attach"], adapter.Effects);
                Assert.Equal(address, before.View.Address);
                var window = new WorkbenchWindow(before, store);
                window.Show();
                before.Layout.SelectDocument("emulation");
                before.Layout.MoveToolToGroup("registers", "bottom");
                before.Layout.Factory.PinDockable(before.Layout.Tool("call-stack"));
                before.Layout.Factory.FloatDockable(before.Layout.Tool("serial"));
                var floatingWindow = Assert.IsAssignableFrom<Window>(Assert.Single(before.Layout.Root.Windows!).Host);
                Assert.True(before.Layout.Focus("registers"));
                Assert.True(before.Layout.Factory.IsDockablePinned(before.Layout.Tool("call-stack")));
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var closes = 0;
                window.Closed += (_, _) => { closes++; closed.SetResult(); };
                window.Close();
                Assert.True(window.IsVisible);
                Assert.True(before.Interaction.IsBlocked("registers"));
                Assert.False(floatingWindow.IsEffectivelyEnabled);
                floatingWindow.Close();
                Assert.True(floatingWindow.IsVisible);
                window.Close();
                Assert.True(window.IsVisible);
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(1, closes);
                Assert.False(before.Interaction.IsBlocked("registers"));
            }
            var persisted = await File.ReadAllTextAsync(store.Path);
            Assert.Equal(original, adapter.State);
            var whileClosed = adapter.Advance();
            Assert.DoesNotContain(original.Id.ToString(), persisted, StringComparison.Ordinal);
            Assert.DoesNotContain(adapter.WorkspaceId.ToString(), persisted, StringComparison.Ordinal);
            Assert.DoesNotContain(directory.FullName, persisted, StringComparison.Ordinal);
            var layout = Assert.IsType<WorkbenchLayoutState>(store.Load());
            using var after = adapter.OpenWorkbench(layout);
            Assert.Equal("emulation", after.Layout.SelectedDocumentId);
            Assert.Equal("bottom", after.Layout.Tool("registers").Owner?.Id);
            Assert.Equal("registers", after.View.ActiveToolId);
            Assert.True(after.Layout.Factory.IsDockablePinned(after.Layout.Tool("call-stack")));
            var reopened = new WorkbenchWindow(after);
            reopened.Show();
            try
            {
                Assert.Equal(adapter.WorkspaceId, after.View.WorkspaceId);
                Assert.Same(adapter, after.Layout.Root.Context);
                Assert.Equal(whileClosed, adapter.State);
                Assert.Equal(whileClosed with { Revision = whileClosed.Revision + 1 }, adapter.Advance());
                Assert.Equal(["launch", "advance", "attach", "advance", "attach", "advance"], adapter.Effects);
            }
            finally { reopened.Close(); }
        }
        finally { directory.Delete(recursive: true); }
    }

    /// <summary>TEST-RETRO-001#verify-ac-behavior-desktop-2</summary>
    [AvaloniaFact]
    public async Task Startup_restores_main_pins_when_remaining_tools_are_floating()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var store = new LayoutStore(Path.Combine(directory.FullName, "layout.json"));
            var saved = new WorkbenchLayoutState(1,
                new DockBranch(new DockNode("document-tabs", "documents", Active: "emulation", Children:
                    [new("document", "start"), new("document", "emulation"), new("document", "disassembly"), new("document", "test-results")]),
                    [new("explorer", "Left", "left"), new("call-stack", "Right", "right")]),
                [new DockBranch(new DockNode("tool-tabs", "right", Active: "registers", Children:
                    [new("tool", "registers"), new("tool", "output"), new("tool", "problems"), new("tool", "serial"),
                     new("tool", "breakpoints"), new("tool", "mcp-activity")], Alignment: "Right"), [])], "registers");
            store.Save(saved);

            using var session = new WorkbenchSession(Guid.NewGuid(), Assert.IsType<WorkbenchLayoutState>(store.Load()));
            var window = new WorkbenchWindow(session);
            window.Show();
            try
            {
                Assert.Equal("emulation", session.Layout.SelectedDocumentId);
                Assert.Contains(session.Layout.Tool("explorer"), session.Layout.Root.LeftPinnedDockables!);
                Assert.Contains(session.Layout.Tool("call-stack"), session.Layout.Root.RightPinnedDockables!);
                Assert.Single(session.Layout.Root.Windows!);
                Assert.True(session.Layout.IsFocused("registers"));
                Assert.Equal("document-tabs", session.Layout.Capture().Main.Content.Kind);

                var floatHost = Assert.IsAssignableFrom<Window>(Assert.Single(session.Layout.Root.Windows!).Host);
                floatHost.Close();
                Assert.Empty(session.Layout.Root.Windows!);
                var closingLayout = session.Layout.Capture();
                Assert.Equal("emulation", session.Layout.SelectedDocumentId);
                using (var immediatelyReopened = new WorkbenchSession(Guid.NewGuid(), closingLayout))
                {
                    Assert.Equal("emulation", immediatelyReopened.Layout.SelectedDocumentId);
                    Assert.Contains(immediatelyReopened.Layout.Tool("explorer"), immediatelyReopened.Layout.Root.LeftPinnedDockables!);
                }

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                Assert.Equal("emulation", session.Layout.SelectedDocumentId);
                var found = new[] { "registers", "output", "problems", "serial", "breakpoints", "mcp-activity" };
                Assert.All(found, id => Assert.Same(session.Layout.Root, session.Layout.Factory.FindRoot(session.Layout.Tool(id))));
                Assert.True(session.Layout.Focus("registers"));

                // With no original owner on this host, Dock creates an aligned destination on unpin.
                var explorer = session.Layout.Tool("explorer");
                session.Layout.Factory.UnpinDockable(explorer);
                Assert.False(session.Layout.Factory.IsDockablePinned(explorer));
                Assert.Same(session.Layout.Root, session.Layout.Factory.FindRoot(explorer));
                Assert.Equal(Alignment.Left, Assert.IsAssignableFrom<IToolDock>(explorer.Owner).Alignment);
            }
            finally { window.Close(); }

            store.Save(saved with { Main = saved.Main with { Pinned = [new("explorer", "Invalid"), saved.Main.Pinned[1]] } });
            var rejected = Assert.IsType<WorkbenchLayoutState>(store.Load());
            using var fallback = new WorkbenchSession(Guid.NewGuid(), rejected);
            Assert.Equal("start", fallback.Layout.SelectedDocumentId);
            Assert.Equal("left", fallback.Layout.Tool("explorer").Owner?.Id);
            Assert.Empty(fallback.Layout.Capture().Floating);
            Assert.Equal("Invalid", store.Load()!.Main.Pinned[0].Alignment);
        }
        finally { directory.Delete(recursive: true); }
    }

    /// <summary>TEST-RETRO-001#verify-ac-behavior-desktop-2</summary>
    [AvaloniaFact]
    public void Closing_a_saved_floating_document_preserves_it_in_the_main_layout()
    {
        const string documents = "document-tabs";
        var state = new WorkbenchLayoutState(1,
            new DockBranch(new DockNode(documents, "documents", Active: "start", Children:
                [new("document", "start"), new("document", "disassembly"), new("document", "test-results")]),
                [new("explorer", "Left"), new("registers", "Right"), new("call-stack", "Right"),
                 new("output", "Bottom"), new("problems", "Bottom"), new("serial", "Bottom"),
                 new("breakpoints", "Bottom"), new("mcp-activity", "Bottom")]),
            [new DockBranch(new DockNode(documents, "floating-documents", Active: "emulation",
                Children: [new("document", "emulation")]), [])]);
        using var session = new WorkbenchSession(Guid.NewGuid(), state);
        var window = new WorkbenchWindow(session);
        window.Show();

        try
        {
            var floating = Assert.IsAssignableFrom<Window>(Assert.Single(session.Layout.Root.Windows!).Host);
            floating.Close();
            var captured = session.Layout.Capture();
            Assert.Empty(captured.Floating);
            Assert.Contains(captured.Main.Content.Children!, item => item.Id == "emulation");
        }
        finally { window.Close(); }
    }

    /// <summary>TEST-RETRO-001#verify-ac-behavior-desktop-2; TEST-RETRO-007#ac-test-system-wait-cursor</summary>
    [AvaloniaFact]
    public async Task Close_finishes_and_clears_its_block_when_layout_save_fails()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            // Replacing a directory with the layout file must fail without losing the existing path.
            var reserved = Directory.CreateDirectory(Path.Combine(directory.FullName, "layout.json"));
            using var session = new WorkbenchSession(Guid.NewGuid());
            var window = new WorkbenchWindow(session, new LayoutStore(reserved.FullName));
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.SetResult();
            window.Show();
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(session.Interaction.IsBlocked("registers"));
            Assert.False(session.View.FocusCommand.CanExecute(null));
            Assert.Equal(reserved.FullName, Assert.Single(directory.EnumerateFileSystemInfos()).FullName);
        }
        finally { directory.Delete(recursive: true); }
    }

    /// <summary>TEST-RETRO-007#ac-test-system-wait-cursor</summary>
    [AvaloniaTheory]
    [InlineData("registers")]
    [InlineData(null)]
    public async Task Synchronous_operation_yields_the_dispatcher(string? scope)
    {
        using var session = new WorkbenchSession(Guid.NewGuid());
        using var release = new ManualResetEventSlim();
        var occupied = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = session.Interaction.RunAsync(scope, _ =>
        {
            occupied.SetResult(Dispatcher.UIThread.CheckAccess());
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)), "Synchronous operation timed out while waiting for UI dispatch.");
            return Task.CompletedTask;
        });
        try
        {
            Assert.False(await occupied.Task.WaitAsync(TimeSpan.FromSeconds(5)), "Operation entered on the UI dispatcher.");
            var observed = await Dispatcher.UIThread.InvokeAsync(() =>
                (session.Interaction.IsBlocked("registers"), session.Interaction.IsBlocked("output"), operation.IsCompleted),
                DispatcherPriority.Background);
            Assert.Equal((scope is not null, false, false), observed);
        }
        finally
        {
            release.Set();
            try { await operation; }
            catch (Xunit.Sdk.XunitException) when (occupied.Task.IsCompletedSuccessfully && occupied.Task.Result) { }
        }
        Assert.False(session.Interaction.IsBlocked("registers"));
    }

    /// <summary>TEST-RETRO-007#ac-test-system-wait-cursor</summary>
    [AvaloniaFact]
    public async Task Interaction_blocks_overlap_and_clear_after_each_outcome()
    {
        using var session = new WorkbenchSession(Guid.NewGuid());
        var region = new Border { Cursor = new Cursor(StandardCursorType.Ibeam) };
        var unrelated = new Border { Cursor = new Cursor(StandardCursorType.Hand) };
        var original = region.Cursor;
        using var surface = session.Interaction.Attach("registers", region);
        using var other = session.Interaction.Attach("output", unrelated);
        using var cancelled = new CancellationTokenSource();
        var open = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = session.Interaction.RunAsync("registers", _ => open.Task);
        var second = session.Interaction.RunAsync("registers", token => ready.Task.WaitAsync(token), cancelled.Token);
        Assert.Equal("Wait", region.Cursor?.ToString());
        Assert.Equal("Hand", unrelated.Cursor?.ToString());
        open.SetResult();
        await first;
        Assert.Equal("Wait", region.Cursor?.ToString());
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.Same(original, region.Cursor);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Interaction.RunAsync("registers", _ => throw new InvalidOperationException()));
        Assert.Same(original, region.Cursor);
    }

    private sealed record SessionState(Guid Id, string Status, int Revision);

    // Test composition binds a running adapter to the workspace, independent of the window lifetime.
    private sealed class RunningAdapterFixture(Guid workspaceId) : IWorkbenchWorkspaceContext
    {
        public Guid WorkspaceId { get; } = workspaceId;
        public SessionState State { get; private set; } = new(Guid.NewGuid(), "running", 42);
        public List<string> Effects { get; } = ["launch"];

        public WorkbenchSession OpenWorkbench(WorkbenchLayoutState? layout = null)
        {
            Effects.Add("attach");
            return new WorkbenchSession(this, layout);
        }

        public SessionState Advance()
        {
            Effects.Add("advance");
            State = State with { Revision = State.Revision + 1 };
            return State;
        }
    }
}
