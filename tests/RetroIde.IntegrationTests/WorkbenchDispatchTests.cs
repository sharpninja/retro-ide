using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

/// <summary>Exercises real commands, Dock controls, and registered views.</summary>
public sealed class WorkbenchDispatchTests
{
    /// <summary>TEST-RETRO-007#ac-test-simplemvc-views; TEST-RETRO-015#ac-test-mcp-scope</summary>
    [AvaloniaFact]
    public void Foreign_workspace_cannot_register_with_the_workbench_controller()
    {
        using var session = new WorkbenchSession(Guid.NewGuid());
        var foreign = new WorkbenchViewModel(Guid.NewGuid()) { FocusTarget = "output" };

        Assert.Throws<ArgumentException>(() => session.Controller.Register(foreign));
        Assert.Null(session.Controller.GetView<IView>(foreign.ViewKey));
        Assert.False(foreign.RegisteredOnce);
        Assert.False(foreign.FocusCommand.CanExecute(null));
        Assert.Equal(0, session.View.WorkbenchRevision);
    }

    /// <summary>TEST-RETRO-007#ac-test-toolkit-bindings; TEST-RETRO-008#ac-test-gesture-parity</summary>
    [AvaloniaFact]
    public async Task Menu_toolbar_and_shortcut_share_one_eligible_focus_command()
    {
        using var session = new WorkbenchSession(Guid.NewGuid());
        var window = new WorkbenchWindow(session);
        window.Show();
        try
        {
            var command = Assert.IsType<AsyncRelayCommand>(window.FocusButton.Command);
            Assert.Same(command, window.FocusMenu.Command);
            Assert.Same(command, window.FocusShortcut.Command);
            Assert.Equal(window.FocusMenu.InputGesture, window.FocusShortcut.Gesture);
            Assert.Equal(window.CommandPresentation.Label, window.FocusMenu.Header);
            Assert.Equal(window.CommandPresentation.Label, window.FocusButtonLabel.Text);
            Assert.Equal(window.CommandPresentation.Shortcut, window.FocusButtonShortcut.Text);
            Assert.NotSame(window.FocusMenu.Icon, window.FocusButtonIcon);
            Assert.Equal(window.CommandPresentation.IconGeometry, window.FocusButtonIcon.Data);
            var gestureError = Assert.Throws<InvalidOperationException>(() =>
                WorkbenchWindow.RegisterShortcut(window, command, window.CommandPresentation.Gesture));
            Assert.Same(window.FocusShortcut, Assert.Single(window.KeyBindings, binding => Equals(binding.Gesture, window.CommandPresentation.Gesture)));
            Assert.Contains("shortcut", gestureError.Message, StringComparison.OrdinalIgnoreCase);

            session.View.FocusTarget = "missing";
            Assert.False(command.CanExecute(null));
            Assert.False(window.FocusMenu.IsEffectivelyEnabled);
            Assert.False(window.FocusButton.IsEffectivelyEnabled);
            window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.Equal(0, session.Controller.DispatchCount);
            Assert.Equal(0, session.View.WorkbenchRevision);
            session.View.FocusTarget = "registers";
            Assert.True(command.CanExecute(null));
            Assert.True(window.FocusMenu.IsEffectivelyEnabled);
            Assert.True(window.FocusButton.IsEffectivelyEnabled);

            var subscribers = typeof(WorkbenchViewModel).GetField(nameof(WorkbenchViewModel.CommandRequested),
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.Single(Assert.IsType<EventHandler<ViewCommandRequested>>(subscribers.GetValue(session.View)).GetInvocationList());
            session.Controller.Register(session.View);
            Assert.Single(Assert.IsType<EventHandler<ViewCommandRequested>>(subscribers.GetValue(session.View)).GetInvocationList());
            await WorkbenchRemoteControl.ClickAsync(window, "workbench.view.focus.toolbar");
            await Assert.IsAssignableFrom<Task>(command.ExecutionTask);
            Assert.Equal("registers", session.View.ActiveToolId);
            Assert.True(session.Layout.IsFocused("registers"));
            Assert.True(session.Layout.Tool("registers").IsActive);
            Assert.Equal(1, session.View.WorkbenchRevision);
            Assert.Equal(1, session.Controller.DispatchCount);

            session.Layout.Focus("output");
            var menu = window.GetVisualDescendants().OfType<MenuItem>()
                .Single(item => Equals(item.Header, "_View"));
            menu.IsSubMenuOpen = true;
            await WorkbenchRemoteControl.ClickAsync(window, "workbench.view.focus.menu");
            await Assert.IsAssignableFrom<Task>(command.ExecutionTask);
            Assert.True(session.Layout.IsFocused("registers"));
            Assert.Equal(2, session.Controller.DispatchCount);

            session.Layout.Focus("output");
            window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control | RawInputModifiers.Shift);
            await Assert.IsAssignableFrom<Task>(command.ExecutionTask);
            Assert.True(session.Layout.IsFocused("registers"));
            Assert.Equal(3, session.Controller.DispatchCount);
        }
        finally { window.Close(); }
    }

