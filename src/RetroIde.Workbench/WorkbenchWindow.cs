using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using RetroIde.Application;
using RetroIde.Application.Git;

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

        var localization = session.Localization;
        var focusDescriptor = WorkbenchCommands.FocusTool;
        CommandPresentation = CommandPresentation.Create(focusDescriptor, localization);
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
        var viewMenu = new MenuItem { ItemsSource = new[] { FocusMenu, CreateLanguageMenu(session) } };
        localization.Observe(viewMenu, () => viewMenu.Header = localization.Text(WorkbenchText.ViewMenu));
        var menu = new Menu
        {
            ItemsSource = new[] { ExplorerPane.CreateFileMenu(session.ExplorerView), viewMenu, SourceControlPane.CreateMenu(session.SourceControl) },
        };

        FocusButtonLabel = new TextBlock { Text = CommandPresentation.Label };
        FocusButtonShortcut = new TextBlock { Text = CommandPresentation.Shortcut, Opacity = 0.62 };
        FocusButtonIcon = CreateIcon();
        var translatedIcon = new Image { Width = 16, Height = 16, IsVisible = false };
        var iconFrame = new Grid { Children = { FocusButtonIcon, translatedIcon } };
        FocusButton = new Button
        {
            Command = focusCommand,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { iconFrame, FocusButtonLabel, FocusButtonShortcut },
            },
        };
        AutomationProperties.SetAutomationId(FocusButton, session.Registry.FocusTool.Id + ".toolbar");
        AutomationProperties.SetName(FocusButton, CommandPresentation.Label);
        ToolTip.SetTip(FocusButton, $"{CommandPresentation.Label} ({CommandPresentation.Shortcut})");
        localization.Observe(FocusButton, () =>
        {
            var presentation = CommandPresentation.Create(focusDescriptor, localization);
            CommandPresentation = presentation;
            FocusButtonLabel.Text = presentation.Label;
            FocusButtonShortcut.Text = presentation.Shortcut;
            FocusButtonIcon.Data = presentation.IconGeometry;
            FocusButtonIcon.IsVisible = presentation.Image is null;
            translatedIcon.Source = presentation.Image;
            translatedIcon.IsVisible = presentation.Image is not null;
            FocusMenu.Header = presentation.Label;
            FocusMenu.Icon = presentation.CreateIcon(16);
            AutomationProperties.SetName(FocusMenu, presentation.Label);
            AutomationProperties.SetName(FocusButton, presentation.Label);
            ToolTip.SetTip(FocusButton, $"{presentation.Label} ({presentation.Shortcut})");
        });
        ApplyLanguageDirection(this, localization);
        FocusShortcut = AddFocusShortcut(this);
        ExplorerPane.RegisterShortcuts(this, session.ExplorerView);
        SourceControlPane.RegisterShortcuts(this, session.SourceControl);
        session.ExplorerView.PickDirectoryAsync = PickWorkspaceDirectoryAsync;
        session.SourceControl.PromptSetupAsync = PromptGitSetupAsync;
        session.SourceControl.PromptCommitAsync = PromptGitCommitAsync;
        session.SourceControl.PromptRecoveryAsync = PromptGitRecoveryAsync;
        session.Layout.Factory.DefaultHostWindowLocator = () =>
        {
            var floating = new HostWindow();
            ApplyLanguageDirection(floating, localization);
            AddFocusShortcut(floating);
            ExplorerPane.RegisterShortcuts(floating, session.ExplorerView);
            SourceControlPane.RegisterShortcuts(floating, session.SourceControl);
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
            session.ExplorerView.PickDirectoryAsync = null;
            session.SourceControl.PromptSetupAsync = null;
            session.SourceControl.PromptCommitAsync = null;
            session.SourceControl.PromptRecoveryAsync = null;
            session.Controller.Unregister(session.View);
        };

        PathIcon CreateIcon() => new() { Width = 16, Height = 16, Data = CommandPresentation.IconGeometry };

        KeyBinding AddFocusShortcut(Window surface) => RegisterShortcut(surface, focusCommand, CommandPresentation.Gesture);
    }

    private static void ApplyLanguageDirection(Window window, WorkbenchLocalization localization)
    {
        Apply(null, EventArgs.Empty);
        window.Opened += (_, _) =>
        {
            localization.Changed += Apply;
            Apply(null, EventArgs.Empty);
        };
        window.Closed += (_, _) => localization.Changed -= Apply;

        void Apply(object? sender, EventArgs args) => window.FlowDirection = localization.State.RightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
    }

    private static MenuItem CreateLanguageMenu(WorkbenchSession session)
    {
        var localization = session.Localization;
        var menu = new MenuItem
        {
            ItemsSource = localization.State.AvailableLocales.Select(tag =>
            {
                var culture = System.Globalization.CultureInfo.GetCultureInfo(tag);
                var entry = new MenuItem { Header = culture.NativeName, Command = session.View.SetDisplayLanguageCommand, CommandParameter = tag };
                AutomationProperties.SetAutomationId(entry, "workbench.language." + tag);
                return entry;
            }).ToArray(),
        };
        localization.Observe(menu, () => menu.Header = localization.Text(WorkbenchText.LanguageMenu));
        return menu;
    }

    private async Task<string?> PickWorkspaceDirectoryAsync(CancellationToken token)
    {
        if (token.IsCancellationRequested || !StorageProvider.CanPickFolder)
            return null;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()).ConfigureAwait(true);
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<GitSetupDialog.Choice?> PromptGitSetupAsync(bool clone, CancellationToken token)
    {
        if (token.IsCancellationRequested || _closed)
            return null;

        var dialog = new GitSetupDialog(_session.Localization, clone) { FlowDirection = FlowDirection };
        using var cancel = token.Register(() => Dispatcher.UIThread.Post(() =>
        {
            if (dialog.IsVisible)
                dialog.Close();
        }));
        return await dialog.ShowDialog<GitSetupDialog.Choice?>(this).ConfigureAwait(true);
    }

    private async Task<string?> PromptGitCommitAsync(GitCommitOperations.CommitState state, bool amend, CancellationToken token)
    {
        if (token.IsCancellationRequested || _closed)
            return null;

        var dialog = new GitCommitDialog(_session.Localization, state, amend) { FlowDirection = FlowDirection };
        using var cancel = token.Register(() => Dispatcher.UIThread.Post(() =>
        {
            if (dialog.IsVisible)
                dialog.Close();
        }));
        return await dialog.ShowDialog<string?>(this).ConfigureAwait(true);
    }

    private async Task<bool> PromptGitRecoveryAsync(GitOperationRecovery.State state, GitOperationRecovery.Action action, CancellationToken token)
    {
        if (token.IsCancellationRequested || _closed)
            return false;
        var dialog = new GitRecoveryDialog(_session.Localization, state, action) { FlowDirection = FlowDirection };
        using var cancel = token.Register(() => Dispatcher.UIThread.Post(() =>
        {
            if (dialog.IsVisible)
                dialog.Close(false);
        }));
        return await dialog.ShowDialog<bool>(this).ConfigureAwait(true);
    }

    private void TrackFloatingInteraction(HostWindow floating)
    {
        floating.Closing += (_, args) =>
        {
            if (_closeSave is not null && args.CloseReason is not
                (WindowCloseReason.OwnerWindowClosing or WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown))
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
        if (args.CloseReason is not (WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown) &&
            _session.Documents.HasUnsavedDocuments)
        {
            args.Cancel = true;
            _session.View.Receive(OperationResult.Failure(Guid.NewGuid(), "unsaved", "Save open documents before closing Retro IDE."));
            return;
        }

        if (args.Cancel || (_layouts is null && _session.ExplorerStore is null) || _replayingClose ||
            args.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
            return; // A forced shutdown can proceed; Closed cancels any pending save.

        args.Cancel = true;
        if (_closeSave is null)
        {
            _closeSave = new CancellationTokenSource();
            _ = SaveStateAndCloseAsync(_closeSave);
        }
    }

    private async Task SaveStateAndCloseAsync(CancellationTokenSource closeSave)
    {
        var interaction = _session.Interaction;
        interaction.Begin(InteractionBlocks.WorkbenchScope);
        try
        {
            var token = closeSave.Token;
            await _session.Controller.DrainGitRefreshAsync(_session.View).ConfigureAwait(true);
            if (_layouts is { } layouts)
            {
                var snapshot = _session.Layout.Capture();
                await SaveStateAsync(() => layouts.Save(snapshot, token), "layout", token).ConfigureAwait(true);
            }
            if (_session.ExplorerStore is { } workspace)
            {
                var snapshot = _session.Explorer.Snapshot();
                await SaveStateAsync(() => workspace.Save(snapshot, token), "workspace state", token).ConfigureAwait(true);
            }

            // Leave the original Closing callback before replaying it. Hold the block
            // until the replay completes so the saved dock layout cannot be edited in between.
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (_closed)
                return;

            _replayingClose = true;
            try { Close(); }
            finally { _replayingClose = false; }
        }
        finally
        {
            _closeSave = null;
            closeSave.Dispose();
            interaction.End(InteractionBlocks.WorkbenchScope);
        }
    }

    private static async Task SaveStateAsync(Action save, string name, CancellationToken token)
    {
        try { await Task.Run(save, token).ConfigureAwait(true); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Unable to save the Retro IDE {name}.");
        }
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
        var refreshing = false;
        picker.SelectionChanged += (_, _) =>
        {
            if (!refreshing)
                session.View.FocusTarget = picker.SelectedIndex >= 0 ? tools[picker.SelectedIndex].Id : null;
        };
        session.Localization.Observe(picker, () =>
        {
            refreshing = true;
            try
            {
                picker.ItemsSource = tools.Select(tool => session.Localization.Text(WorkbenchText.PaneTitle(tool.Id))).ToArray();
                picker.SelectedIndex = Array.FindIndex(tools, tool => string.Equals(tool.Id, session.View.FocusTarget, StringComparison.Ordinal));
                AutomationProperties.SetName(picker, session.Localization.Text(WorkbenchText.FocusSelector));
            }
            finally { refreshing = false; }
        });

        var commandBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(10, 6),
            Children = { picker, focusButton },
        };

        var status = new TextBlock { Margin = new Thickness(12, 4), TextTrimming = TextTrimming.CharacterEllipsis };
        IDisposable? statusBinding = null;
        session.Localization.Observe(status, () =>
        {
            var fallback = session.Localization.Text(WorkbenchText.NoEmulator);
            statusBinding?.Dispose();
            statusBinding = status.Bind(TextBlock.TextProperty,
                new Binding(nameof(WorkbenchViewModel.LastResult) + "." + nameof(OperationResult.Summary))
                {
                    FallbackValue = fallback,
                    TargetNullValue = fallback,
                });
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
    internal CommandPresentation CommandPresentation { get; private set; }

    internal void ShowAttachmentPaths(string message)
    {
        var label = new TextBox { Text = message, IsReadOnly = true, BorderThickness = new Thickness(0), MaxHeight = 64, TextWrapping = TextWrapping.Wrap };
        var panel = (DockPanel)Content!;
        DockPanel.SetDock(label, Avalonia.Controls.Dock.Bottom);
        panel.Children.Insert(2, label);
    }
}
