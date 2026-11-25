using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using RetroIde.Application;

namespace RetroIde.Workbench;

/// <summary>The main desktop entry point with an MDI Dock surface and shared command bindings.</summary>
/// <remarks>TEST-RETRO-007#ac-test-toolkit-bindings</remarks>
public sealed class WorkbenchWindow : Window
{
    private readonly WorkbenchSession _session;
    private readonly LayoutStore? _layouts;
    private CancellationTokenSource? _closeSave;
    private bool _replayingClose;
    private bool _closed;

    /// <summary>Creates the desktop presentation for a live workbench session.</summary>
    public WorkbenchWindow(WorkbenchSession session, LayoutStore? layouts = null)
    {
        _session = session;
        _layouts = layouts;
        DataContext = session.View;
        Title = "Retro IDE";
        Width = 1200;
        Height = 780;
        MinWidth = 750;
        MinHeight = 480;

        CommandPresentation = CommandPresentation.Create(CommandPresentation.Focus(session.Registry));
        var focusCommand = session.View.FocusCommand;
        FocusMenu = new MenuItem
        {
            Header = CommandPresentation.Label,
            Icon = CreateIcon(),
            InputGesture = CommandPresentation.Gesture,
            Command = focusCommand,
        };
        AutomationProperties.SetAutomationId(FocusMenu, session.Registry.FocusTool.Id + ".menu");
        void UpdateFocusEligibility(object? _, EventArgs args) => FocusMenu.IsEnabled = focusCommand.CanExecute(null);
        focusCommand.CanExecuteChanged += UpdateFocusEligibility;
        UpdateFocusEligibility(null, EventArgs.Empty);
        Closed += (_, _) => focusCommand.CanExecuteChanged -= UpdateFocusEligibility;
        var viewMenu = new MenuItem { Header = "_View", ItemsSource = new[] { FocusMenu } };
        var menu = new Menu { ItemsSource = new[] { viewMenu } };

        FocusButtonLabel = new TextBlock { Text = CommandPresentation.Label };
        FocusButtonShortcut = new TextBlock { Text = CommandPresentation.Shortcut, Opacity = 0.62 };
        FocusButtonIcon = CreateIcon();
        FocusButton = new Button
        {
            Command = focusCommand,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { FocusButtonIcon, FocusButtonLabel, FocusButtonShortcut },
            },
        };
        AutomationProperties.SetAutomationId(FocusButton, session.Registry.FocusTool.Id + ".toolbar");
        AutomationProperties.SetName(FocusButton, CommandPresentation.Label);
        ToolTip.SetTip(FocusButton, $"{CommandPresentation.Label} ({CommandPresentation.Shortcut})");
        FocusShortcut = AddFocusShortcut(this);
        session.Layout.Factory.DefaultHostWindowLocator = () =>
        {
            var floating = new HostWindow();
            AddFocusShortcut(floating);
            TrackFloatingInteraction(floating);
            return floating;
        };
        // Dock initializes its locator when the layout is created, before the workbench binds commands.
        session.Layout.Factory.HostWindowLocator![nameof(Dock.Model.Core.IDockWindow)] = session.Layout.Factory.DefaultHostWindowLocator;

        Content = BuildSurface(session, menu, FocusButton);
        Opened += (_, _) => session.Layout.ShowFloatingLayouts();
        Closed += (_, _) =>
        {
            _closed = true;
            _closeSave?.Cancel();
            session.Controller.Unregister(session.View);
        };

        PathIcon CreateIcon() => new() { Width = 16, Height = 16, Data = CommandPresentation.IconGeometry };

