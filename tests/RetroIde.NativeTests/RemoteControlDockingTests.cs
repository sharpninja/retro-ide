using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.RemoteControl.Server;
using Avalonia.RemoteControl.Server.Rendering;
using Avalonia.RemoteControl.Server.Threading;
using Avalonia.VisualTree;
using Dock.Model.Core;
using Microsoft.Extensions.Options;
using RetroIde.Testing;
using RetroIde.Workbench;

namespace RetroIde.NativeTests;

internal static class RemoteControlDockingTests
{
    /// <summary>TEST-RETRO-001#verify-ac-behavior-desktop-1; TEST-RETRO-007#ac-test-system-wait-cursor</summary>
    public static async Task RunAsync(CancellationTokenSource finished)
    {
        try
        {
            await RemoteControlBrandingTests.VerifyAsync().ConfigureAwait(true);
            await RemoteControlWorkflowTests.RunAsync().ConfigureAwait(true);
            await RemoteControlLanguageEditorTests.RunAsync().ConfigureAwait(true);
            await RemoteControlLanguageEditorTests.RunPanesAsync().ConfigureAwait(true);
            await RemoteControlGitTests.RunAsync().ConfigureAwait(true);
            using var session = new WorkbenchSession(Guid.NewGuid());
            var window = new WorkbenchWindow(session);
            window.Show();
            try
            {
                await WorkbenchRemoteControl.ClickAsync(window, "workbench.view.focus.toolbar").ConfigureAwait(true);
                var pending = session.View.FocusCommand.ExecutionTask
                    ?? throw new InvalidOperationException("The RemoteControl toolbar action did not start the registered command.");
                await pending.ConfigureAwait(true);
                if (!session.Layout.IsFocused("registers") || session.View.WorkbenchRevision != 1)
                    throw new InvalidOperationException("The RemoteControl toolbar action did not focus Registers once.");

                await VerifyWaitLifecycleAsync(session, window, window).ConfigureAwait(true);
                var tool = session.Layout.Tool("registers");
                var owner = (IDock)tool.Owner!;
                var anchor = session.Layout.Tool("call-stack");
                session.Layout.Factory.FloatDockable(tool);
                var floating = session.Layout.Root.Windows?.SingleOrDefault()?.Host as Window
                    ?? throw new InvalidOperationException("Registers did not open a native floating host.");
                var primaryHandle = window.TryGetPlatformHandle()
                    ?? throw new InvalidOperationException("The workbench has no platform window handle.");
                var floatingHandle = floating.TryGetPlatformHandle()
                    ?? throw new InvalidOperationException("The floating Registers tool has no platform window handle.");
                if (floatingHandle.Handle == primaryHandle.Handle)
                    throw new InvalidOperationException("Registers did not receive a distinct platform window.");

                await VerifyWaitLifecycleAsync(session, floating, window).ConfigureAwait(true);
                session.Layout.Factory.MoveDockable((IDock)tool.Owner!, owner, tool, anchor);
                if (!ReferenceEquals(owner, tool.Owner) || !ReferenceEquals(session.Layout.Root, session.Layout.Factory.FindRoot(tool)))
                    throw new InvalidOperationException("Registers did not redock to its original group.");

                Console.WriteLine($"RemoteControl focused Registers once and verified a native Dock float and redock. Main: {primaryHandle.HandleDescriptor}; float: {floatingHandle.HandleDescriptor}. No desktop input was used.");
            }
            finally { window.Close(); }
        }
        finally { finished.Cancel(); }
    }