    /// <summary>TEST-RETRO-007#ac-test-simplemvc-views; TEST-RETRO-007#ac-test-view-result-routing</summary>
    [AvaloniaFact]
    public async Task Completion_is_correlated_to_its_live_origin_even_if_focus_changes()
    {
        using var session = new WorkbenchSession(Guid.NewGuid());
        var other = new WorkbenchViewModel(session.View.WorkspaceId);
        session.Controller.Register(other);
        var completions = new List<OperationResult>();
        Guid requestId = default;
        session.View.CommandRequested += (_, request) => requestId = request.RequestId;
        session.View.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(WorkbenchViewModel.LastResult), StringComparison.Ordinal))
            {
                Assert.True(Dispatcher.UIThread.CheckAccess());
                completions.Add(Assert.IsType<OperationResult>(session.View.LastResult));
            }
        };
        session.View.FocusTarget = "registers";
        session.View.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(WorkbenchViewModel.WorkbenchRevision), StringComparison.Ordinal))
                Assert.Equal(session.Layout.ActiveToolId, session.View.Address.ActiveToolId);
            if (string.Equals(args.PropertyName, nameof(WorkbenchViewModel.ActiveToolId), StringComparison.Ordinal) &&
                string.Equals(session.View.ActiveToolId, "registers", StringComparison.Ordinal))
                Dispatcher.UIThread.Post(() => session.Layout.Focus("output"), DispatcherPriority.Normal);
        };
        var first = session.View.FocusCommand.ExecuteAsync(null);
        Assert.True(session.View.FocusCommand.IsRunning);
        await first;
        Assert.Equal("output", session.View.ActiveToolId);
        Assert.Single(completions);
        Assert.Equal(requestId, completions[0].OperationId);
        Assert.Equal("ok", completions[0].Code);
        Assert.Null(other.LastResult);

        var pending = session.View.FocusCommand.ExecuteAsync(null);
        session.Controller.Unregister(session.View);
        await pending;
        Assert.Single(completions);
        Assert.Same(other, session.Controller.GetView<IView>(other.ViewKey));
        Assert.False(session.View.FocusCommand.CanExecute(null));

        var replacement = new WorkbenchViewModel(other.WorkspaceId);
        session.Controller.Register(replacement);
        Assert.NotEqual(session.View.ViewKey, replacement.ViewKey);
        Assert.Throws<InvalidOperationException>(() => session.Controller.Register(session.View));

        replacement.FocusTarget = "call-stack";
        replacement.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(WorkbenchViewModel.ActiveToolId), StringComparison.Ordinal))
                Dispatcher.UIThread.Post(() => session.Controller.Unregister(replacement), DispatcherPriority.Normal);
        };
        await replacement.FocusCommand.ExecuteAsync(null);
        Assert.True(session.Layout.IsFocused("call-stack"));
        Assert.Null(replacement.LastResult);
    }

    /// <summary>TEST-RETRO-007#ac-test-view-result-routing</summary>
    [AvaloniaFact]
    public async Task Stale_focus_failure_reaches_the_origin_once_on_the_ui_thread()
    {
        using var session = new WorkbenchSession(Guid.NewGuid());
        var other = new WorkbenchViewModel(session.View.WorkspaceId);
        session.Controller.Register(other);
        var window = new WorkbenchWindow(session);
        var alternateButton = new Button { Content = "Other view", Command = other.FocusCommand };
        var alternateWindow = new Window { DataContext = other, Content = alternateButton };
        window.Show();
        try
        {
            var completions = new List<OperationResult>();
            Guid requestId = default;
            session.View.FocusTarget = "registers";
            session.View.PropertyChanged += (_, args) =>
            {
                if (string.Equals(args.PropertyName, nameof(WorkbenchViewModel.LastResult), StringComparison.Ordinal))
                {
                    Assert.True(Dispatcher.UIThread.CheckAccess());
                    completions.Add(Assert.IsType<OperationResult>(session.View.LastResult));
                }
            };
            other.FocusTarget = "output";
            session.View.CommandRequested += (_, request) =>
            {
                requestId = request.RequestId;
                Assert.True(session.Layout.Focus("output"));
                alternateWindow.Show();
                alternateWindow.Activate();
                Assert.True(alternateButton.Focus());
                Assert.True(alternateButton.IsFocused);
            };

            await WorkbenchRemoteControl.ClickAsync(window, "workbench.view.focus.toolbar");
            await Assert.IsAssignableFrom<Task>(session.View.FocusCommand.ExecutionTask);
            var failure = Assert.Single(completions);
            Assert.NotEqual(Guid.Empty, requestId);
            Assert.Equal(requestId, failure.OperationId);
            Assert.Equal("stale_revision", failure.Code);
            Assert.True(session.Layout.IsFocused("output"));
            Assert.Null(other.LastResult);
        }
        finally
        {
            alternateWindow.Close();
            window.Close();
        }
    }

    /// <summary>TEST-RETRO-007#ac-test-cqrs-unit-of-work; TEST-RETRO-015#ac-test-mcp-viewmodel-context</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reentrant_focus_change_cannot_publish_an_older_context(bool returnToOriginalTarget)
    {
        using var session = new WorkbenchSession(Guid.NewGuid());
        var changed = false;
        session.View.PropertyChanging += (_, args) =>
        {
            if (changed || !string.Equals(args.PropertyName, nameof(WorkbenchViewModel.WorkbenchRevision), StringComparison.Ordinal))
                return;

            changed = true;
            Assert.True(session.Layout.Focus("output"));
            if (returnToOriginalTarget)
                Assert.True(session.Layout.Focus("registers"));
        };

        session.View.FocusTarget = "registers";
        await session.View.FocusCommand.ExecuteAsync(null);
        Assert.True(changed);
        Assert.Equal(returnToOriginalTarget ? 3 : 2, session.View.WorkbenchRevision);
        var result = Assert.IsType<OperationResult>(session.View.LastResult);
        Assert.Equal("tool_unavailable", result.Code);
        Assert.Null(result.Context);
        Assert.Equal(session.Layout.Revision, session.View.Address.WorkbenchRevision);
        var currentTool = returnToOriginalTarget ? "registers" : "output";
        Assert.Equal(currentTool, session.View.Address.ActiveToolId);
        Assert.True(session.Layout.IsFocused(currentTool));
    }

    /// <summary>TEST-RETRO-015#ac-test-mcp-viewmodel-context</summary>
    [AvaloniaFact]
    public void Workbench_rejects_empty_workspace_identity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkbenchViewModel(Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkbenchSession(Guid.Empty));
    }

    /// <summary>TEST-RETRO-007#ac-test-cqrs-unit-of-work; TEST-RETRO-007#ac-test-toolkit-bindings</summary>
    [AvaloniaFact]
    public async Task Cancellation_and_stale_context_produce_correlated_outcomes_without_mutation()
    {
        using var session = new WorkbenchSession(Guid.NewGuid());
        session.View.FocusTarget = "registers";
        Guid requestId = default;
        session.View.CommandRequested += (_, request) => requestId = request.RequestId;
        var pending = session.View.FocusCommand.ExecuteAsync(null);
        Assert.True(session.View.FocusCommand.IsRunning);
        session.View.FocusCommand.Cancel();
        await pending;
        Assert.False(session.View.FocusCommand.IsRunning);
        Assert.NotEqual(Guid.Empty, requestId);
        Assert.Equal(requestId, session.View.LastResult?.OperationId);
        Assert.Equal("cancelled", session.View.LastResult?.Code);
        Assert.Equal(0, session.View.WorkbenchRevision);

        var grant = new ActorGrant("fixture", session.View.WorkspaceId, session.View.ViewKey, canMutate: true);
        var captured = session.View.Address;
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = session.Controller.DispatchAsync(session.Registry.FocusTool, new FocusToolInput("registers"), captured, grant, firstId, default);
        var second = session.Controller.DispatchAsync(session.Registry.FocusTool, new FocusToolInput("output"), captured, grant, secondId, default);
        var racing = await Task.WhenAll(first, second);
        Assert.Equal([firstId, secondId], racing.Select(x => x.OperationId));
        Assert.Equal(["ok", "stale_revision"], racing.Select(x => x.Code), StringComparer.Ordinal);
        Assert.Equal(1, session.View.WorkbenchRevision);
        Assert.True(session.Layout.IsFocused("registers"));

        var queuedId = Guid.NewGuid();
        var queued = session.Controller.DispatchAsync(session.Registry.FocusTool, new FocusToolInput("output"),
            session.View.Address, grant, queuedId, default);
        grant.Revoke();
        var rejected = await queued;
        Assert.Equal(queuedId, rejected.OperationId);
        Assert.Equal("unauthorized", rejected.Code);
        Assert.Equal(1, session.View.WorkbenchRevision);
    }
}