        KeyBinding AddFocusShortcut(Window surface) => RegisterShortcut(surface, focusCommand, CommandPresentation.Gesture);
    }

    private void TrackFloatingInteraction(HostWindow floating)
    {
        floating.Closing += (_, args) =>
        {
            if (_closeSave is not null && args.CloseReason is not (WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown))
                args.Cancel = true;
        };
        floating.Opened += (_, _) =>
        {
            var interaction = _session.Interaction;
            var cursor = interaction.Attach(InteractionBlocks.WorkbenchScope, floating);
            IDisposable? blockedInput = null;
            interaction.Changed += UpdateInput;
            UpdateInput(null, EventArgs.Empty);
            floating.Closed += (_, _) =>
            {
                interaction.Changed -= UpdateInput;
                blockedInput?.Dispose();
                cursor.Dispose();
            };

            void UpdateInput(object? sender, EventArgs args)
            {
                if (interaction.IsBlocked(InteractionBlocks.WorkbenchScope))
                    blockedInput ??= floating.SetValue(InputElement.IsEnabledProperty, false, BindingPriority.Animation);
                else
                {
                    blockedInput?.Dispose();
                    blockedInput = null;
                }
            }
        };
    }

    internal static KeyBinding RegisterShortcut(Window surface, ICommand command, KeyGesture gesture)
    {
        if (surface.KeyBindings.Any(binding => Equals(binding.Gesture, gesture)))
            throw new InvalidOperationException($"Conflicting shortcut in the workbench scope: {gesture}");

        var shortcut = new KeyBinding { Command = command, Gesture = gesture };
        surface.KeyBindings.Add(shortcut);
        return shortcut;
    }

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs args)
    {
        base.OnClosing(args);
        if (args.Cancel || _layouts is null || _replayingClose ||
            args.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
            return; // A forced shutdown can proceed; Closed cancels any pending save.

        args.Cancel = true;
        if (_closeSave is null)
        {
            _closeSave = new CancellationTokenSource();
            _ = SaveLayoutAndCloseAsync(_layouts, _closeSave);
        }
    }

    private async Task SaveLayoutAndCloseAsync(LayoutStore store, CancellationTokenSource closeSave)
    {
        try
        {
            var snapshot = _session.Layout.Capture();
            await _session.Interaction.RunAsync(InteractionBlocks.WorkbenchScope, token =>
            {
                store.Save(snapshot, token);
                return Task.CompletedTask;
            }, closeSave.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (closeSave.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Unable to save the Retro IDE layout.");
        }
        finally
        {
            // A fast save or a synchronous failure can complete inside the original close callback.
            await Dispatcher.Yield(DispatcherPriority.Background);
            _closeSave = null;
            closeSave.Dispose();
        }

        if (_closed)
            return;

        _replayingClose = true;
        try { Close(); }
        finally { _replayingClose = false; }
    }

    private static DockPanel BuildSurface(WorkbenchSession session, Menu menu, Button focusButton)
    {
        var tools = session.Layout.Tools.ToArray();
        var picker = new ComboBox
        {
            ItemsSource = tools.Select(tool => tool.Title).ToArray(),
            SelectedIndex = Array.FindIndex(tools, tool => string.Equals(tool.Id, session.View.FocusTarget, StringComparison.Ordinal)),
            MinWidth = 162,
        };
        picker.SelectionChanged += (_, _) =>
            session.View.FocusTarget = picker.SelectedIndex >= 0 ? tools[picker.SelectedIndex].Id : null;

        var commandBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(10, 6),
            Children = { picker, focusButton },
        };

        var status = new TextBlock { Text = "No emulator connected", Margin = new Thickness(12, 4), TextTrimming = TextTrimming.CharacterEllipsis };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(WorkbenchViewModel.LastResult) + "." + nameof(OperationResult.Summary))
        {
            FallbackValue = "No emulator connected",
            TargetNullValue = "No emulator connected",
        });
        var dock = new DockControl
        {
            Layout = session.Layout.Root,
            Factory = session.Layout.Factory,
            InitializeLayout = false,
        };

        var shell = new DockPanel();
        DockPanel.SetDock(menu, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(commandBar, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(status, Avalonia.Controls.Dock.Bottom);
        shell.Children.Add(menu);
        shell.Children.Add(commandBar);
        shell.Children.Add(status);
        var dockRegion = session.Interaction.Wrap(InteractionBlocks.WorkbenchScope, dock);
        shell.Children.Add(dockRegion);
        return shell;
    }

    internal MenuItem FocusMenu { get; }
    internal Button FocusButton { get; }
    internal KeyBinding FocusShortcut { get; }
    internal TextBlock FocusButtonLabel { get; }
    internal TextBlock FocusButtonShortcut { get; }
    internal PathIcon FocusButtonIcon { get; }
    internal CommandPresentation CommandPresentation { get; }

    internal void ShowAttachmentPaths(string message)
    {
        var label = new TextBox { Text = message, IsReadOnly = true, BorderThickness = new Thickness(0), MaxHeight = 64, TextWrapping = TextWrapping.Wrap };
        var panel = (DockPanel)Content!;
        DockPanel.SetDock(label, Avalonia.Controls.Dock.Bottom);
        panel.Children.Insert(2, label);
    }
}