    private static async Task VerifyWaitLifecycleAsync(WorkbenchSession session, Window window, WorkbenchWindow workbench)
    {
        var tool = session.Layout.Tool("registers");
        using var layoutTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        Border? overlay;
        do
        {
            window.UpdateLayout();
            overlay = window.GetVisualDescendants().OfType<Border>()
                .SingleOrDefault(border => string.Equals(border.Name, "InteractionSurface", StringComparison.Ordinal) &&
                    ReferenceEquals(border.DataContext, tool) && border.GetVisualParent() is Grid { IsEffectivelyVisible: true });
            if (overlay is null)
                await Task.Delay(30, layoutTimeout.Token).ConfigureAwait(true);
        } while (overlay is null);

        var region = (Grid)overlay.GetVisualParent()!;
        var pane = (Control)region.Children[0];
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var background = session.Interaction.RunAsync(null, _ => release.Task);
        Task? blocked = null;
        try
        {
            if (overlay.IsVisible || overlay.Cursor is not null)
                throw new InvalidOperationException("Background work displayed a Wait cursor in Registers.");

            blocked = session.Interaction.RunAsync("registers", _ => release.Task);
            VerifyBlocked(overlay, pane);
            var deadline = TimeProvider.System.GetUtcNow() + TimeSpan.FromSeconds(4);
            while (!HitsOverlay(window, region, overlay))
            {
                if (TimeProvider.System.GetUtcNow() >= deadline)
                    throw new InvalidOperationException("The scoped Wait overlay was not the hit target over Registers.");
                await Task.Delay(30).ConfigureAwait(true);
            }

            var availableTarget = ReferenceEquals(window, workbench) ? "output" : "explorer";
            await VerifyAvailableCommandAsync(session, workbench, availableTarget, blocked).ConfigureAwait(true);
            var frames = new AvaloniaRenderTargetFrameProvider(
                Options.Create(new AvaloniaRemoteControlOptions { AllowRemoteFrames = true }),
                new AvaloniaUiThreadRemoteControlDispatcher());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var frame = await frames.CaptureFrameAsync(window, 1, timeout.Token).ConfigureAwait(true);
            if (frame is not { PixelWidth: > 0, PixelHeight: > 0, Png.Length: > 0 } || blocked.IsCompleted)
                throw new InvalidOperationException("The remote workbench did not render a frame during a pending block.");

            await CheckCancelledBlockAsync(session).ConfigureAwait(true);
            VerifyBlocked(overlay, pane);
            await CheckFailedBlockAsync(session).ConfigureAwait(true);
            VerifyBlocked(overlay, pane);
        }
        finally
        {
            release.TrySetResult();
            await background.ConfigureAwait(true);
            if (blocked is not null)
                await blocked.ConfigureAwait(true);
        }

        if (overlay.IsVisible || overlay.Cursor is not null || !pane.IsEffectivelyEnabled || session.Interaction.IsBlocked("registers"))
            throw new InvalidOperationException("Registers did not restore its unblocked cursor and input state.");

        Console.WriteLine($"RemoteControl rendered {window.TryGetPlatformHandle()?.HandleDescriptor} during a block, verified the Wait overlay and an available command, and exercised overlapping success, cancellation, and failure.");
    }

    private static bool HitsOverlay(Window window, Grid region, Border overlay)
    {
        var center = region.TranslatePoint(new Point(region.Bounds.Width / 2, region.Bounds.Height / 2), window);
        return center is { } position && ReferenceEquals(window.InputHitTest(position), overlay);
    }

    private static async Task VerifyAvailableCommandAsync(
        WorkbenchSession session, WorkbenchWindow workbench, string target, Task blocked)
    {
        var previousTarget = session.View.FocusTarget;
        var previousExecution = session.View.FocusCommand.ExecutionTask;
        var revision = session.View.WorkbenchRevision;
        session.View.FocusTarget = target;
        try
        {
            await WorkbenchRemoteControl.ClickAsync(workbench, "workbench.view.focus.toolbar").ConfigureAwait(true);
            var focus = session.View.FocusCommand.ExecutionTask;
            if (focus is null || ReferenceEquals(focus, previousExecution))
                throw new InvalidOperationException("The available toolbar command did not start a new operation.");
            await focus.ConfigureAwait(true);
            if (blocked.IsCompleted || session.View.WorkbenchRevision != revision + 1 || !session.Layout.IsFocused(target))
                throw new InvalidOperationException($"The available toolbar command did not focus {target} exactly once during the Registers block.");
            Console.WriteLine($"RemoteControl dispatched {target} during a pending Registers block, revision {revision} -> {session.View.WorkbenchRevision}.");
        }
        finally { session.View.FocusTarget = previousTarget; }
    }

    private static void VerifyBlocked(Border overlay, Control pane)
    {
        if (!overlay.IsVisible || pane.IsEffectivelyEnabled || !string.Equals(overlay.Cursor?.ToString(), "Wait", StringComparison.Ordinal))
            throw new InvalidOperationException("Registers did not expose its platform Wait cursor.");
    }

    private static async Task CheckCancelledBlockAsync(WorkbenchSession session)
    {
        using var cancel = new CancellationTokenSource();
        var operation = session.Interaction.RunAsync("registers", token => Task.Delay(Timeout.Infinite, token), cancel.Token);
        await cancel.CancelAsync().ConfigureAwait(true);
        try { await operation.ConfigureAwait(true); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return; }
        throw new InvalidOperationException("The overlapping Registers block did not cancel.");
    }

    private static async Task CheckFailedBlockAsync(WorkbenchSession session)
    {
        var expected = new InvalidOperationException("Fixture failure");
        try
        {
            await session.Interaction.RunAsync("registers", _ => Task.FromException(expected)).ConfigureAwait(true);
        }
        catch (InvalidOperationException actual) when (ReferenceEquals(actual, expected)) { return; }
        throw new InvalidOperationException("The overlapping Registers block did not propagate failure.");
    }
}
