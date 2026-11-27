using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using RetroIde.Workbench;

namespace RetroIde.NativeTests;

internal static class NativeDockingTests
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args is ["--remote-control-desktop"])
                return RemoteControlDesktop.Run();
            var brandingOnly = args is ["--remote-control-branding"];
            var languageOnly = args is ["--remote-control-language-editor"];
            if (args is not ([] or ["--remote-control"]) && !brandingOnly && !languageOnly)
                throw new ArgumentException("Use --remote-control to run without desktop keyboard or mouse input.");

            var remoteControl = args.Length != 0;
            var app = remoteControl ? RemoteControlDesktop.BuildApp() : AppBuilder.Configure<WorkbenchApp>().UsePlatformDetect();
            app.SetupWithoutStarting();
            using var finished = new CancellationTokenSource();
            var result = brandingOnly ? RemoteControlBrandingTests.RunAsync(finished) :
                languageOnly ? RunLanguageEditorAsync(finished) :
                remoteControl ? RemoteControlDockingTests.RunAsync(finished) : RunAsync(finished);
            Dispatcher.UIThread.MainLoop(finished.Token);
            result.GetAwaiter().GetResult();
            if (!brandingOnly && !languageOnly)
                Task.Run(() => ProductionAttachmentTests.RunAsync(useNativeShortcut: !remoteControl)).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task RunLanguageEditorAsync(CancellationTokenSource finished)
    {
        try
        {
            await RemoteControlLanguageEditorTests.RunAsync().ConfigureAwait(true);
            await RemoteControlLanguageEditorTests.RunPanesAsync().ConfigureAwait(true);
            await RemoteControlLanguageEditorTests.RunServerEditsAsync().ConfigureAwait(true);
        }
        finally { finished.Cancel(); }
    }

    /// <summary>
    /// TEST-RETRO-001#verify-ac-behavior-desktop-1
    /// TEST-RETRO-001#verify-ac-contract-desktop-1
    /// TEST-RETRO-007#ac-test-system-wait-cursor
    /// TEST-RETRO-008#ac-test-gesture-parity
    /// </summary>
    private static async Task RunAsync(CancellationTokenSource finished)
    {
        try
        {
            using var input = new NativeInput();
            using var session = new WorkbenchSession(Guid.NewGuid());
            var window = new WorkbenchWindow(session) { Topmost = OperatingSystem.IsWindows() };
            var workArea = FitTestWindow(window);
            window.Show();
            try
            {
                await UntilAsync(() => window.IsVisible && window.FocusButton.Bounds.Width > 0, "main workbench").ConfigureAwait(true);
                Console.WriteLine($"Native backend: {window.TryGetPlatformHandle()?.HandleDescriptor}; input: {input.Backend}");
                await InvokeWorkbenchBindingsAsync(input, window, session).ConfigureAwait(true);
                await DragToolAsync(input, window, session, workArea).ConfigureAwait(true);
            }
            finally { window.Close(); }
        }
        finally { await finished.CancelAsync(); }
    }

    private static PixelRect FitTestWindow(Window window)
    {
        var screen = window.Screens.Primary ?? window.Screens.All.FirstOrDefault()
            ?? throw new InvalidOperationException("The native test requires a screen.");
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        // Reserve room for the native frame and a pointer target beyond the right edge.
        var width = Math.Min(930, Math.Floor(area.Width / scale) - 40);
        var height = Math.Min(630, Math.Floor(area.Height / scale) - 64);
        Require(width >= window.MinWidth && height >= window.MinHeight,
            $"The desktop work area {area} at scale {scale} is too small for the native docking test.");
        window.Width = width;
        window.Height = height;
        var inset = (int)Math.Ceiling(8 * scale);
        window.Position = new PixelPoint(area.X + inset, area.Y + inset);
        return area;
    }

    private static async Task InvokeWorkbenchBindingsAsync(NativeInput input, WorkbenchWindow window, WorkbenchSession session)
    {
        var toolbar = Center(window.FocusButton);
        if (OperatingSystem.IsWindows())
        {
            input.FocusWindow(window);
            await UntilAsync(() => window.IsActive && NativeInput.IsWin32WindowAt(toolbar, window),
                "foreground workbench toolbar in the interactive desktop").ConfigureAwait(true);
        }
        await input.ClickAsync(toolbar).ConfigureAwait(true);
        await UntilAsync(() => session.Controller.DispatchCount == 1 && session.Layout.IsFocused("registers"), "toolbar focus through native input",
            () => $"Window active {window.IsActive}, button hovered {window.FocusButton.IsPointerOver}, " +
                $"dispatches {session.Controller.DispatchCount}, revision {session.View.WorkbenchRevision}.").ConfigureAwait(true);
        await UntilAsync(() => !session.View.FocusCommand.IsRunning, "toolbar completion").ConfigureAwait(true);
        session.Layout.Focus("output");
        input.FocusWindow(window);
        await input.ShortcutAsync().ConfigureAwait(true);
        await UntilAsync(() => session.Controller.DispatchCount == 2 && session.Layout.IsFocused("registers") && !session.View.FocusCommand.IsRunning, "shortcut focus through native input").ConfigureAwait(true);

        session.Layout.Focus("output");
        var menu = window.GetVisualDescendants().OfType<Menu>().Single();
        var viewMenu = menu.GetVisualDescendants().OfType<MenuItem>().Single(item => Equals(item.Header, "_View"));
        await input.ClickAsync(Center(viewMenu)).ConfigureAwait(true);
        await UntilAsync(() => window.FocusMenu.IsVisible && window.FocusMenu.Bounds.Width > 0, "open View menu").ConfigureAwait(true);
        await input.ClickAsync(Center(window.FocusMenu)).ConfigureAwait(true);
        await UntilAsync(() => session.Controller.DispatchCount == 3 && session.Layout.IsFocused("registers") && !session.View.FocusCommand.IsRunning, "menu focus through native input").ConfigureAwait(true);
        await UntilAsync(() => !menu.IsOpen && !viewMenu.IsSubMenuOpen, "View menu dismissal").ConfigureAwait(true);
        if (OperatingSystem.IsWindows())
        {
            input.FocusWindow(window);
            await UntilAsync(() => window.IsActive, "workbench activation after View menu dismissal").ConfigureAwait(true);
        }
    }

    private static async Task DragToolAsync(NativeInput input, WorkbenchWindow window, WorkbenchSession session, PixelRect workArea)
    {
        var registers = session.Layout.Tool("registers");
        var callStack = session.Layout.Tool("call-stack");
        await SelectCallStackAsync(input, window, registers, callStack, workArea).ConfigureAwait(true);
        await input.ClickAsync(Tab(window, registers)).ConfigureAwait(true);
        await VerifyWaitAsync(session, window, input).ConfigureAwait(true);

        var start = Tab(window, registers);
        var outside = window.PointToScreen(new Point(window.ClientSize.Width + 95, 145));
        var edge = new PixelPoint(Math.Min(outside.X, workArea.Right - 4), outside.Y);
        Require(workArea.ContainsExclusive(edge) && edge.X >= window.PointToScreen(new Point(window.ClientSize.Width + 12, 145)).X,
            $"No reachable tear-away point beside the workbench at {window.Position} in work area {workArea}.");
        await input.DragStartAsync(start).ConfigureAwait(true);
        await input.MoveAsync(edge).ConfigureAwait(true);
        await input.ReleaseAsync().ConfigureAwait(true);

        await UntilAsync(() => session.Layout.Root.Windows is [{ Host: HostWindow { IsVisible: true } }], "native tear-away").ConfigureAwait(true);
        var floating = (HostWindow)session.Layout.Root.Windows!.Single().Host!;
        floating.Topmost = window.Topmost;
        Require(floating.TryGetPlatformHandle() is not null, "Floating pane needs a native handle.");
        Require(floating.TryGetPlatformHandle()!.Handle != window.TryGetPlatformHandle()!.Handle, "Floating pane must use a distinct top-level window.");
        await VerifyWaitAsync(session, floating, input).ConfigureAwait(true);
        await VerifyRestoredFloatAsync(session, window).ConfigureAwait(true);

        var targetTab = FindTab(window, callStack);
        var rightPane = targetTab.FindAncestorOfType<ToolControl>() ?? throw new InvalidOperationException("Missing dock target.");
        var target = Center(rightPane);
        input.FocusWindow(floating);
        await input.DragStartAsync(Tab(floating, registers)).ConfigureAwait(true);
        await input.MoveAsync(target).ConfigureAwait(true);
        await input.MoveAsync(new PixelPoint(target.X + 1, target.Y)).ConfigureAwait(true);
        await input.ReleaseAsync().ConfigureAwait(true);
        await UntilAsync(() => ReferenceEquals(registers.Owner, callStack.Owner) && session.Layout.Root.Windows?.Count is 0, "redock into the original tab group").ConfigureAwait(true);
        Console.WriteLine("Native toolbar, keyboard, menu, dock/tab, tear-away, redock, layout restore, and scoped wait checks passed.");
    }

    private static async Task SelectCallStackAsync(NativeInput input, Window window, IDockable registers, IDockable callStack, PixelRect workArea)
    {
        var tab = FindTab(window, callStack);
        var target = TabTitleCenter(tab, callStack);
        var hit = window.InputHitTest(window.PointToClient(target)) as Visual;
        Require(workArea.ContainsExclusive(target) && hit is not null && (ReferenceEquals(hit, tab) || tab.IsVisualAncestorOf(hit)),
            $"Call Stack point {target} misses the tab in work area {workArea} (tab bounds {tab.Bounds}, hit {hit?.GetType().Name ?? "none"}).");

        string? press = null;
        void ObservePress(object? _, PointerPressedEventArgs e)
        {
            var source = e.Source as Visual;
            press = source is not null && (ReferenceEquals(source, tab) || tab.IsVisualAncestorOf(source))
                ? "Call Stack"
                : source?.GetType().Name;
        }
        window.AddHandler(InputElement.PointerPressedEvent, ObservePress, RoutingStrategies.Tunnel, handledEventsToo: true);
        try
        {
            await input.ClickAsync(target).ConfigureAwait(true);
            await UntilAsync(() => Equals(callStack, (registers.Owner as IDock)?.ActiveDockable), "Call Stack tab selection",
                () => $"Point {target}, work area {workArea}, window active {window.IsActive}, " +
                    $"routed press {press ?? "none"}, tab hovered {tab.IsPointerOver}, " +
                    $"active dockable {(registers.Owner as IDock)?.ActiveDockable?.Id ?? "none"}.").ConfigureAwait(true);
        }
        finally { window.RemoveHandler(InputElement.PointerPressedEvent, ObservePress); }
    }

    private static async Task VerifyRestoredFloatAsync(WorkbenchSession session, Window window)
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var store = new LayoutStore(Path.Combine(folder.FullName, "layout.json"));
            store.Save(session.Layout.Capture());
            using var restored = new WorkbenchSession(session.View.WorkspaceId, store.Load());
            var restoredWindow = new WorkbenchWindow(restored, store) { Position = new PixelPoint(55, 65), Width = 850, Height = 550 };
            restoredWindow.Show();
            try
            {
                await UntilAsync(() => restored.Layout.Root.Windows is [{ Host: HostWindow { IsVisible: true } }], "native floating layout restoration").ConfigureAwait(true);
                Require(restored.Layout.IsFocused("registers"), "Restored floating pane must regain selection.");
            }
            finally
            {
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                restoredWindow.Closed += (_, _) => closed.SetResult();
                restoredWindow.Close();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(7)).ConfigureAwait(true);
            }
            Require(store.Load() is { Floating.Length: 1, ActiveTool: "registers" },
                "Closing through the production save path must keep the floating Registers layout.");
            window.Activate();
        }
        finally { folder.Delete(recursive: true); }
    }

    private static async Task VerifyWaitAsync(WorkbenchSession session, Window window, NativeInput input)
    {
        var registers = session.Layout.Tool("registers");
        Border? overlay = null;
        await UntilAsync(() => (overlay = window.GetVisualDescendants().OfType<Border>().SingleOrDefault(border =>
            string.Equals(border.Name, "InteractionSurface", StringComparison.Ordinal) &&
            ReferenceEquals(border.DataContext, registers) &&
            border.GetVisualParent() is Grid { IsEffectivelyVisible: true })) is not null, "Registers interaction surface");
        var region = (Grid)overlay!.GetVisualParent()!;
        var pane = (Control)region.Children[0];
        var point = Center(region);
        await input.MoveAsync(point).ConfigureAwait(true);
        var normal = input.ReadCursor();
        using var referenceWait = new Cursor(StandardCursorType.Wait);
        long wait;
        using (region.SetValue(InputElement.CursorProperty, referenceWait, Avalonia.Data.BindingPriority.Animation))
        {
            await input.MoveAsync(new PixelPoint(point.X + 1, point.Y)).ConfigureAwait(true);
            wait = input.ReadCursor();
            Require(wait != normal, "The platform's standard Wait cursor must differ from the normal Registers cursor.");
        }

        using var release = new ManualResetEventSlim();
        var occupied = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var block = session.Interaction.RunAsync("registers", _ =>
        {
            occupied.SetResult(Dispatcher.UIThread.CheckAccess());
            Require(release.Wait(TimeSpan.FromSeconds(12)), "Synchronous Registers operation timed out.");
            return Task.CompletedTask;
        });
        try
        {
            Require(!await occupied.Task.WaitAsync(TimeSpan.FromSeconds(7)).ConfigureAwait(true), "Registers operation entered on the UI dispatcher.");
            await input.MoveAsync(point).ConfigureAwait(true);
            Require(ReferenceEquals(window.InputHitTest(window.PointToClient(point)), overlay) && overlay.IsPointerOver,
                "The native pointer must hit the Registers block surface.");
            await UntilAsync(() => input.ReadCursor() == wait, "effective platform Wait cursor over Registers");
            Require(!pane.IsEffectivelyEnabled, "Registers content still accepts input.");
            if (window is WorkbenchWindow workbench)
            {
                Require(!workbench.FocusButton.IsEffectivelyEnabled, "Registers focus command must respect the interaction scope.");
                var picker = workbench.GetVisualDescendants().OfType<ComboBox>().Single();
                await input.ClickAsync(Center(picker)).ConfigureAwait(true);
                await UntilAsync(() => picker.IsDropDownOpen, "native input outside the blocked pane");
                Require(input.ReadCursor() != wait, "The available tool picker acquired the Wait cursor.");
                picker.IsDropDownOpen = false;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                await input.MoveAsync(Center(picker)).ConfigureAwait(true);
                await input.MoveAsync(point).ConfigureAwait(true);
                await UntilAsync(() => overlay.IsPointerOver && input.ReadCursor() == wait,
                    "Wait cursor upon returning to Registers",
                    () => $"Registers pointer-over: {overlay.IsPointerOver}, effective cursor: {input.ReadCursor()}, expected cursor: {wait}.");
            }
            Require(!block.IsCompleted, "The synchronous operation must still be running during native input.");
        }
        finally
        {
            release.Set();
            await block.ConfigureAwait(true);
        }

        await input.MoveAsync(new PixelPoint(point.X + 1, point.Y)).ConfigureAwait(true);
        await UntilAsync(() => input.ReadCursor() == normal, "effective platform cursor restored over Registers");
        Require(pane.IsEffectivelyEnabled && !overlay.IsVisible, "Registers did not regain input after the block.");
        Console.WriteLine($"Verified effective {input.Backend} Wait and restoration over {(window is HostWindow ? "floating" : "docked")} Registers.");
    }

    private static ToolTabStripItem FindTab(Window window, IDockable tool) => window.GetVisualDescendants()
        .OfType<ToolTabStripItem>().Single(tab => ReferenceEquals(tab.DataContext, tool));

    private static PixelPoint Tab(Window window, IDockable tool) => TabTitleCenter(FindTab(window, tool), tool);

    private static PixelPoint TabTitleCenter(ToolTabStripItem tab, IDockable tool) =>
        Center(tab.GetVisualDescendants().OfType<TextBlock>().First(text => Equals(text.Text, tool.Title)));

    private static PixelPoint Center(Control control) =>
        control.PointToScreen(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2));

    private static async Task UntilAsync(Func<bool> condition, string description, Func<string>? failureDetails = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(7));
        try
        {
            while (!condition())
                await Task.Delay(30, timeout.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException ex)
        {
            var details = failureDetails is null ? "" : " " + failureDetails();
            throw new InvalidOperationException($"Timed out waiting for {description}.{details}", ex);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
